using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChargeState.Core.Infrastructure;

namespace ChargeState.Core.Panorama;

/// <summary>A folder or file in a Panorama listing.</summary>
/// <param name="Path">Its WebDAV path, for example /_webdav/MacCoss/maccoss/@files/2026-BioTRACK/.</param>
public sealed record PanoramaEntry(string Name, string Path, bool IsFolder);

/// <summary>A listing failed; the message is written for the person using the app.</summary>
public sealed class PanoramaException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Panorama did not accept the sign-in, so asking for another one may help.</summary>
    public bool IsSignInProblem { get; init; }
}

/// <summary>Lists Panorama folders, read-only.</summary>
public interface IPanoramaFiles
{
    Task<IReadOnlyList<PanoramaEntry>> ListAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Panorama's folders and files, the way PanoramaBridge browses them: one level at a time with
/// LabKey's WebDAV listing (<c>GET /_webdav/...?method=json</c>), signed in with HTTP Basic on
/// every request. Nothing is written to Panorama.
/// </summary>
public sealed class PanoramaFiles : IPanoramaFiles, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly PanoramaCredential _credential;

    public PanoramaFiles(Uri server, PanoramaCredential credential, HttpMessageHandler? handler = null)
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
        using var request = new HttpRequestMessage(HttpMethod.Get, PanoramaPaths.Encode(folder) + "?method=json");
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
            var display = PanoramaPaths.ToFolder(folder);
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
                throw new PanoramaException($"{display} is not on Panorama.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new PanoramaException($"Panorama answered {(int)response.StatusCode} {response.ReasonPhrase} for {display}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(body, folder);
        }
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
