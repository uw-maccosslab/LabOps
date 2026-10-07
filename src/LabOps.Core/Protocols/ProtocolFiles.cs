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
}
