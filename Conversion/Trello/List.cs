namespace Trello2GitLab.Conversion.Trello;

public sealed class List
{
	public string? Id { get; set; }

	public string? Name { get; set; }

	public bool Closed { get; set; }

	public Action? CloseAction { get; set; }
}