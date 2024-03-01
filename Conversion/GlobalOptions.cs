namespace Trello2GitLab.Conversion;

public sealed class GlobalOptions
{
	public ConverterAction Action { get; set; }
	public int DeleteIfGreaterThanIssueId { get; set; }
}