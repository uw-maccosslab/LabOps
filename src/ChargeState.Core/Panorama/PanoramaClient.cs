using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChargeState.Core.Infrastructure;

namespace ChargeState.Core.Panorama;

/// <summary>A folder or file in a Panorama listing.</summary>
/// <param name="Path">Its WebDAV path, for example /_webdav/MacCoss/maccoss/@files/2026-BioTRACK/.</param>
public sealed record PanoramaEntry(string Name, string Path, bool IsFolder);

/// <summary>A notebook in the lab's ELN (Panorama's labbook.Notebook).</summary>
/// <param name="RowId">The number at the end of its ID, and in its address.</param>
/// <param name="Id">The notebook ID, for example ELN-1567-20250730-131.</param>
public sealed record PanoramaNotebook(
    int RowId, string Id, string? Title, string? Status, string? Author, DateTimeOffset? Modified, bool Archived)
{
    /// <summary>"inProgress" as a person reads it: "In progress".</summary>
    public string StatusText => string.IsNullOrEmpty(Status)
        ? ""
        : char.ToUpperInvariant(Status[0]) + string.Concat(Status[1..].Select(c => char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    public string ModifiedText => Modified?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public string Url(Uri? server = null) => PanoramaPaths.NotebookUrl(RowId, server);
}

/// <summary>A listing failed; the message is written for the person using the app.</summary>
public sealed class PanoramaException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Panorama did not accept the sign-in, so asking for another one may help.</summary>
    public bool IsSignInProblem { get; init; }
}

/// <summary>Reads Panorama's folders and the lab's ELN notebooks.</summary>
public interface IPanoramaClient
{
    Task<IReadOnlyList<PanoramaEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PanoramaNotebook>> ListNotebooksAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to Panorama, signed in with HTTP Basic on every request. Folders and files come the way
/// PanoramaBridge browses them, one level at a time with LabKey's WebDAV listing
/// (<c>GET /_webdav/...?method=json</c>); notebooks and Skyline documents from LabKey's query API.
/// The one thing it writes is a project's wiki page (PanoramaClient.Wiki.cs).
/// </summary>
public sealed partial class PanoramaClient : IPanoramaClient, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly PanoramaCredential _credential;

    public PanoramaClient(Uri server, PanoramaCredential credential, HttpMessageHandler? handler = null)
    {
        _credential = credential;
        // No redirects and no cookies: a redirect means a sign-in page, which must fail rather
        // than look like an empty folder, and stateless Basic needs no session (or CSRF token).
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = server,
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.ProductName}/{AppInfo.Version}");
    }

    public async Task<IReadOnlyList<PanoramaEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        var folder = PanoramaPaths.AsFolder(path);
        var body = await GetAsync(PanoramaPaths.Encode(folder) + "?method=json", PanoramaPaths.ToFolder(folder), cancellationToken)
            .ConfigureAwait(false);
        return Parse(body, folder);
    }

    /// <summary>The lab's notebooks, newest first; templates are left out.</summary>
    public async Task<IReadOnlyList<PanoramaNotebook>> ListNotebooksAsync(CancellationToken cancellationToken = default)
    {
        var body = await GetAsync(PanoramaPaths.NotebooksQuery, "the notebooks", cancellationToken).ConfigureAwait(false);
        return ParseNotebooks(body);
    }

    /// <summary>A GET as text, with every failure turned into a message for the person.</summary>
    private async Task<string> GetAsync(string relativeUrl, string display, CancellationToken cancellationToken) =>
        (await SendAsync(new HttpRequestMessage(HttpMethod.Get, relativeUrl), display, allowMissing: false, cancellationToken)
            .ConfigureAwait(false))!;

    /// <summary>
    /// Sends a request and returns the answer as text, with every failure turned into a message for
    /// the person; null for a 404 when <paramref name="allowMissing"/>.
    /// </summary>
    private async Task<string?> SendAsync(HttpRequestMessage request, string display, bool allowMissing, CancellationToken cancellationToken)
    {
        using var owned = request;
        request.Headers.Authorization = _credential.ToAuthenticationHeader();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PanoramaException($"Could not reach Panorama ({ex.Message}). Check the network connection.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || (int)response.StatusCode is >= 300 and < 400)
            {
                var typed = _credential.Source == "typed";
                var which = typed
                    ? _credential.IsApiKey ? "this API key" : "this user name and password"
                    : $"the sign-in saved by {_credential.Source} ({_credential})";
                throw new PanoramaException(
                    $"Panorama did not accept {which}. "
                    + (typed ? "Check it and try again." : "The API key may have expired, or the password changed."))
                {
                    IsSignInProblem = true,
                };
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new PanoramaException($"This Panorama account cannot open {display}.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (allowMissing)
                {
                    return null;
                }

                throw new PanoramaException($"{display} is not on Panorama.");
            }

            if (!response.IsSuccessStatusCode)
            {
                // LabKey's APIs explain a refusal in JSON: {"exception": "..."}.
                var detail = ApiError(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                throw new PanoramaException(detail is null
                    ? $"Panorama answered {(int)response.StatusCode} {response.ReasonPhrase} for {display}."
                    : $"Panorama refused {display}: {detail}");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads a <c>query-selectRows.api</c> answer for labbook.Notebook.</summary>
    public static IReadOnlyList<PanoramaNotebook> ParseNotebooks(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new PanoramaException("Panorama did not send the notebooks; the sign-in was probably not accepted.", ex)
            {
                IsSignInProblem = true,
            };
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                throw new PanoramaException("Panorama's list of notebooks was not understood.");
            }

            var notebooks = new List<PanoramaNotebook>();
            foreach (var row in rows.EnumerateArray())
            {
                if (Int(row, "RowId") is not { } rowId || Text(row, "Name") is not { } id)
                {
                    continue;
                }

                notebooks.Add(new PanoramaNotebook(rowId, id, Text(row, "Title"), Text(row, "Status"),
                    Text(row, "CreatedBy/DisplayName"), Date(row, "Modified"), row.TryGetProperty("Archived", out var a) && a.ValueKind == JsonValueKind.True));
            }

            return notebooks;
        }

        static string? Text(JsonElement row, string name) =>
            row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static int? Int(JsonElement row, string name) =>
            row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

        // LabKey sends "2025-07-30 10:50:16.399", in the server's time zone.
        static DateTimeOffset? Date(JsonElement row, string name) =>
            Text(row, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
    }

    /// <summary>Reads a <c>?method=json</c> listing of <paramref name="parent"/> (a WebDAV path).</summary>
    /// <remarks>
    /// Each entry's path is built from the parent and its name rather than the server's href,
    /// which arrives encoded in more than one form (as in PanoramaBridge's MethodJsonParser).
    /// </remarks>
    public static IReadOnlyList<PanoramaEntry> Parse(string body, string parent)
    {
        Listing? listing;
        try
        {
            listing = JsonSerializer.Deserialize<Listing>(body);
        }
        catch (JsonException ex)
        {
            // An expired session or a missing sign-in answers with an HTML page.
            throw new PanoramaException("Panorama did not send a folder listing; the sign-in was probably not accepted.", ex)
            {
                IsSignInProblem = true,
            };
        }

        if (listing?.Files is null)
        {
            throw new PanoramaException("Panorama's folder listing was empty or not understood.");
        }

        var folder = PanoramaPaths.AsFolder(parent);
        return
        [
            .. listing.Files.Where(f => !string.IsNullOrEmpty(f.Text)).Select(f =>
            {
                var isFolder = f.Collection ?? !(f.Leaf ?? true);
                return new PanoramaEntry(f.Text!, folder + f.Text + (isFolder ? "/" : ""), isFolder);
            }),
        ];
    }

    public void Dispose() => _http.Dispose();

    private sealed class Listing
    {
        [JsonPropertyName("files")]
        public List<Entry>? Files { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("collection")]
        public bool? Collection { get; set; }

        [JsonPropertyName("leaf")]
        public bool? Leaf { get; set; }
    }
}

/// <summary>
/// Panorama paths three ways: WebDAV (/_webdav/MacCoss/maccoss/@files/2026-BioTRACK/, what the
/// listing uses), folder (/MacCoss/maccoss/@files/2026-BioTRACK, what lab-projects records), and the
/// address a browser opens.
/// </summary>
public static class PanoramaPaths
{
    public static Uri DefaultServer { get; } = new("https://panoramaweb.org");

    /// <summary>
    /// Where browsing starts: the MacCoss project, which holds the lab's shared folder (maccoss,
    /// where PanoramaBridge uploads by default) beside Collaborations.
    /// </summary>
    public const string StartFolder = "/_webdav/MacCoss/";

    private const string WebDavRoot = "/_webdav";

    /// <summary>The project whose ELN holds the lab's notebooks.</summary>
    public const string NotebookProject = "/MacCoss";

    /// <summary>labbook.Notebook in the ELN's project: what a notebook picker shows, newest first, no templates.</summary>
    public const string NotebooksQuery = NotebookProject + "/query-selectRows.api?schemaName=labbook&query.queryName=Notebook"
        + "&query.columns=RowId,Name,Title,Status,CreatedBy%2FDisplayName,Modified,Archived"
        + "&query.Template~eq=false&query.sort=-Modified&query.maxRows=5000";

    /// <summary>Where a notebook opens: .../MacCoss/samplemanager-app.view#/notebooks/131.</summary>
    public static string NotebookUrl(int rowId, Uri? server = null) =>
        $"{(server ?? DefaultServer).GetLeftPart(UriPartial.Authority)}{NotebookProject}/samplemanager-app.view#/notebooks/{rowId}";

    /// <summary>
    /// The address for a notebook ID: its last number is the notebook's row (ELN-1567-20250730-131,
    /// ELN-20250730-131 and ELN-131 are all notebook 131). Null for anything else.
    /// </summary>
    public static string? NotebookUrlFromId(string? id) =>
        id is not null && System.Text.RegularExpressions.Regex.Match(id.Trim(), @"^ELN-(?:.*-)?(\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m
            ? NotebookUrl(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            : null;

    /// <summary>With a leading and a trailing slash.</summary>
    public static string AsFolder(string path) => "/" + path.Trim().Trim('/') + (path.Trim().Trim('/').Length == 0 ? "" : "/");

    /// <summary>/_webdav/MacCoss/maccoss/@files/X/ to /MacCoss/maccoss/@files/X.</summary>
    public static string ToFolder(string webDavPath)
    {
        var path = "/" + webDavPath.Trim().Trim('/');
        return path.StartsWith(WebDavRoot + "/", StringComparison.OrdinalIgnoreCase) ? path[WebDavRoot.Length..] : path;
    }

    /// <summary>/MacCoss/maccoss/@files/X to /_webdav/MacCoss/maccoss/@files/X/.</summary>
    public static string ToWebDav(string folder) => AsFolder(WebDavRoot + "/" + folder.Trim().Trim('/'));

    /// <summary>
    /// Each segment percent-encoded, except '@', which LabKey's file roots (@files) keep as is
    /// (PanoramaBridge's RemotePath does the same).
    /// </summary>
    public static string Encode(string path) =>
        string.Join('/', path.Split('/').Select(s => Uri.EscapeDataString(s).Replace("%40", "@", StringComparison.Ordinal)));

    /// <summary>
    /// The page a browser opens for a recorded folder: a file area (@files) as Panorama's WebDAV
    /// listing, a folder as its start page, and a full address as given.
    /// </summary>
    public static string BrowserUrl(string folder, Uri? server = null)
    {
        server ??= DefaultServer;
        var host = server.GetLeftPart(UriPartial.Authority);
        if (folder.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return folder;
        }

        var trimmed = folder.Trim().Trim('/');
        return trimmed.Split('/').Any(s => s.StartsWith('@'))
            ? host + Encode(ToWebDav(trimmed))
            : $"{host}/{Encode(trimmed)}/project-begin.view";
    }
}
