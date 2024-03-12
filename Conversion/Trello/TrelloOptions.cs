namespace Trello2GitLab.Conversion.Trello;

public sealed class TrelloOptions
{
	/// <summary>
	/// Trello API Key.
	/// </summary>
	public string Key { get; set; }

	/// <summary>
	/// Trello API Token.
	/// </summary>
	public string Token { get; set; }

	/// <summary>
	/// Trello board ID.
	/// </summary>
	public string BoardId { get; set; }

	/// <summary>
	/// Specifies which cards to include.
	/// Must be <c>"all"</c> (default), <c>"open"</c>, <c>"visible"</c> or <c>"closed"</c>.
	/// </summary>
	public string Include { get; set; } = "all";

	/// <summary>
	/// If specified and non-empty, only cards with the given IDs/ShortUrls will be included.
	/// </summary>
	public string[]? CardsToInclude { get; set; }
}