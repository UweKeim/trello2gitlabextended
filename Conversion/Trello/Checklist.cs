﻿namespace Trello2GitLab.Conversion.Trello;

public sealed class Checklist
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string IdCard { get; set; }

	public IReadOnlyList<ChecklistItem> CheckItems { get; set; }
}