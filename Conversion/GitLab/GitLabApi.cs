﻿using Newtonsoft.Json.Serialization;

namespace Trello2GitLab.Conversion.GitLab;

/// <summary>
/// GitLab Api helper.
/// </summary>
internal class GitLabApi : IDisposable
{
	private static readonly JsonSerializerSettings jsonSettings = new()
	{
		ContractResolver = new DefaultContractResolver()
		{
			NamingStrategy = new SnakeCaseNamingStrategy(),
		},
		DateFormatString = "yyyy-MM-ddTHH:mm:ssK",
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient client;

	/// <summary>
	/// GitLab Api helper.
	/// </summary>
	public GitLabApi(GitLabOptions options)
	{
		BaseUrl = $"{options.Url}/api/v4";
		ProjectUrl = $"{BaseUrl}/projects/{options.ProjectId}";
		Token = options.Token;
		Sudo = options.Sudo;

		client = new();
		client.DefaultRequestHeaders.Add("Accept", "application/json");
		client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", Token);
	}

	private string BaseUrl { get; }
	private string ProjectUrl { get; }
	private string Token { get; }
	public bool Sudo { get; }

	public void Dispose()
	{
		client.Dispose();
	}

	/// <summary>
	/// Gets all users.
	/// </summary>
	public async Task<IReadOnlyList<User>> GetAllUsers()
	{
		return await RequestPaged<User>(HttpMethod.Get, "/users", projectBasedUrl: false);
	}

	/// <summary>
	/// Edits an user.
	/// </summary>
	/// <param name="user">The user to edit</param>
	public async Task<User?> EditUser(EditUser user)
	{
		return await Request<User>(HttpMethod.Put, $"/users/{user.Id}", null, user, projectBasedUrl: false);
	}

	/// <summary>
	/// Gets all project's milestones.
	/// </summary>
	public async Task<IReadOnlyList<Milestone>> GetAllMilestones()
	{
		return await RequestPaged<Milestone>(HttpMethod.Get, "/milestones", projectBasedUrl: true);
	}

	/// <summary>
	/// Gets all project's Issues.
	/// </summary>
	public async Task<IReadOnlyList<Issue>> GetAllIssues()
	{
		return await RequestPaged<Issue>(HttpMethod.Get, "/issues", projectBasedUrl: true);
	}

	/// <summary>
	/// Creates an issue in the target GitLab server.
	/// </summary>
	/// <param name="newIssue">The issue object to create.</param>
	/// <param name="createdBy">ID of the user creating the issue.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	public async Task<Issue?> CreateIssue(NewIssue newIssue, int? createdBy = null)
	{
		return await Request<Issue>(HttpMethod.Post, "/issues", createdBy, newIssue);
	}

	/// <summary>
	/// Uploads a file to the target GitLab server.
	/// </summary>
	/// <remarks>
	/// https://docs.gitlab.com/ee/api/projects.html#upload-a-file
	/// </remarks>
	public async Task<Upload?> UploadFile(string filePath, string? mimeType = null)
	{
		using var content = new MultipartFormDataContent();
		using var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));

		fileContent.Headers.ContentType = new(string.IsNullOrEmpty(mimeType)
			? MimeMapping.MimeUtility.GetMimeMapping(filePath)
			: mimeType);
		content.Add(fileContent, @"file", Path.GetFileName(filePath));

