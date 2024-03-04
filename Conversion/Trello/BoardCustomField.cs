namespace Trello2GitLab.Conversion.Trello;

/// <summary>
/// Custom field for a board.
/// </summary>
/// <remarks>
/// https://developer.atlassian.com/cloud/trello/guides/rest-api/getting-started-with-custom-fields/
/// </remarks>
public sealed class BoardCustomField
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("idModel")]
	public string IdModel { get; set; }

	[JsonProperty("modelType")]
	public string ModelType { get; set; }

	[JsonProperty("fieldGroup")]
	public string FieldGroup { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("pos")]
	public int Pos { get; set; }

	[JsonProperty("options")]
	public List<BoardCustomFieldOption> Options { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

public class BoardCustomFieldOption
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("idCustomField")]
	public string IdCustomField { get; set; }

	[JsonProperty("value")]
	public BoardCustomFieldOptionValue? Value { get; set; }

	[JsonProperty("color")]
	public string Color { get; set; }

	[JsonProperty("pos")]
	public int Pos { get; set; }
}

public class BoardCustomFieldOptionValue
{
	[JsonProperty("text")]
	public string Text { get; set; }
}