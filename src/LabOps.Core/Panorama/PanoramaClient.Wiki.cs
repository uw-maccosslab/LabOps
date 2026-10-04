using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LabOps.Core.Panorama;

/// <summary>A Skyline document in a Panorama folder (a row of targetedms.runs).</summary>
/// <param name="Name">The document's name, without the upload's date and time or .sky.zip.</param>
/// <param name="Proteins">Its protein groups (PeptideGroupCount), as Panorama's folder page counts them.</param>
public sealed record SkylineDocument(string Name, int? Replicates, int? Peptides, int? Proteins, DateOnly? Uploaded);

/// <summary>A wiki page as Panorama's editor holds it: what a save needs, and its current text.</summary>
/// <param name="EntityId">LabKey's ID for the page.</param>
/// <param name="PageVersionId">The version being replaced; LabKey refuses a save when someone has edited it since.</param>
public sealed record WikiPageInfo(
    string EntityId, int? RowId, int? PageVersionId, string Name, string Title, string Body, int? Parent,
    bool ShowAttachments, bool ShouldIndex)
{
    /// <summary>The footer project.py wiki writes: the page is LabOps's to keep up to date.</summary>
    public const string Mark = "id=\"labops-wiki\"";

    /// <summary>The same footer on pages published before the app was renamed from ChargeState.</summary>
    public const string LegacyMark = "id=\"chargestate-wiki\"";

    private const string Footer = "<div id=\"(?:labops|chargestate)-wiki\"";

    /// <summary>LabOps made the page (its footer is there), rather than someone writing it by hand.</summary>
    public bool IsLabOps => Body.Contains(Mark, StringComparison.Ordinal) || Body.Contains(LegacyMark, StringComparison.Ordinal);

    /// <summary>
    /// Someone edited the page on Panorama after LabOps published it: the text above the footer
    /// no longer matches the footer's fingerprint of it (data-body, from project.py wiki). False for a
    /// page without that fingerprint.
    /// </summary>
    public bool EditedOnPanorama
    {
        get
        {
            if (Regex.Match(Body, Footer + "[^>]*data-body=\"([0-9a-f]+)\"") is not { Success: true } m)
            {
                return false;
            }

            var above = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Body[..m.Index])));
            return !above.StartsWith(m.Groups[1].Value, StringComparison.Ordinal);
        }
    }

    /// <summary>The fingerprint of the written parts last published, from the footer; null on a page written by hand.</summary>
    public string? WrittenHash => Regex.Match(Body, Footer + "[^>]*data-written=\"([^\"]*)\"") is { Success: true } m
        ? m.Groups[1].Value
        : null;
}

public sealed partial class PanoramaClient
{
    private const string SkylineQuery = "query-selectRows.api?schemaName=targetedms&query.queryName=runs"
        + "&query.columns=FileName,ReplicateCount,PeptideCount,PeptideGroupCount,Created&query.sort=Created";

    /// <summary>The Skyline documents in a folder, oldest first.</summary>
    public async Task<IReadOnlyList<SkylineDocument>> ListSkylineDocumentsAsync(string folder, CancellationToken cancellationToken = default)
    {
        var body = await GetAsync(PanoramaPaths.Encode(PanoramaPaths.AsFolder(folder)) + SkylineQuery, folder, cancellationToken)
            .ConfigureAwait(false);
        return ParseSkylineDocuments(body);
    }

