namespace Trello2GitLab.Conversion;

public enum ConverterAction
{
	All = 0,
	AdjustMentions = 1,
	DeleteIssues = 2,
	MoveCustomFields = 3,
	AssocitateWithTrello = 4,
	ReplaceTrelloLinks = 5
}