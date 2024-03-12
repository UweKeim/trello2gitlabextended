namespace Trello2GitLab.Conversion;

using GitLab;
using System;
using System.Linq;
using Trello;

public sealed class Converter : IDisposable
{
	private const int TITLE_MAX_LENGTH = 255;
	private const int DESCRIPTION_MAX_LENGTH = 1048576;

	private readonly TrelloApi trello;
	private readonly GitLabApi gitlab;
	private readonly AssociationsOptions associations;
	private readonly ConverterOptions options;
	private bool isDisposed;
	private Board? trelloBoard;

	/// <summary>
	/// Creates a new converter using the options provided.
	/// </summary>
	/// <param name="options">The converter options.</param>
	/// <exception cref="ArgumentNullException"></exception>
	public Converter(ConverterOptions options)
	{
		CheckOptions(options);

		trello = new(options.Trello);

		gitlab = new(options.GitLab);

		associations = options.Associations;

		this.options = options;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~Converter()
	{
		Dispose(false);
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
			throw new ArgumentNullException(nameof(ConverterOptions.Trello.BoardId),
				"Missing Trello boardCustomFields ID.");

		if (!new[] { "all", "open", "visible", "closed" }.Contains(options.Trello.Include))
			throw new ArgumentException("Valid values are: 'all', 'open', 'visible' or 'closed'",
				nameof(ConverterOptions.Trello.Include));

		if (string.IsNullOrEmpty(options.GitLab.Token))
			throw new ArgumentNullException(nameof(ConverterOptions.GitLab.Token), "Missing GitLab token.");

		if (options.GitLab.ProjectId == default)
			throw new ArgumentNullException(nameof(ConverterOptions.GitLab.ProjectId), "Missing GitLab project ID.");
	}

	/// <summary>
	/// Replace the links in descriptions and comments that point to other Trello cards
	/// with links to the respective GitLab issues.
	/// </summary>
	/// <param name="progress">A progress update provider.</param>
	public async Task<bool> ReplaceTrelloLinks(IProgress<ConversionProgressReport> progress)
	{
		progress.Report(new("Starting replacing Trello card links with GitLab issue links."));

		// --
		// Grant admin privileges if Sudo option provided.

		var nonAdminUsers = new List<User>();

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.GrantAdminPrivileges));

			var users = await gitlab.GetAllUsers();

			nonAdminUsers.AddRange(users.Where(u => !u.IsAdmin)
				.Join(associations.Members_Users, u => u.Id, au => au.Value, (u, _) => u));

