namespace Trello2GitLab.Conversion.GitLab;

/// <summary>
/// https://docs.gitlab.com/ee/api/notes.html#modify-existing-issue-note
/// </summary>
public sealed class ModifyIssueNote
{
	public string? Body { get; set; }
}