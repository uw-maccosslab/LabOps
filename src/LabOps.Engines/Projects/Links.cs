using System.Text;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;

namespace LabOps.Engines.Projects;

/// <summary>
/// Links. An experiment lists its Panorama folders (raw data, and results shared there); a project
/// or an experiment lists LabKey ELN notebooks, which have no API, so a notebook is its ID and a
/// link. People paste whatever address their browser shows, so a Panorama URL of any form is
/// reduced to the folder path. Ported from project.py, with Python's urllib.parse behavior.
/// </summary>
public static partial class Links
{
    public const string PanoramaHost = "panoramaweb.org";

    public const string ProtocolComment = "Protocols from LabOps-Protocols, each at the version used and for its step "
                                          + "(project.py link <item> protocol <id> --version N --step STEP).";

    // A notebook ID ends in the notebook's site-wide row ID (ELN-{user}-{date}-{row}, ELN-{date}-{row}
    // or ELN-{row}), which is also where it opens.
    [GeneratedRegex(@"^ELN-(?:.*-)?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NotebookPattern();

    [GeneratedRegex(@"^/?[A-Za-z]:[\\/]")]
    private static partial Regex LocalPath();

    [GeneratedRegex(@"^https?://", RegexOptions.IgnoreCase)]
    public static partial Regex WebAddress();

    [GeneratedRegex(@"\.(view|api|post)$")]
    private static partial Regex ControllerAction();

    /// <summary>The link for an ELN ID such as ELN-1567-20250730-131, or null for anything else.</summary>
    public static string? NotebookUrl(string? notebookId)
    {
        var m = NotebookPattern().Match(Py.Strip(notebookId ?? ""));
        return m.Success ? $"https://{PanoramaHost}/MacCoss/samplemanager-app.view#/notebooks/{m.Groups[1].Value}" : null;
    }

    /// <summary>urllib.parse.urlsplit's parts that matter here: the host name, the path and the query.</summary>
    public static (string? Host, string Path, string Query) SplitUrl(string url)
    {
        var rest = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var netlocEnd = rest.IndexOfAny(['/', '?', '#']);
        var netloc = netlocEnd < 0 ? rest : rest[..netlocEnd];
        rest = netlocEnd < 0 ? "" : rest[netlocEnd..];
        var fragment = rest.IndexOf('#');
        if (fragment >= 0)
        {
            rest = rest[..fragment];
        }

        var q = rest.IndexOf('?');
        var path = q < 0 ? rest : rest[..q];
        var query = q < 0 ? "" : rest[(q + 1)..];
        var host = netloc[(netloc.LastIndexOf('@') + 1)..];
        if (host.StartsWith('['))
        {
            host = host[1..Math.Max(1, host.IndexOf(']'))];
        }
        else if (host.IndexOf(':') is var colon and >= 0)
        {
            host = host[..colon];
        }

        return (host.Length > 0 ? host.ToLowerInvariant() : null, path, query);
    }

    /// <summary>urllib.parse.unquote: %XX as UTF-8, with invalid sequences replaced.</summary>
    public static string Unquote(string text)
    {
        if (!text.Contains('%', StringComparison.Ordinal))
        {
            return text;
        }

        var output = new StringBuilder();
        var bytes = new List<byte>();
        void Flush()
        {
            if (bytes.Count > 0)
            {
                output.Append(Encoding.UTF8.GetString([.. bytes]));
                bytes.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                Flush();
                output.Append(text[i]);
            }
        }

        Flush();
        return output.ToString();
    }

    /// <summary>parse_qs(query).get(name, [""])[0]: the first non-blank value, with + as a space.</summary>
    public static string QueryValue(string query, string name)
    {
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = Unquote(pair[..eq].Replace('+', ' '));
            var value = Unquote(pair[(eq + 1)..].Replace('+', ' '));
            if (key == name && value.Length > 0)
            {
                return value;
            }
        }

        return "";
    }

    /// <summary>
    /// /MacCoss/maccoss/2026-BioTRACK from that path or any Panorama address for it:
    /// .../MacCoss/maccoss/2026-BioTRACK/project-begin.view (controller-action last) or
    /// .../project/MacCoss/maccoss/2026-BioTRACK/begin.view (controller first). A file area keeps its
    /// place: .../_webdav/MacCoss/maccoss/@files/2026-BioTRACK/ is /MacCoss/maccoss/@files/2026-BioTRACK.
    /// </summary>
    public static string PanoramaFolder(string text)
    {
        text = Py.Strip(text);
        // A path on this computer is never a Panorama folder. Git Bash turns /MacCoss/... into
        // C:/Program Files/Git/MacCoss/... before the command sees it, so say how to avoid that.
        if (LocalPath().IsMatch(text))
        {
            throw new EngineError($"{text} is a path on this computer, not a Panorama folder. In Git Bash, start the command "
                                  + "with MSYS_NO_PATHCONV=1, or paste the folder's address from the browser");
        }

        List<string> parts;
        if (WebAddress().IsMatch(text))
        {
            var (host, path, _) = SplitUrl(text);
            if (!(host ?? "").EndsWith(PanoramaHost, StringComparison.Ordinal))
            {
                throw new EngineError($"{text} is not a Panorama address (https://{PanoramaHost}/...)");
            }

            parts = [.. path.Split('/').Where(s => s.Length > 0).Select(Unquote)];
            if (parts.Count > 0 && parts[0] == "_webdav")
            {
                parts = parts[1..];
            }
            else if (parts.Count > 0 && ControllerAction().IsMatch(parts[^1]))
            {
                // Python slices are forgiving: parts[1:-1] of one part is no parts.
                parts = parts[^1].Contains('-', StringComparison.Ordinal) ? parts[..^1] : parts.Count >= 2 ? parts[1..^1] : [];
            }
        }
        else
        {
            parts = [.. text.Replace('\\', '/').Split('/').Where(s => s.Length > 0)];
        }

        if (parts.Count == 0)
        {
            throw new EngineError($"{PyText.ReprString(text)} does not name a Panorama folder");
        }

        return "/" + string.Join('/', parts);
    }
}