			await SetUserAdminPrivileges(nonAdminUsers, true, progress, ConversionStep.GrantAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesGranted));
		}

		// --

		var adjustCount = 0;

		progress.Report(new(ConversionStep.FetchingTrelloBoard));

		trelloBoard = await trello.GetBoard();

		var totalCards = trelloBoard.Cards.Count;

		progress.Report(new(ConversionStep.TrelloBoardFetched));

		// --

		var allIssues = await gitlab.GetAllIssues();

		for (var i = 0; i < totalCards; i++)
		{
			progress.Report(
				new($"{i + 1}/{totalCards} Replacing Trello card links with GitLab issue links."));

			var card = trelloBoard.Cards[i];
			if (!wantIncludeCard(card))
			{
				progress.Report(new($"    => Skipping Trello card #{card.ShortLink}."));
				continue;
			}

			var issue = await findGitLabIssueForTrelloCard(gitlab, card, allIssues);
			if (issue == null) continue;

			// If here, the issue is not yet associated with the Trello card.

			var did = await replaceTrelloLinks(issue, allIssues, progress);

			if (did)
			{
				progress.Report(new($"    => Replaced Trello links in issue #{issue.Iid}."));
				adjustCount++;
			}
		}


		// --
		// Revoke admin privileges (of non admin users) if Sudo option provided.

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.RevokeAdminPrivileges));

			await SetUserAdminPrivileges(nonAdminUsers, false, progress, ConversionStep.RevokeAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesRevoked));
		}

		// --

		progress.Report(new($"Starting replacing Trello card links with GitLab issue links."));
		return true;
	}

	public async Task<bool> AssocitateWithTrello(IProgress<ConversionProgressReport> progress)
	{
		progress.Report(new("Starting associate non-associated GitLab issues with their original Trello card."));

		// --
		// Grant admin privileges if Sudo option provided.

		var nonAdminUsers = new List<User>();

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.GrantAdminPrivileges));

			var users = await gitlab.GetAllUsers();

			nonAdminUsers.AddRange(users.Where(u => !u.IsAdmin)
				.Join(associations.Members_Users, u => u.Id, au => au.Value, (u, _) => u));

			await SetUserAdminPrivileges(nonAdminUsers, true, progress, ConversionStep.GrantAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesGranted));
		}

		// --

		var adjustCount = 0;

		progress.Report(new(ConversionStep.FetchingTrelloBoard));

		trelloBoard = await trello.GetBoard();

		var totalCards = trelloBoard.Cards.Count;

		progress.Report(new(ConversionStep.TrelloBoardFetched));

		// --

		var allIssues = await gitlab.GetAllIssues();

		for (var i = 0; i < totalCards; i++)
		{
			progress.Report(
				new($"{i + 1}/{totalCards} Checking and associating issue with card (if not yet associated)."));

			var card = trelloBoard.Cards[i];
			if (!wantIncludeCard(card))
			{
				progress.Report(new($"    => Skipping Trello card #{card.ShortLink}."));
				continue;
			}

			var issue = await findGitLabIssueForTrelloCardInNotes(gitlab, card, allIssues);
			if (issue != null) continue;

			issue = await findGitLabIssueForTrelloCard(gitlab, card, allIssues);
			if (issue == null) continue;

			// If here, the issue is not yet associated with the Trello card.

			var createAction = FindCreateCardAction(card);
			var createdBy = FindAssociatedUserId(createAction?.IdMemberCreator);

			await associateIssueWithTrelloCard(card, issue, createdBy);

			progress.Report(new($"    => Added Trello association to issue #{issue.Iid}."));
			adjustCount++;
		}


		// --
		// Revoke admin privileges (of non admin users) if Sudo option provided.

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.RevokeAdminPrivileges));

			await SetUserAdminPrivileges(nonAdminUsers, false, progress, ConversionStep.RevokeAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesRevoked));
		}

		// --

		progress.Report(new($"Finished associating {adjustCount} GitLab issues with their original Trello card."));
		return true;
	}

	/// <summary>
	/// Move the custom fields from Trello to GitLab.
	/// </summary>
	/// <param name="progress">A progress update provider.</param>
	public async Task<bool> MoveCustomFields(IProgress<ConversionProgressReport> progress)
	{
		progress.Report(new("Starting to move custom fields."));

		// --
		// Grant admin privileges if Sudo option provided.

		var nonAdminUsers = new List<User>();

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.GrantAdminPrivileges));

			var users = await gitlab.GetAllUsers();

			nonAdminUsers.AddRange(users.Where(u => !u.IsAdmin)
				.Join(associations.Members_Users, u => u.Id, au => au.Value, (u, _) => u));

			await SetUserAdminPrivileges(nonAdminUsers, true, progress, ConversionStep.GrantAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesGranted));
		}

		// --

		var adjustCount = 0;

		progress.Report(new(ConversionStep.FetchingTrelloBoard));

		trelloBoard = await trello.GetBoard();

		var totalCards = trelloBoard.Cards.Count;

		progress.Report(new(ConversionStep.TrelloBoardFetched));

		// --

		var boardCustomFields = await trello.GetAllCustomFields();

		var allIssues = await gitlab.GetAllIssues();

		for (var i = 0; i < totalCards; i++)
		{
			var any = false;

			progress.Report(new($"{i + 1}/{totalCards} Checking and moving custom fields (if not yet moved)."));

			var card = trelloBoard.Cards[i];
			if (!wantIncludeCard(card))
			{
				progress.Report(new($"    => Skipping Trello card #{card.ShortLink}."));
				continue;
			}

			var cardCustomFieldItems = await trello.GetCardCustomFieldItems(trelloBoard.Cards[i].Id);
			if (cardCustomFieldItems.Count == 0) continue;

			var issue = await findGitLabIssueForTrelloCard(gitlab, card, allIssues);
			if (issue == null) continue;

			var descriptionAdjusted =
				await checkAddCustomFields(boardCustomFields, cardCustomFieldItems, issue.Description);

			if (!string.IsNullOrEmpty(descriptionAdjusted) && !string.Equals(descriptionAdjusted, issue.Description))
			{
				try
				{
					await gitlab.EditIssueDescription(
						issue.Iid,
						new()
						{
							Description = descriptionAdjusted,
						});

					any = true;
				}
				catch (ApiException exception)
				{
					progress.Report(new(
						$"Error while editing issue description: {exception.Message}\nIssue: {issue.Id} (#{issue.Iid})"));
				}
			}

			if (any)
			{
				progress.Report(new("    => Added custom fields."));
				adjustCount++;
			}
		}

		// --
		// Revoke admin privileges (of non admin users) if Sudo option provided.

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.RevokeAdminPrivileges));

			await SetUserAdminPrivileges(nonAdminUsers, false, progress, ConversionStep.RevokeAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesRevoked));
		}

		// --

		progress.Report(new($"Finished moving custom fields for {adjustCount} cards."));
		return true;
	}

	private readonly Dictionary<int, IReadOnlyList<IssueNote>> _cacheForGitLabIssueNotes = new();

	private async Task<Issue?> findGitLabIssueForTrelloCardInNotes(GitLabApi gitLabApi, Card card,
		IReadOnlyList<Issue> allIssues)
	{
		// First look for the card ID in a comment.
		foreach (var issue in allIssues)
		{
			// Cache to speed up things.
			var notes = _cacheForGitLabIssueNotes.GetValueOrDefault(issue.Id);
			if (notes == null)
			{
				notes = await gitLabApi.GetAllIssueNotes(issue.Iid);
				_cacheForGitLabIssueNotes[issue.Id] = notes;
			}

			foreach (var comment in notes)
			{
				if (comment.Body.Contains(card.Id)) // Here, intentionally the ID not the ShortLink.
				{
					return issue;
				}
			}
		}

		return null;
	}

	private async Task<Issue?> findGitLabIssueForTrelloCard(GitLabApi gitLabApi, Card card,
		IReadOnlyList<Issue> allIssues)
	{
		// First look for the card ID in a comment.
		var i = await findGitLabIssueForTrelloCardInNotes(gitLabApi, card, allIssues);
		if (i != null) return i;

		// Next, try to find a unique matching title.
		var matches = allIssues.Where(j => j.Title == card.Name).ToList();

		return matches.Count == 1
			? matches.First()
			// If multiple, do not try to adjust since we might touch the wrong issue.
			: null;
	}

	/// <summary>
	/// Adjusts the mentions inside the GitLab issues and their comments.
	/// </summary>
	/// <param name="progress">A progress update provider.</param>
	public async Task<bool> AdjustMentions(IProgress<ConversionProgressReport> progress)
	{
		progress.Report(new("Starting to adjust mentions."));

		// --
		// Grant admin privileges if Sudo option provided.

		var nonAdminUsers = new List<User>();

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.GrantAdminPrivileges));

			var users = await gitlab.GetAllUsers();

			nonAdminUsers.AddRange(users.Where(u => !u.IsAdmin)
				.Join(associations.Members_Users, u => u.Id, au => au.Value, (u, _) => u));

			await SetUserAdminPrivileges(nonAdminUsers, true, progress, ConversionStep.GrantAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesGranted));
		}

		// --

		var adjustCount = 0;

		var issues = await gitlab.GetAllIssues();

		var totalIssues = issues.Count;

		for (var i = 0; i < totalIssues; i++)
		{
			var any = false;

			progress.Report(new($"{i + 1}/{totalIssues} Adjusting mentions."));

			var issue = issues[i];

			// Adjust issue description.
			var description = replaceMentions(issue.Description);

			if (description != issue.Description)
			{
				try
				{
					await gitlab.EditIssueDescription(
						issue.Iid,
						new()
						{
							Description = description,
						});

					any = true;
				}
				catch (ApiException exception)
				{
					progress.Report(new(
						$"Error while editing issue description: {exception.Message}\nIssue: {issue.Id} (#{issue.Iid})"));
				}
			}

			// Also adjust notes.
			var comments = await gitlab.GetAllIssueNotes(issue.Iid);

			foreach (var comment in comments)
			{
				var editedComment = new ModifyIssueNote
				{
					Body = replaceMentions(comment.Body)
				};

				if (comment.Body != editedComment.Body)
				{
					try
					{
						await gitlab.ModifyIssueNote(
							issue,
							comment.Id,
							editedComment);

						any = true;
					}
					catch (ApiException exception)
					{
						progress.Report(new(
							$"Error while editing issue note: {exception.Message}\nIssue: {issue.Id} (#{issue.Iid})\nComment: {comment.Id}"));
					}
				}
			}

			if (any)
			{
				progress.Report(new($"    => Adjusted mentions."));
				adjustCount++;
			}
		}

		// --
		// Revoke admin privileges (of non admin users) if Sudo option provided.

		if (gitlab.Sudo)
		{
			progress.Report(new(ConversionStep.RevokeAdminPrivileges));

			await SetUserAdminPrivileges(nonAdminUsers, false, progress, ConversionStep.RevokeAdminPrivileges);

			progress.Report(new(ConversionStep.AdminPrivilegesRevoked));
		}

		// --

		progress.Report(new($"Finished adjusting {adjustCount} mentions."));
		return true;
	}

	/// <summary>
	/// Converts all Trello cards to GitLab issues.
	/// </summary>
	/// <param name="progress">A progress update provider.</param>
	public async Task<bool> ConvertAll(IProgress<ConversionProgressReport> progress)
	{
		progress.Report(new(ConversionStep.Init));

		// Fetch Trello boardCustomFields.

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

			nonAdminUsers.AddRange(users.Where(u => !u.IsAdmin)
				.Join(associations.Members_Users, u => u.Id, au => au.Value, (u, _) => u));

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
					progress.Report(new(ConversionStep.FetchMilestones, i, totalMilestones,
						[
							$"Error while fetching milestone: milestone with iid '{labelMilestone.Value}' not found on project"
						]));
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
					progress.Report(new(ConversionStep.FetchMilestones, i, totalMilestones,
						new[]
						{
							$"Error while fetching milestone: milestone with iid '{listMilestone.Value}' not found on project"
						}));
				}

				i++;
			}

			associations.Lists_Milestones = listsMilestones;

			progress.Report(new(ConversionStep.MilestonesFetched));
		}

		// Convert all cards.

		var boardCustomFields = await trello.GetAllCustomFields();

		var allIssues = await gitlab.GetAllIssues();

		for (var i = 0; i < totalCards; i++)
		{
			progress.Report(new(ConversionStep.ConvertingCards, i, totalCards));

			var card = trelloBoard.Cards[i];
			if (!wantIncludeCard(card))
			{
				progress.Report(new($"    => Skipping Trello card #{card.ShortLink}."));
				continue;
			}

			var issue = await findGitLabIssueForTrelloCard(gitlab, card, allIssues);
			if (issue != null)
			{
				progress.Report(new(ConversionStep.ConvertingCards, i, totalCards,
					[$"    ==> Issue already exists: {issue.Id} (#{issue.Iid}). Skipping."]));
				continue;
			}

			var cardCustomFieldItems = await trello.GetCardCustomFieldItems(card.Id);

			var errors = await Convert(boardCustomFields, cardCustomFieldItems, card);

			if (errors.Count != 0)
			{
				progress.Report(new(ConversionStep.ConvertingCards, i, totalCards, errors));
			}
		}

		progress.Report(new(ConversionStep.CardsConverted));

		// --
		// As a second pass, also replace any Trello links to other cards.

		await ReplaceTrelloLinks(progress);

		// --
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
	/// <param name="users"></param>
	/// <param name="admin">User is admin.</param>
	/// <param name="progress">A progress update provider.</param>
	/// <param name="step">The step doing this action.</param>
	protected async Task SetUserAdminPrivileges(IReadOnlyList<User> users, bool admin,
		IProgress<ConversionProgressReport> progress, ConversionStep step)
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
				progress.Report(new(step, i, users.Count,
					new[]
					{
						$"Error while {(admin ? "granting" : "revoking")} admin privilege: {exception.Message}\nUser: {user.Id} ({user.Username})"
					}));
			}
		}
	}

	private sealed class AttachmentMapping
	{
		public Attachment TrelloAttachment { get; set; } = null!;
		public Upload? GitlabAttachment { get; set; }

		/// <summary>
		/// Merken, wenn verarbeitet wurde.
		/// </summary>
		public bool DidReplace { get; set; }
	}

	/// <summary>
	/// Converts a Trello card to a GitLab issue.
	/// </summary>
	protected async Task<IReadOnlyList<string>> Convert(
		IReadOnlyList<BoardCustomField> boardCustomFields,
		IReadOnlyList<CardCustomFieldItem> cardCustomFieldItems,
		Card card)
	{
		var errors = new List<string>();

		// Finds basic infos.

		var createAction = FindCreateCardAction(card);
		var createdBy = FindAssociatedUserId(createAction?.IdMemberCreator);

		var assignees = card.IdMembers?.Select(FindAssociatedUserId).Where(u => u != null).Cast<int>();

		var labels = GetCardAssociatedLabels(card);

		var attachments = await GetCardAttachments(card);

		// Creates GitLab issue.

		Issue? issue = null;
		string? description;

		// Verlinkungen anpassen.
		var attachmentUrlMappings = new List<AttachmentMapping>();

		try
		{
			foreach (var attachment in attachments)
			{
				var fileName = Path.GetFileName(attachment.Url);
				var bytes = await trello.DownloadAttachment(attachment.Url);

				if (bytes == null || bytes.Length == 0)
				{
					attachmentUrlMappings.Add(new()
					{
						TrelloAttachment = attachment,
						GitlabAttachment = null
					});

					continue;
				}

				var tempFolderPath = Path.Combine(Path.GetTempPath(),
					$@"Trello2GitLab-{DateTime.Now.Ticks}-{Guid.NewGuid():N}");
				try
				{
					fileName = sanitizeFileName(fileName);

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

			description =
				(await GetCardDescriptionWithChecklistsAndCustomFields(boardCustomFields, cardCustomFieldItems, card))
				.Truncate(DESCRIPTION_MAX_LENGTH);
			description = replaceAttachments(description, attachmentUrlMappings, false);
			description = replaceMentions(description);

			issue = await gitlab.CreateIssue(new()
			{
				CreatedAt = createAction?.Date ?? card.DateLastActivity,
				Title = card.Name.Truncate(TITLE_MAX_LENGTH),
				Description = description,
				Labels = labels.Any() ? string.Join(',', labels) : null,
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

		// Add one comment to identify the original Trello card.
		// Helpful for later migrations and stuff.
		if (true)
		{
			await associateIssueWithTrelloCard(card, issue, createdBy);
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
						Body = replaceMentions(
							replaceAttachments(commentAction.Data.Text, attachmentUrlMappings, false)),
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

		Trello.Action? closeAction = null;

		if (card.Closed)
		{
			closeAction = FindCloseCardAction(card);
		}
		else
		{
			var list = trelloBoard?.Lists.FirstOrDefault(l => l.Id == card.IdList);

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

	private static string? sanitizeFileName(string? fileName)
	{
		if (string.IsNullOrEmpty(fileName)) return fileName;

		var invalidChars = Path.GetInvalidFileNameChars();
		var sanitizedFileName = new string(fileName
			.Where(ch => !invalidChars.Contains(ch))
			.ToArray());

		// Optional: Ersetze spezifische Zeichen oder Muster, die Probleme verursachen könnten.
		// Zum Beispiel, um Directory-Traversals zu verhindern (sehr rudimentär):
		sanitizedFileName = sanitizedFileName.Replace(@"..", string.Empty);

		return sanitizedFileName;
	}

	private async Task<bool> replaceTrelloLinks(
		Issue issue,
		IReadOnlyList<Issue> allIssues,
		IProgress<ConversionProgressReport> progress)
	{
		var any = false;

		// --
		// Description.

		// Adjust issue description.
		var description = await doReplaceTrelloLinks(issue.Description, allIssues, progress);

		if (description != issue.Description)
		{
			try
			{
				await gitlab.EditIssueDescription(
					issue.Iid,
					new()
					{
						Description = description,
					});

				any = true;
			}
			catch (ApiException exception)
			{
				progress.Report(new(
					$"Error while editing issue description: {exception.Message}\nIssue: {issue.Id} (#{issue.Iid})"));
			}
		}

		// --

		// Also adjust notes.
		var comments = await gitlab.GetAllIssueNotes(issue.Iid);

		foreach (var comment in comments)
		{
			var editedComment = new ModifyIssueNote
			{
				Body = await doReplaceTrelloLinks(comment.Body, allIssues, progress)
			};

			if (comment.Body != editedComment.Body)
			{
				try
				{
					await gitlab.ModifyIssueNote(
						issue,
						comment.Id,
						editedComment);

					any = true;
				}
				catch (ApiException exception)
				{
					progress.Report(new(
						$"Error while editing issue note: {exception.Message}\nIssue: {issue.Id} (#{issue.Iid})\nComment: {comment?.Id}"));
				}
			}
		}

		return any;
	}

	private async Task<string?> doReplaceTrelloLinks(
		string? text,
		IReadOnlyList<Issue> allIssues,
		IProgress<ConversionProgressReport> progress)
	{
		if (string.IsNullOrEmpty(text)) return text;
		if (!text.Contains(@"https://trello.com/c/")) return text;
		if (text.Contains(uniqueInidicator)) return text;
		if (text.Contains(@"Migrated from Trello card")) return text;

		const string corePattern = @"https:\/\/trello\.com\/c\/([A-Za-z0-9]+)(\/\d+-[\w-%]+)?";

		// Case 1.
		if (true)
		{
			var pattern = @"\[(https?:\/\/[^\s\]]+)\]\(\1(?:\s+"".*?"")?\)";
			var matches = Regex.Matches(text, pattern);

			foreach (Match match in matches)
			{
				var matchesInner = Regex.Matches(match.Groups[1].Value, corePattern);

				foreach (Match matchInner in matchesInner)
				{
					var cardId = matchInner.Groups[1].Value;

					var card = trelloBoard.Cards.FirstOrDefault(c => c.ShortLink == cardId);
					if (card == null) continue;

					if (!wantIncludeCard(card))
					{
						progress.Report(new($"    => Skipping Trello card #{card.ShortLink}."));
						continue;
					}

					var issue = await findGitLabIssueForTrelloCard(gitlab, card, allIssues);
					if (issue == null) continue;

					// Link to GitLab issue.
					text = text.Replace(match/*inner*/.Value, $@"#{issue.Iid}");
				}
			}
		}

		// Case 2.
		if (true)
		{
			var matches = Regex.Matches(text, corePattern);

			foreach (Match match in matches)
			{
				var cardId = match.Groups[1].Value;

				var card = trelloBoard.Cards.FirstOrDefault(c => c.ShortLink == cardId);
				if (card == null) continue;

				if (!wantIncludeCard(card))
				{
					progress.Report(new($"    => Skipping Trello card #{card.ShortLink}."));
					continue;
				}

				var issue = await findGitLabIssueForTrelloCard(gitlab, card, allIssues);
				if (issue == null) continue;

				// Link to GitLab issue.
				text = text.Replace(match.Value, $@"#{issue.Iid}");
			}
		}

		return text;
	}

	/// <summary>
	/// Never change this.
	/// </summary>
	private const string uniqueInidicator = @"9e5812858dd8430ebe18c496c219736a";

	private async Task associateIssueWithTrelloCard(Card card, Issue? issue, int? createdBy)
	{
		await gitlab.CommentIssue(
			issue,
			new()
			{
				Body =
					$"""
					 Migrated from Trello card [{card.ShortLink}]({card.ShortUrl}).

					 <!--
					 Trello card ID:   '{card.Id}'.
					 Trello list ID:   '{card.IdList}'.
					 Unique indicator: '{uniqueInidicator}'.
					 -->
					 """,
				CreatedAt = DateTime.Now,
			},
			createdBy);
	}

	private string? replaceMentions(string? text)
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
				if (mapping.GitlabAttachment != null)
				{
					text = text.Replace(mapping.TrelloAttachment.Url, mapping.GitlabAttachment.Url);
					mapping.DidReplace = true;
				}
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
					if (notReplacedAttachment.GitlabAttachment != null)
					{
						text += $"- {notReplacedAttachment.GitlabAttachment.Markdown}\n";
					}
					else
					{
						// Keep original link, to whatever it might be.
						text += $"- {notReplacedAttachment.TrelloAttachment.Url}\n";
					}
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
	/// Gets the description of a given Trello card, and adds checklists and custom fields.
	/// </summary>
	protected async Task<string?> GetCardDescriptionWithChecklistsAndCustomFields(
		IReadOnlyList<BoardCustomField> boardCustomFields,
		IReadOnlyList<CardCustomFieldItem> cardCustomFieldItems,
		Card card)
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

		description = await checkAddCustomFields(boardCustomFields, cardCustomFieldItems, description);

		return description;
	}

	private const string CustomFieldHeadline = "### Custom Fields";

	private async Task<string?> checkAddCustomFields(
		IReadOnlyList<BoardCustomField> boardCustomFields,
		IReadOnlyList<CardCustomFieldItem> cardCustomFieldItems,
		string? description)
	{
		if (string.IsNullOrEmpty(description)) return description;

		if (boardCustomFields.Count <= 0) return description;
		if (boardCustomFields.Count <= 0) return description;

		// If headline already exists, assume already added.
		if (description.Contains(CustomFieldHeadline)) return description;

		var customFields = await generateCustomFields(boardCustomFields, cardCustomFieldItems);

		// No custom fields collected.
		if (string.IsNullOrEmpty(customFields)) return description;

		description += $"\n\n{customFields?.Trim()}";
		return description;
	}

	private async Task<string?> generateCustomFields(
		IReadOnlyList<BoardCustomField> boardCustomFields,
		IReadOnlyList<CardCustomFieldItem> cardCustomFieldItems)
	{
		if (boardCustomFields.Count <= 0) return null;
		if (boardCustomFields.Count <= 0) return null;

		var result = new StringBuilder();
		result.Append($"\n\n{CustomFieldHeadline}\n\n");

		var any = false;

		foreach (var boardCustomField in boardCustomFields)
		{
			var cardCustomFieldItem = cardCustomFieldItems.FirstOrDefault(c => c.IdCustomField == boardCustomField.Id);

			if (cardCustomFieldItem == null) continue;

			var value = cardCustomFieldItem.Value?.Text;

			if (boardCustomField.Type == "list")
			{
				var option = boardCustomField.Options.FirstOrDefault(o =>
					o.Id == value || o.Id == cardCustomFieldItem.IdValue?.ToString());
				value = option?.Value?.Text;
			}

			if (string.IsNullOrEmpty(value)) continue;

			result.Append($"- **{boardCustomField.Name}**: {value}\n");
			any = true;
		}

		return any ? result.ToString() : null;
	}

	/// <summary>
	/// Gets all associated labels from a given Trello card and its list.
	/// </summary>
	protected List<string> GetCardAssociatedLabels(Card card)
	{
		string? gitlabLabel;
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
			return associations.Labels_Milestones?.Join(card.IdLabels, lm => lm.Key, l => l, (lm, l) => lm.Value)
				.FirstOrDefault();
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

	private bool wantIncludeCard(Card? card)
	{
		if (card == null) return false;
		if (options.Trello.CardsToInclude is not { Length: > 0 }) return true;

		return options.Trello.CardsToInclude.Any(c =>
			c.Equals(card.Id, StringComparison.OrdinalIgnoreCase) ||
			c.Equals(card.ShortLink, StringComparison.OrdinalIgnoreCase));
	}
}