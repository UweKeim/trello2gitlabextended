namespace Trello2GitLab.Conversion.Trello;

public sealed class CardCustomFieldItem
{
	[JsonProperty("id")]
	public string? Id { get; set; }

	[JsonProperty("value")]
	public CardCustomFieldItemValue? Value { get; set; }

	[JsonProperty("idValue")]
	public object? IdValue { get; set; }

	[JsonProperty("idCustomField")]
	public string? IdCustomField { get; set; }

	[JsonProperty("idModel")]
	public string? IdModel { get; set; }

	[JsonProperty("modelType")]
	public string? ModelType { get; set; }
}

public sealed class CardCustomFieldItemValue
{
	[JsonProperty("text")]
	public string? Text { get; set; }
}