    /// <summary>The wiki page as Panorama's editor holds it, or null when the folder has no page by that name.</summary>
    public async Task<WikiPageInfo?> GetWikiPageAsync(string folder, string name, CancellationToken cancellationToken = default)
    {
        var url = PanoramaPaths.Encode(PanoramaPaths.AsFolder(folder)) + "wiki-edit.view?name=" + Uri.EscapeDataString(name);
        var body = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), $"the wiki page {name} in {folder}", allowMissing: true,
            cancellationToken).ConfigureAwait(false);
        return body is null ? null : ParseWikiEditor(body);
    }

    /// <summary>
    /// Writes the wiki page as HTML: a new page, or a new version of <paramref name="existing"/>
    /// (Panorama keeps the earlier versions). Returns the page as saved.
    /// </summary>
    public async Task<WikiPageInfo> SaveWikiPageAsync(
        string folder, string name, string title, string html, WikiPageInfo? existing, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["entityId"] = existing?.EntityId,
            ["rowId"] = existing?.RowId,
            ["name"] = name,
            ["title"] = title,
            ["body"] = html,
            ["parent"] = existing?.Parent,
            ["pageVersionId"] = existing?.PageVersionId,
            ["rendererType"] = "HTML",
            ["showAttachments"] = existing?.ShowAttachments ?? true,
            ["shouldIndex"] = existing?.ShouldIndex ?? true,
        };
        var (token, cookie) = await SecurityContextAsync(cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(HttpMethod.Post, PanoramaPaths.Encode(PanoramaPaths.AsFolder(folder)) + "wiki-saveWiki.api")
        {
            Content = JsonContent.Create(payload),
        };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("X-LABKEY-CSRF", token);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        var body = await SendAsync(request, $"the wiki page {name} in {folder}", allowMissing: false, cancellationToken).ConfigureAwait(false);
        return ParseSaved(body!, name, title, html, existing);
    }

    /// <summary>
    /// LabKey refuses a POST without a CSRF token, even with an API key ("invalid security context").
    /// As LabKey's own client libraries do, the token comes from <c>login-whoami.api</c>, and the
    /// session cookies of that answer go with the POST, since the token belongs to that session.
    /// Both are used for one request and never kept or logged.
    /// </summary>
    private async Task<(string? Token, string? Cookie)> SecurityContextAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "login-whoami.api");
        request.Headers.Authorization = _credential.ToAuthenticationHeader();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join("; ", values.Select(v => v.Split(';', 2)[0].Trim()).Where(v => v.Contains('=', StringComparison.Ordinal)))
                : null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            var token = Text(document.RootElement, "CSRF");
            return (token, string.IsNullOrEmpty(cookie) ? null : cookie);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Without a token the save is refused, and that refusal says why.
            return (null, null);
        }
    }

    /// <summary>Reads a <c>query-selectRows.api</c> answer for targetedms.runs.</summary>
    public static IReadOnlyList<SkylineDocument> ParseSkylineDocuments(string body)
    {
        using var document = ParseJson(body, "the Skyline documents");
        if (!document.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            throw new PanoramaException("Panorama's list of Skyline documents was not understood.");
        }

        return
        [
            .. rows.EnumerateArray().Select(row => new SkylineDocument(
                DocumentName(Text(row, "FileName") ?? "Skyline document"),
                Int(row, "ReplicateCount"), Int(row, "PeptideCount"), Int(row, "PeptideGroupCount"),
                Text(row, "Created") is { Length: >= 10 } created
                && DateOnly.TryParseExact(created[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d
                    : null)),
        ];
    }

    /// <summary>"2026-09-BioTRACK-initial-plate1_2026-10-01_23-38-45.sky.zip" is 2026-09-BioTRACK-initial-plate1.</summary>
    public static string DocumentName(string fileName) =>
        Regex.Replace(fileName, @"(_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})?\.sky(\.zip)?$", "", RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads the page's properties from Panorama's wiki editor (<c>LABKEY._wiki.setProps({...})</c>),
    /// which is where LabKey gives the version a save must name.
    /// </summary>
    public static WikiPageInfo ParseWikiEditor(string html)
    {
        var block = Regex.Match(html, @"LABKEY\._wiki\.setProps\(\{(.*?)\}\);", RegexOptions.Singleline);
        if (!block.Success)
        {
            throw new PanoramaException("Panorama's wiki editor was not understood, so the page was left alone.");
        }

        var props = block.Groups[1].Value;
        var entityId = JsString(props, "entityId");
        if (string.IsNullOrEmpty(entityId))
        {
            throw new PanoramaException("Panorama's wiki editor did not say which page it is, so the page was left alone.");
        }

        return new WikiPageInfo(entityId, JsInt(props, "rowId"), JsInt(props, "pageVersionId"), JsString(props, "name") ?? "",
            JsString(props, "title") ?? "", JsString(props, "body") ?? "", JsInt(props, "parent"),
            JsBool(props, "showAttachments") ?? true, JsBool(props, "shouldIndex") ?? true);
    }

    /// <summary>A JavaScript string literal's text: \x3C, é, \n, \', \" and \\ undone.</summary>
    public static string DecodeJsString(string literal)
    {
        var text = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            var c = literal[i];
            if (c != '\\' || i + 1 >= literal.Length)
            {
                text.Append(c);
                continue;
            }

            var next = literal[++i];
            switch (next)
            {
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'x' when i + 2 < literal.Length && IsHex(literal, i + 1, 2):
                    text.Append((char)int.Parse(literal.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                case 'u' when i + 4 < literal.Length && IsHex(literal, i + 1, 4):
                    text.Append((char)int.Parse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: text.Append(next); break;
            }
        }

        return text.ToString();

        static bool IsHex(string s, int start, int length) => s.AsSpan(start, length).ToString().All(Uri.IsHexDigit);
    }

    private static WikiPageInfo ParseSaved(string body, string name, string title, string html, WikiPageInfo? existing)
    {
        using var document = ParseJson(body, "the saved wiki page");
        var root = document.RootElement;
        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            throw new PanoramaException("Panorama did not save the wiki page" + (ApiError(body) is { } why ? $": {why}" : "."));
        }

        var props = root.TryGetProperty("wikiProps", out var p) ? p : root;
        return new WikiPageInfo(
            Text(props, "entityId") ?? existing?.EntityId ?? "", Int(props, "rowId") ?? existing?.RowId,
            Int(props, "pageVersionId"), name, title, html, existing?.Parent, existing?.ShowAttachments ?? true, existing?.ShouldIndex ?? true);
    }

    /// <summary>The reason in a LabKey API refusal ({"exception": "..."}), if the body is one.</summary>
    private static string? ApiError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("exception", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonDocument ParseJson(string body, string what)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new PanoramaException($"Panorama did not send {what}; the sign-in was probably not accepted.", ex)
            {
                IsSignInProblem = true,
            };
        }
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static string? JsString(string props, string key) =>
        Regex.Match(props, $@"(?m)^\s*{key}:\s*'((?:[^'\\]|\\.)*)'", RegexOptions.Singleline) is { Success: true } m
            ? DecodeJsString(m.Groups[1].Value)
            : null;

    private static int? JsInt(string props, string key) =>
        Regex.Match(props, $@"(?m)^\s*{key}:\s*(-?\d+)") is { Success: true } m
            ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;

    private static bool? JsBool(string props, string key) =>
        Regex.Match(props, $@"(?m)^\s*{key}:\s*(true|false)") is { Success: true } m ? m.Groups[1].Value == "true" : null;
}
