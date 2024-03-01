namespace Trello2GitLab.Conversion;

using System.Net;
using System.Net.Http.Headers;

public sealed class ApiException : Exception
{
	public HttpStatusCode HttpStatusCode { get; }

	public HttpResponseHeaders HttpResponseHeaders { get; }

	public string Details { get; }

	internal ApiException(HttpResponseMessage response, string details, Exception? innerException = null)
		: base($"{(int)response.StatusCode} {response.ReasonPhrase} {details}", innerException)
	{
		HttpStatusCode = response.StatusCode;
		HttpResponseHeaders = response.Headers;
		Details = details;
	}
}