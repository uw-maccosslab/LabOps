using LabOps.Core.Infrastructure;

namespace LabOps.Core.Protocols;

/// <summary>
/// The files a protocol comes from: documents protocol.py reads (Word, PDF, LaTeX, Markdown,
/// text) and anything else kept as it is, method files above all (a KingFisher or other robot
/// program, an instrument method).
/// </summary>
public static class ProtocolFiles
{
    private static readonly string[] DocumentExtensions = [".docx", ".doc", ".pdf", ".md", ".txt", ".tex"];

    /// <summary>The file dialogs' filter: any file first, since method files have all kinds of extensions.</summary>
    public const string Filter =
        "All files (*.*)|*.*|Protocol documents (*.docx;*.doc;*.pdf;*.md;*.txt;*.tex)|*.docx;*.doc;*.pdf;*.md;*.txt;*.tex";

    public static bool IsDocument(string path) =>
        DocumentExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The chosen files, for the box that shows them: "a.pdf", or "3 files: a.pdf, b.pdf, c.bdz".</summary>
    public static string Describe(IReadOnlyList<string> files) => files.Count switch
    {
        0 => "",
        1 => files[0],
        _ => $"{files.Count} files: {string.Join(", ", files.Select(Path.GetFileName))}",
    };

    /// <summary>
    /// The file a link on a protocol's page names, when it is one in a protocol's folder of the
    /// LabOps-Protocols copy at <paramref name="clone"/> (the page links a method by where its file
    /// is); null for anything else.
    /// </summary>
    public static string? Linked(string uri, string? clone)
    {
        if (clone is null || !Uri.TryCreate(uri, UriKind.Absolute, out var link) || !link.IsFile)
        {
            return null;
        }

        var path = Path.GetFullPath(link.LocalPath);
        var protocols = Path.Combine(clone, "protocols");
        // The engine writes the link from where the copy really is (Python's resolve() follows
        // junctions, links and subst drives, and gives a mapped drive's network path), which can be
        // spelled differently from the copy's path here. The linked file is not touched until it is
        // known to be inside, so a link to another computer's share is never opened.
        var inside = IsUnder(path, Path.GetFullPath(protocols)) || (FinalPath.Of(protocols) is { } real && IsUnder(path, real));
        return inside && File.Exists(path) ? path : null;
    }

    private static bool IsUnder(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
