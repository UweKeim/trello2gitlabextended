namespace Trello2GitLab.Conversion.GitLab;

public sealed class Upload
{
	[JsonProperty("alt")]
	public string Alt { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }

	[JsonProperty("full_path")]
	public string FullPath { get; set; }

	[JsonProperty("markdown")]
	public string Markdown { get; set; }
}