		return await Request<Upload>(HttpMethod.Post, $"/uploads", null, content);
	}

	/// <summary>
	/// Edits an issue.
	/// </summary>
	/// <param name="editIssue">The issue object to edit.</param>
	/// <param name="editedBy">ID of the user editing the issue.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	public async Task<Issue?> EditIssue(EditIssue editIssue, int? editedBy = null)
	{
		return await Request<Issue>(HttpMethod.Put, $"/issues/{editIssue.IssueIid}", editedBy, editIssue);
	}

	public async Task<Issue?> EditIssueDescription(int issueIid, EditIssueDescription editIssueDescription,
		int? editedBy = null)
	{
		return await Request<Issue>(HttpMethod.Put, $"/issues/{issueIid}", editedBy, editIssueDescription);
	}

	/// <summary>
	/// Gets all project's Issues.
	/// </summary>
	public async Task<IReadOnlyList<IssueNote>> GetAllIssueNotes(int issueIid)
	{
		return await RequestPaged<IssueNote>(HttpMethod.Get, $"/issues/{issueIid}/notes", projectBasedUrl: true);
	}

	/// <summary>
	/// Adds comment to an issue.
	/// </summary>
	/// <param name="issue">The issue object to comment.</param>
	/// <param name="comment">The comment object to add.</param>
	/// <param name="commentedBy">ID of the user commenting the issue.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	public async Task<IssueNote?> CommentIssue(Issue issue, NewIssueNote comment, int? commentedBy = null)
	{
		return await Request<IssueNote>(HttpMethod.Post, $"/issues/{issue.Iid}/notes", commentedBy, comment);
	}

	/// <summary>
	/// Edits a comment on an issue.
	/// </summary>
	/// <param name="issue">The issue object to comment.</param>
	/// <param name="comment">The comment object to add.</param>
	/// <param name="noteId"></param>
	/// <param name="commentedBy">ID of the user commenting the issue.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	public async Task<IssueNote?> ModifyIssueNote(Issue issue, int noteId, ModifyIssueNote comment,
		int? commentedBy = null)
	{
		return await Request<IssueNote>(HttpMethod.Put, $"/issues/{issue.Iid}/notes/{noteId}", commentedBy, comment);
	}

	/// <summary>
	/// Builds a GitLab Api URL.
	/// </summary>
	/// <param name="endpoint">Target endpoint (starting with `/`).</param>
	/// <param name="projectBasedUrl">Tells if the API URL targets the project.</param>
	private string Url(string endpoint, bool projectBasedUrl)
	{
		return (projectBasedUrl ? ProjectUrl : BaseUrl) + endpoint;
	}

	/// <summary>
	/// Makes an asynchronous paged request to GitLab API (without body).
	/// </summary>
	/// <typeparam name="T">Fetched data type.</typeparam>
	/// <param name="method">The HTTP method.</param>
	/// <param name="endpoint">Target endpoint (starting with `/`).</param>
	/// <param name="userId">User to impersonate (if sudo).</param>
	/// <param name="content">HTTP request content.</param>
	/// <param name="projectBasedUrl">Tells if the API URL targets the project.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	private async Task<IReadOnlyList<T>> RequestPaged<T>(HttpMethod method, string endpoint, int? userId = null,
		HttpContent? content = null, bool projectBasedUrl = true)
	{
		const int limit = 100;
		var page = 1;
		var items = new List<T>();

		var separator = endpoint.Contains('?') ? '&' : '?';

		IReadOnlyList<T>? apiResponseItems;
		do
		{
			apiResponseItems = await Request<IReadOnlyList<T>>(method,
				$"{endpoint}{separator}per_page={limit}&page={page}", userId, content, projectBasedUrl);

			if (apiResponseItems != null) items.AddRange(apiResponseItems);
			page++;
		} while (apiResponseItems is { Count: limit });

		return items;
	}

	/// <summary>
	/// Makes an asynchronous request to GitLab API.
	/// </summary>
	/// <typeparam name="T">Fetched data type.</typeparam>
	/// <param name="method">The HTTP method.</param>
	/// <param name="endpoint">Target endpoint (starting with `/`).</param>
	/// <param name="userId">User to impersonate (if sudo).</param>
	/// <param name="serializableContent">Serializable content to send.</param>
	/// <param name="projectBasedUrl"></param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	private async Task<T?> Request<T>(HttpMethod method, string endpoint, int? userId, object serializableContent,
		bool projectBasedUrl = true)
	{
		var serializedContent = JsonConvert.SerializeObject(serializableContent, jsonSettings);

		using var content = new StringContent(serializedContent, Encoding.UTF8, "application/json");
		return await Request<T>(method, endpoint, userId, content, projectBasedUrl);
	}

	/// <summary>
	/// Makes an asynchronous request to GitLab API (without body).
	/// </summary>
	/// <typeparam name="T">Fetched data type.</typeparam>
	/// <param name="method">The HTTP method.</param>
	/// <param name="endpoint">Target endpoint (starting with `/`).</param>
	/// <param name="userId">User to impersonate (if sudo).</param>
	/// <param name="content">HTTP request content.</param>
	/// <param name="projectBasedUrl">Tells if the API URL targets the project.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	internal async Task<T?> Request<T>(HttpMethod method, string endpoint, int? userId = null,
		HttpContent? content = null, bool projectBasedUrl = true)
	{
		using var request = new HttpRequestMessage(method, Url(endpoint, projectBasedUrl));
		if (Sudo && userId != null)
		{
			request.Headers.Add("Sudo", userId.ToString());
		}

		if (content != null)
		{
			request.Content = content;
		}

		using var response = await client.SendAsync(request);
		using var responseContent = response.Content;
		var contentString = await responseContent.ReadAsStringAsync();

		if ((int)response.StatusCode >= 400)
			throw new ApiException(response, contentString);

		return JsonConvert.DeserializeObject<T>(contentString, jsonSettings);
	}

	/// <summary>
	/// Makes an asynchronous request to GitLab API (without body nor response).
	/// </summary>
	/// <param name="method">The HTTP method.</param>
	/// <param name="endpoint">Target endpoint (starting with `/`).</param>
	/// <param name="projectBasedUrl">Tells if the API URL targets the project.</param>
	/// <exception cref="ApiException"></exception>
	/// <exception cref="HttpRequestException"></exception>
	internal async Task Request(HttpMethod method, string endpoint, bool projectBasedUrl = true)
	{
		using var request = new HttpRequestMessage(method, Url(endpoint, projectBasedUrl));
		using var response = await client.SendAsync(request);
		using var responseContent = response.Content;
		var contentString = await responseContent.ReadAsStringAsync();

		if ((int)response.StatusCode >= 400)
			throw new ApiException(response, contentString);
	}
}