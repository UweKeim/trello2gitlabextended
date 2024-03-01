namespace Trello2GitLab.Conversion.GitLab;

/// <summary>
/// https://docs.gitlab.com/ee/api/issues.html#edit-issue
/// </summary>
public class EditIssueDescription
{
	public string Description { get; set; }
}