using LabOps.Core.Projects;

namespace LabOps.App.Services;

/// <summary>
/// What the projects' identifier scan said of a collaborator's file, in the words both Organize
/// metadata and the chat's attachments use, so the rule for what reaches Claude reads the same
/// everywhere.
/// </summary>
public static class IdentifierCheck
{
    /// <summary>How to fix a refused file.</summary>
    public const string Fix = "Remove those columns, or replace them with a study code";

    /// <summary>Each sheet with its size: "Samples: 40 rows, 12 columns".</summary>
    public static string Sheets(ScanResult scan) =>
        string.Join("; ", scan.Sheets.Select(s => $"{s.Sheet}: {s.Rows} rows, {s.Columns.Count} columns"));

    /// <summary>The findings that stop the file reaching Claude, one per line.</summary>
    public static string Errors(ScanResult scan) =>
        "- " + string.Join("\n- ", scan.Findings.Where(f => f.IsError).Select(f => f.ToString()));

    public static IReadOnlyList<string> Warnings(ScanResult scan) => [.. scan.Warnings.Select(w => w.ToString())];

    /// <summary>What Claude is told of a file the scan let through; null when the scan refuses it.</summary>
    public static string? Note(ScanResult scan)
    {
        if (scan.Errors > 0)
        {
            return null;
        }

        var warnings = Warnings(scan);
        return $"scanned by the app: {Sheets(scan)}; nothing identifying"
            + (warnings.Count == 0 ? "" : $"; warnings to check: {string.Join("; ", warnings)}");
    }
}
