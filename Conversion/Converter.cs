﻿namespace Trello2GitLab.Conversion;

using GitLab;
using System.Linq;
using Trello;

public sealed class Converter : IDisposable
{
	private const int TITLE_MAX_LENGTH = 255;
	private const int DESCRIPTION_MAX_LENGTH = 1048576;

	private readonly ConverterOptions _options;
	private readonly TrelloApi trello;
	private readonly GitLabApi gitlab;
	private readonly AssociationsOptions associations;
	private bool isDisposed;
	private Board trelloBoard;

	/// <summary>
	/// Creates a new converter using the options provided.
	/// </summary>
	/// <param name="options">The converter options.</param>
	/// <exception cref="ArgumentNullException"></exception>
	public Converter(ConverterOptions options)
	{
		_options = options;
		CheckOptions(options);

		trello = new(options.Trello);

		gitlab = new(options.GitLab);

		associations = options.Associations;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing)
	{
		if (isDisposed)
			return;

		if (disposing)
		{
			trello?.Dispose();
			gitlab?.Dispose();
		}

		isDisposed = true;
	}

	/// <summary>
	/// Checks converter options validity.
	/// </summary>
	/// <param name="options">The converter options to check.</param>
	protected static void CheckOptions(ConverterOptions options)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options), "Missing converter options.");

		if (options.Trello == null)
			throw new ArgumentNullException(nameof(ConverterOptions.Trello), "Missing Trello options.");

		if (options.GitLab == null)
			throw new ArgumentNullException(nameof(ConverterOptions.GitLab), "Missing GitLab options.");

		if (options.Associations == null)
			throw new ArgumentNullException(nameof(ConverterOptions.Associations), "Missing Associations options.");

		if (string.IsNullOrEmpty(options.Trello.Key))
			throw new ArgumentNullException(nameof(ConverterOptions.Trello.Key), "Missing Trello key.");

		if (string.IsNullOrEmpty(options.Trello.Token))
			throw new ArgumentNullException(nameof(ConverterOptions.Trello.Token), "Missing Trello token.");

		if (string.IsNullOrEmpty(options.Trello.BoardId))
			throw new ArgumentNullException(nameof(ConverterOptions.Trello.BoardId), "Missing Trello board ID.");

		if (!new string[] { "all", "open", "visible", "closed" }.Contains(options.Trello.Include))
			throw new ArgumentException("Valid values are: 'all', 'open', 'visible' or 'closed'", nameof(ConverterOptions.Trello.Include));

		if (string.IsNullOrEmpty(options.GitLab.Token))
			throw new ArgumentNullException(nameof(ConverterOptions.GitLab.Token), "Missing GitLab token.");

		if (options.GitLab.ProjectId == default)
			throw new ArgumentNullException(nameof(ConverterOptions.GitLab.ProjectId), "Missing GitLab project ID.");
	}

	/// <summary>
	/// Converts all Trello cards to GitLab issues.
	/// </summary>
	/// <param name="progress">A progress update provider.</param>
	public async Task<bool> ConvertAll(IProgress<ConversionProgressReport> progress)
	{
		progress.Report(new(ConversionStep.Init));

		// Fetch Trello board.

		progress.Report(new(ConversionStep.FetchingTrelloBoard));

		trelloBoard = await trello.GetBoard();

		var totalCards = trelloBoard.Cards.Count;

		progress.Report(new(ConversionStep.TrelloBoardFetched));

		// Grant admin privileges if Sudo option provided.

		var nonAdminUsers = new List<User>();

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.GrantAdminPrivileges));

			var users = await gitlab.GetAllUsers();

			nonAdminUsers.AddRange(users.Where(u => !u.IsAdmin).Join(associations.Members_Users, u => u.Id, au => au.Value, (u, _) => u));

			await SetUserAdminPrivileges(nonAdminUsers, true, progress, ConversionStep.GrantAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesGranted));
		}

		// Fetch project milestones and convert iid to global id.

		if (associations.Labels_Milestones.Count != 0 || associations.Lists_Milestones.Count != 0)
		{
			progress.Report(new(ConversionStep.FetchMilestones));

			var milestones = await gitlab.GetAllMilestones();

			var totalMilestones = associations.Labels_Milestones.Count + associations.Lists_Milestones.Count;
			var i = 0;

			var labelsMilestones = new Dictionary<string, int>();
			foreach (var labelMilestone in associations.Labels_Milestones)
			{
				progress.Report(new(ConversionStep.FetchMilestones, i, totalMilestones));

				var milestone = milestones.FirstOrDefault(m => m.Iid == labelMilestone.Value);

				if (milestone != null)
				{
					labelsMilestones[labelMilestone.Key] = milestone.Id;
				}
				else
				{
					progress.Report(new(ConversionStep.FetchMilestones, i, totalMilestones, new string[] { $"Error while fetching milestone: milestone with iid '{labelMilestone.Value}' not found on project" }));
				}

				i++;
			}
			associations.Labels_Milestones = labelsMilestones;

			var listsMilestones = new Dictionary<string, int>();
			foreach (var listMilestone in associations.Lists_Milestones)
			{
				progress.Report(new(ConversionStep.FetchMilestones, i, totalMilestones));

				var milestone = milestones.FirstOrDefault(m => m.Iid == listMilestone.Value);

				if (milestone != null)
				{
					listsMilestones[listMilestone.Key] = milestone.Id;
				}
				else
				{
					progress.Report(new(ConversionStep.FetchMilestones, i, totalMilestones, new string[] { $"Error while fetching milestone: milestone with iid '{listMilestone.Value}' not found on project" }));
				}

				i++;
			}
			associations.Lists_Milestones = listsMilestones;

			progress.Report(new(ConversionStep.MilestonesFetched));
		}

		// Convert all cards.

		for (var i = 0; i < totalCards; i++)
		{
			progress.Report(new(ConversionStep.ConvertingCards, i, totalCards));

			var errors = await Convert(trelloBoard.Cards[i]);

			if (errors.Count != 0)
			{
				progress.Report(new(ConversionStep.ConvertingCards, i, totalCards, errors));
			}
		}

		progress.Report(new(ConversionStep.CardsConverted));

		// Revoke admin privileges (of non admin users) if Sudo option provided.

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.RevokeAdminPrivileges));

			await SetUserAdminPrivileges(nonAdminUsers, false, progress, ConversionStep.RevokeAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesRevoked));
		}

		progress.Report(new(ConversionStep.Finished, totalCards, totalCards));

		return true;
	}

	/// <summary>
	/// Sets GitLab users admin privileges.
	/// </summary>
	/// <param name="admin">User is admin.</param>
	/// <param name="progress">A progress update provider.</param>
	/// <param name="step">The step doing this action.</param>
	protected async Task SetUserAdminPrivileges(IReadOnlyList<User> users, bool admin, IProgress<ConversionProgressReport> progress, ConversionStep step)
	{
		for (var i = 0; i < users.Count; i++)
		{
			progress.Report(new(step, i, users.Count));

			var user = users[i];

			try
			{
				await gitlab.EditUser(new() { Id = user.Id, Admin = admin });
			}
			catch (ApiException exception)
			{
				progress.Report(new(step, i, users.Count, new string[] { $"Error while {(admin ? "granting" : "revoking")} admin privilege: {exception.Message}\nUser: {user.Id} ({user.Username})" }));
			}
		}
	}

	private sealed class AttachmentMapping
	{
		public Attachment TrelloAttachment { get; set; }
		public Upload GitlabAttachment { get; set; }

		/// <summary>
		/// Merken, wenn verarbeitet wurde.
		/// </summary>
		public bool DidReplace { get; set; }
	}

	/// <summary>
	/// Converts a Trello card to a GitLab issue.
	/// </summary>
	protected async Task<IReadOnlyList<string>> Convert(Card card)
	{
		var errors = new List<string>();

		// Finds basic infos.

		var createAction = FindCreateCardAction(card);
		var createdBy = FindAssociatedUserId(createAction?.IdMemberCreator);

		var assignees = card.IdMembers?.Select(m => FindAssociatedUserId(m)).Where(u => u != null).Cast<int>();

		var labels = GetCardAssociatedLabels(card);

		var attachments = await GetCardAttachments(card);

		// Creates GitLab issue.

		Issue issue = null;
		string description = null;

		// Verlinkungen anpassen.
		var attachmentUrlMappings = new List<AttachmentMapping>();

		try
		{
			foreach (var attachment in attachments)
			{
				var fileName = Path.GetFileName(attachment.Url);
				var bytes = await trello.DownloadAttachment(attachment.Url);

				var tempFolderPath = Path.Combine(Path.GetTempPath(),
					$@"Trello2GitLab-{DateTime.Now.Ticks}-{Guid.NewGuid():N}");
				try
				{
					Directory.CreateDirectory(tempFolderPath);
					var tempFilePath = Path.Combine(tempFolderPath, fileName);
					await File.WriteAllBytesAsync(tempFilePath, bytes);

					var gitlabAttachment =
						await gitlab.UploadFile(tempFilePath, attachment.MimeType);

					attachmentUrlMappings.Add(new()
					{
						TrelloAttachment = attachment,
						GitlabAttachment = gitlabAttachment
					});
				}
				finally
				{
					Directory.Delete(tempFolderPath, true);
				}
			}

			description = GetCardDescriptionWithChecklists(card).Truncate(DESCRIPTION_MAX_LENGTH);
			description = replaceAttachments(description, attachmentUrlMappings, false);
			description = replaceMentions(description);

			issue = await gitlab.CreateIssue(new()
				{
					CreatedAt = createAction?.Date ?? card.DateLastActivity,
					Title = card.Name.Truncate(TITLE_MAX_LENGTH),
					Description = description,
					Labels = labels != null ? string.Join(',', labels) : null,
					AssisgneeIds = assignees,
					DueDate = card.Due,
					MilestoneId = GetCardAssociatedMilestone(card),
				},
				createdBy
			);
		}
		catch (ApiException exception)
		{
			errors.Add(GetErrorMessage("creating issue", exception));
			return errors;
		}

		// Create GitLab issue's comments.

		foreach (var commentAction in FindCommentActions(card))
		{
			try
			{
				await gitlab.CommentIssue(
					issue,
					new()
					{
						Body = replaceMentions(replaceAttachments(commentAction.Data.Text, attachmentUrlMappings, false)),
						CreatedAt = commentAction.Date,
					},
					FindAssociatedUserId(commentAction.IdMemberCreator)
				);
			}
			catch (ApiException exception)
			{
				errors.Add(GetErrorMessage("creating issue comment", exception));
			}
		}

		// Append non-referenced attachments to the Issue description as Markdown.
		if (attachmentUrlMappings.Any(m => !m.DidReplace))
		{
			description = replaceAttachments(description, attachmentUrlMappings, true);

			try
			{
				var editedIssue = await gitlab.EditIssueDescription(
					issue.Iid,
					new()
					{
						Description = description,
					});

				Console.WriteLine(editedIssue.Id);
			}
			catch (ApiException exception)
			{
				errors.Add(GetErrorMessage("appending non-referenced attachments", exception));
			}
		}

		// Closes issue if the card or the list is closed.

		Trello.Action closeAction = null;

		if (card.Closed)
		{
			closeAction = FindCloseCardAction(card);
		}
		else
		{
			var list = trelloBoard.Lists.FirstOrDefault(l => l.Id == card.IdList);

			if (list?.Closed == true)
			{
				list.CloseAction ??= FindCloseListAction(list);
				closeAction = list.CloseAction;
			}
		}

		if (closeAction != null)
		{
			try
			{
				await gitlab.EditIssue(
					new()
					{
						IssueIid = issue.Iid,
						StateEvent = "close",
						UpdatedAt = closeAction.Date,
					},
					FindAssociatedUserId(closeAction.IdMemberCreator)
				);
			}
			catch (ApiException exception)
			{
				errors.Add(GetErrorMessage("closing issue", exception));
			}
		}

		return errors;

		string GetErrorMessage(string actionContext, Exception exception)
		{
			var issueInfos = issue != null ? $"\nIssue: {issue.Id} (#{issue.Iid})" : "";
			return $"Error while {actionContext}: {exception.Message}\nCard: {card.Id}{issueInfos}";
		}
	}

	private string replaceMentions(string text)
	{
		if (string.IsNullOrEmpty(text)) return text;

		foreach (var mention in associations.Mentions)
		{
			var trelloMention = $@"{mention.Key.Trim('@')}";
			var gitlabMention = $"@{mention.Value.Trim('@')}";

			text = Regex.Replace(text, $@"@\b{Regex.Escape(trelloMention)}\b", gitlabMention, RegexOptions.IgnoreCase);
		}

		return text;
	}

	/// <summary>
	/// Modify attachment URLs from Trello to GitLab.
	/// </summary>
	private string replaceAttachments(
		string text, 
		List<AttachmentMapping> attachmentMappings,
		bool appendNonReferenced)
	{
		if (string.IsNullOrEmpty(text)) return text;

		foreach (var mapping in attachmentMappings)
		{
			if (text.Contains(mapping.TrelloAttachment.Url))
			{
				text = text.Replace(mapping.TrelloAttachment.Url, mapping.GitlabAttachment.Url);
				mapping.DidReplace = true;
			}
		}

		// Append non-referenced attachments as Markdown.
		if (appendNonReferenced)
		{
			var notReplacedAttachments = attachmentMappings.Where(m => !m.DidReplace).ToList();
			if (notReplacedAttachments.Count > 0)
			{
				text += "\n\n### Attachments\n\n";
				foreach (var notReplacedAttachment in notReplacedAttachments)
				{
					text += $"- {notReplacedAttachment.GitlabAttachment.Markdown}\n";
				}
			}
		}

		return text;
	}

	private async Task<IEnumerable<Attachment>> GetCardAttachments(Card card)
	{
		return await trello.GetAttachment(card.Id);
	}

	/// <summary>
	/// Gets the description of a given Trello card, and adds checklists.
	/// </summary>
	protected string GetCardDescriptionWithChecklists(Card card)
	{
		var description = card.Desc ?? "";

		foreach (var checklist in FindChecklists(card))
		{
			description += $"\n\n### {checklist.Name}\n\n";

			foreach (var checkItem in checklist.CheckItems)
			{
				description += $"- [{(checkItem.State == "complete" ? "x" : " ")}] {checkItem.Name}\n";
			}
		}

		return description;
	}

	/// <summary>
	/// Gets all associated labels from a given Trello card and its list.
	/// </summary>
	protected List<string> GetCardAssociatedLabels(Card card)
	{
		string gitlabLabel;
		var labels = new List<string>();

		foreach (var idLabel in card.IdLabels)
		{
			if (associations.Labels_Labels.TryGetValue(idLabel, out gitlabLabel))
			{
				labels.Add(gitlabLabel);
			}
		}

		if (associations.Lists_Labels.TryGetValue(card.IdList, out gitlabLabel))
		{
			labels.Add(gitlabLabel);
		}

		return labels;
	}

	/// <summary>
	/// Gets the associated milestone ID from a given Trello card, using list ID or labels.
	/// </summary>
	protected int? GetCardAssociatedMilestone(Card card)
	{
		if (associations.Lists_Milestones.TryGetValue(card.IdList, out var milestone))
		{
			return milestone;
		}

		if (card.IdLabels != null)
		{
			return associations.Labels_Milestones?.Join(card.IdLabels, lm => lm.Key, l => l, (lm, l) => lm.Value).FirstOrDefault();
		}

		return null;
	}

	/// <summary>
	/// Finds all checklists of a given Trello card.
	/// </summary>
	protected IEnumerable<Checklist> FindChecklists(Card card)
	{
		return trelloBoard.Checklists.Where(c => c.IdCard == card.Id);
	}

	/// <summary>
	/// Finds the create action of a given Trello card.
	/// </summary>
	protected Trello.Action FindCreateCardAction(Card card)
	{
		return trelloBoard.Actions.FirstOrDefault(a =>
			a.Type == "createCard"
			&& a.Data.Card.Id == card.Id
		);
	}

	/// <summary>
	/// Finds the close action of a given Trello card.
	/// </summary>
	protected Trello.Action FindCloseCardAction(Card card)
	{
		return trelloBoard.Actions.LastOrDefault(a =>
			a.Type == "updateCard"
			&& a.Data.Card.Id == card.Id
			&& a.Data.Old.TryGetValue("closed", out var closed)
			&& !(bool)closed
		);
	}

	/// <summary>
	/// Finds the close action of a given Trello list.
	/// </summary>
	protected Trello.Action FindCloseListAction(List list)
	{
		return trelloBoard.Actions.LastOrDefault(a =>
			a.Type == "updateList"
			&& a.Data.List.Id == list.Id
			&& a.Data.Old.TryGetValue("closed", out var closed)
			&& !(bool)closed
		);
	}

	/// <summary>
	/// Finds all comment actions of a given Trello card.
	/// </summary>
	protected IEnumerable<Trello.Action> FindCommentActions(Card card)
	{
		return trelloBoard.Actions.Where(a =>
			a.Type == "commentCard"
			&& a.Data.Card.Id == card.Id
		);
	}

	/// <summary>
	/// Finds all checklists of a given Trello card.
	/// </summary>
	protected int? FindAssociatedUserId(string trelloMemberId)
	{
		if (string.IsNullOrEmpty(trelloMemberId))
			return null;

		if (!associations.Members_Users.TryGetValue(trelloMemberId, out var gitlabUserId))
			return null;

		return gitlabUserId;
	}

	/// <summary>
	/// Deletes all GitLab project issues.
	/// </summary>
	/// <remarks>
	/// Used for internal tests.
	/// </remarks>
	public async Task DeleteAllIssues(int idGreaterThan)
	{
		await gitlab.DeleteAllIssues(idGreaterThan);
	}
}