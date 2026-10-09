using System.Security.Cryptography;
using System.Text;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;
using static LabOps.Engines.Projects.Deidentification;

namespace LabOps.Engines.Projects;

/// <summary>
/// Free-text columns a person has read for identifiers. check warns about every free-text column
/// (notes, comments), since no rule can tell a note from a name; once someone has read one and
/// found nothing identifying, `labops projects review` records it in the project's (or
/// experiment's) record under `reviewed:`, with a fingerprint of the column's text. check then
/// says nothing more about it, until the text changes.
/// </summary>
public static class Reviews
{
    public const string Key = "reviewed";

    public const string Comment = "Free-text columns a person read for identifiers (labops projects review); check warns again if one changes.";

    /// <summary>One recorded review: the file (relative to the record's folder), the column, and its text's fingerprint.</summary>
    public sealed record Review(string File, string Column, string Values, string? By, string? Date);

    /// <summary>
    /// The text's fingerprint: its distinct values, in order, so that more rows saying the same
    /// things, or the rows in another order, need no new review, and any new wording does.
    /// </summary>
    public static string Fingerprint(IEnumerable<string> values)
    {
        var distinct = values.Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', distinct))))[..12];
    }

    /// <summary>The record's reviews; entries that are not well formed are left to validation to report.</summary>
    public static List<Review> Read(PyDict record) =>
        record[Key] is List<object?> list
            ? [.. list.OfType<PyDict>()
                .Where(e => Values.Text(e["file"]) is not null && Values.Text(e["column"]) is not null && Values.Text(e["values"]) is not null)
                .Select(e => new Review(Values.Text(e["file"])!, Values.Text(e["column"])!, Values.Text(e["values"])!,
                    Values.Text(e["by"]), Values.Text(e["date"])))]
            : [];

    /// <summary>Validation of `reviewed:`: a list of {file, column, values, by, date}.</summary>
    public static IEnumerable<(string, string)> Rules(PyDict record)
    {
        if (record[Key] is null)
        {
            yield break;
        }

        if (record[Key] is not List<object?> list
            || list.Any(e => e is not PyDict d || Values.Text(d["file"]) is null || Values.Text(d["column"]) is null || Values.Text(d["values"]) is null))
        {
            yield return ("ERROR", $"{Key}: each entry is written like {{file: metadata/samples.csv, column: Notes, values: 1a2b3c4d5e6f, by: login, date: 2026-10-09}} (labops projects review writes them)");
        }
    }

    /// <summary>
    /// The folder of the project or experiment a repository path belongs to (the nearest folder
    /// above it holding project.yaml or experiment.yaml), or null outside one.
    /// </summary>
    public static string? Owner(ProjectRepository repo, string relPath)
    {
        var folder = Path.GetDirectoryName(Path.Combine(repo.Root, relPath.Replace('/', Path.DirectorySeparatorChar)));
        while (folder is not null && folder.Length > repo.Projects.Length && folder.StartsWith(repo.Projects, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(folder, "experiment.yaml")) || File.Exists(Path.Combine(folder, "project.yaml")))
            {
                return folder;
            }

            folder = Path.GetDirectoryName(folder);
        }

        return null;
    }

    /// <summary>
    /// The findings with each reviewed free-text column left out, or, when its text has changed
    /// since, its warning saying so. Records are read once per call.
    /// </summary>
    public static List<Finding> Apply(ProjectRepository repo, List<Finding> findings)
    {
        var records = new Dictionary<string, List<Review>>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<Finding>();
        foreach (var f in findings)
        {
            if (f.Fingerprint is null || f.Column is null || Owner(repo, f.File) is not { } owner)
            {
                kept.Add(f);
                continue;
            }

            if (!records.TryGetValue(owner, out var reviews))
            {
                records[owner] = reviews = ReadOwner(owner);
            }

            var file = Path.GetRelativePath(owner, Path.Combine(repo.Root, f.File.Replace('/', Path.DirectorySeparatorChar))).Replace('\\', '/');
            var review = reviews.LastOrDefault(r => r.File == file && r.Column == f.Column);
            if (review is null)
            {
                kept.Add(f);
            }
            else if (review.Values != f.Fingerprint)
            {
                var who = review.By is null ? "someone" : review.By;
                kept.Add(f with { Message = $"is free text and has changed since {who} read it for identifiers"
                                            + (review.Date is null ? "" : $" on {review.Date}") + "; check it again" });
            }
        }

        return kept;
    }

    private static List<Review> ReadOwner(string folder)
    {
        var path = File.Exists(Path.Combine(folder, "experiment.yaml")) ? Path.Combine(folder, "experiment.yaml") : Path.Combine(folder, "project.yaml");
        try
        {
            return YamlLoader.Load(YamlText.ReadText(path, Path.GetFileName(path))) is PyDict d ? Read(d) : [];
        }
        catch (Exception ex) when (ex is YamlProblemException or EngineError)
        {
            // A record that cannot be read is reported with its project; its reviews count for nothing.
            return [];
        }
    }
}
