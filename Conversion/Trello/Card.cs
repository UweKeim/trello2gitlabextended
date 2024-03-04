﻿namespace Trello2GitLab.Conversion.Trello;

public sealed class Card
{
	public string Id { get; set; }

	public bool Closed { get; set; }

	public DateTime DateLastActivity { get; set; }

	public string Name { get; set; }

	public string Desc { get; set; }

	public string ShortLink { get; set; }
	public string ShortUrl { get; set; }

	public DateTime? Due { get; set; }

	public IReadOnlyList<string> IdLabels { get; set; }

	public string IdList { get; set; }

	public IReadOnlyList<string> IdChecklists { get; set; }

	public IReadOnlyList<string> IdMembers { get; set; }
}