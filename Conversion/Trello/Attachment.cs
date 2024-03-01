namespace Trello2GitLab.Conversion.Trello;

using System;
using System.Collections.Generic;

public class Attachment
{
	public string Id { get; set; }
	public string Bytes { get; set; }
	public DateTime Date { get; set; }
	public string EdgeColor { get; set; }
	public string IdMember { get; set; }
	public bool IsUpload { get; set; }
	public string MimeType { get; set; }
	public string Name { get; set; }
	public List<object> Previews { get; set; }
	public string Url { get; set; }
	public int Pos { get; set; }
}