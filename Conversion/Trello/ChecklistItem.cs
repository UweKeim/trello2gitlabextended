namespace Trello2GitLab.Conversion.Trello;

public sealed class ChecklistItem
{
	public string Id { get; set; }

	public string IdChecklist { get; set; }

	public string State { get; set; }

	public string Name { get; set; }
}