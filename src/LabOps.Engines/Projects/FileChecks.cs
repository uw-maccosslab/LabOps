using System.Diagnostics;
using System.Text;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;
using static LabOps.Engines.Projects.Deidentification;

namespace LabOps.Engines.Projects;

/// <summary>
/// What may be committed under projects/, file by file: only text the identifier check can read,
/// with records and the wiki text keeping contact details off the shared wiki page, and the
/// pre-commit check that runs this on every staged file (check --staged).
/// </summary>
public static class FileChecks
{
    // Only text the identifier check can read is committed under projects/. Sheets are converted to
    // CSV first: a workbook can hide columns, sheets and comments that nobody looked at, and a PDF or
    // document cannot be checked at all. Originals stay in inbox/.
    public static readonly IReadOnlySet<string> AllowedInProjects = new HashSet<string>(StringComparer.Ordinal) { ".yaml", ".yml", ".csv", ".json", ".md" };

    // Sheets, documents and archives, which are never committed anywhere in the repository.
    public static readonly IReadOnlySet<string> NeverCommitted = new HashSet<string>(StringComparer.Ordinal)
    {
        ".xlsx", ".xlsm", ".xlsb", ".xls", ".ods", ".numbers", ".tsv", ".pdf", ".doc", ".docx", ".odt",
        ".rtf", ".ppt", ".pptx", ".zip", ".7z", ".sav", ".dta", ".sas7bdat", ".rds", ".rdata",
    };

    public const int MaxReceivedBytes = 5 * 1024 * 1024;

    // The records, each at its place; a file of the same name anywhere else is data.
    private static readonly Dictionary<string, int> RecordDepths = new(StringComparer.Ordinal)
    {
        ["lab.yaml"] = 3, ["project.yaml"] = 4, ["experiment.yaml"] = 5,
    };

    // What a project's wiki page shows from each record. Collaborators can read the page, so these
    // fields get the rule wiki.yaml gets: no email addresses or phone numbers.
    private static readonly Dictionary<string, string[]> PublishedFields = new(StringComparer.Ordinal)
    {
        ["lab.yaml"] = ["pi", "institution"],
        ["project.yaml"] = ["title", "funding", "sample_type", "species", "lab_contact", "steps", "protocols"],
        ["experiment.yaml"] = ["title", "instrument", "steps", "protocols", "panorama"],
    };

    /// <summary>What a file under projects/ is: "record", "wiki" (a project's wiki.yaml) or "data".</summary>
    public static string Role(string name)
    {
        var parts = name.Split('/');
        if (RecordDepths.TryGetValue(parts[^1], out var depth) && depth == parts.Length)
        {
            return "record";
        }

        return parts[^1] == Validation.WikiFile && parts.Length == 4 ? "wiki" : "data";
    }

    public static List<Finding> PublishedFindings(PyDict d, string fileName, string source) =>
        [.. PublishedFields[fileName].SelectMany(key => Strings(d[key], key))
            .Where(s => HasContactDetails(s.Text)).Select(s => Error(source, SharedPage, s.Where))];

    /// <summary>The page is shared with the collaborators: no email addresses or phone numbers in it.</summary>
    public static List<Finding> WikiFindings(string text, string name)
    {
        object? w;
        try
        {
            w = YamlLoader.Load(text);
        }
        catch (YamlProblemException ex)
        {
            return [Error(name, $"is not valid YAML, so it cannot be checked ({ex.Problem} (line {ex.Line}))")];
        }

        return [.. Strings(w).Where(s => HasContactDetails(s.Text)).Select(s => Error(name, SharedPage, s.Where))];
    }

    /// <summary>
    /// check_file: the rules for one file under projects/, from its repository path and what it holds.
    /// `parseErrors` false leaves a record that does not parse to the structure check, which reports it.
    /// </summary>
    public static List<Finding> CheckFile(string name, byte[] data, bool parseErrors = true)
    {
        var fileName = name[(name.LastIndexOf('/') + 1)..];
        if (fileName == ".gitkeep")
        {
            return [];
        }

        if (!AllowedInProjects.Contains(Suffix(name)))
        {
            return [Error(name, "only CSV, YAML, JSON and Markdown files are committed here; convert a sheet to CSV "
                                + "(labops projects sheet) and keep originals such as spreadsheets and PDFs in inbox/")];
        }

        string text;
        try
        {
            text = Sheets.DecodeText(data, name);
        }
        catch (EngineError ex)
        {
            return [Error(name, $"could not be read, so it cannot be checked: {ex.Message}")];
        }

        switch (Role(name))
        {
            case "record":
                var (d, problem) = Records.Parse(text, fileName);
                if (problem is not null)
                {
                    return parseErrors ? [Error(name, problem)] : [];
                }

                return PublishedFindings(d, fileName, name);
            case "wiki":
                return WikiFindings(text, name);
            default:
                var found = new List<Finding>();
                if (name.Contains("/metadata/received/", StringComparison.Ordinal) && data.Length > MaxReceivedBytes)
                {
                    found.Add(Error(name, $"is larger than {MaxReceivedBytes / (1024 * 1024)} MB"));
                }

                found.AddRange(DataFindings(name, text));
                return found;
        }
    }

    /// <summary>
    /// The structure rules a staged record or wiki.yaml must meet, from its text and path alone, so
    /// that a file the listing could not use never reaches the others.
    /// </summary>
    public static List<(string Level, string Message)> StagedRules(string name, string text, IReadOnlySet<string> knownPeople)
    {
        var parts = name.Split('/');
        var role = Role(name);
        if (role == "wiki")
        {
            try
            {
                return Validation.WikiRules(YamlLoader.Load(text), name);
            }
            catch (YamlProblemException)
            {
                return [];
            }
        }

        if (role != "record")
        {
            return [];
        }

        var (d, problem) = Records.Parse(text, parts[^1]);
        if (problem is not null)
        {
            return [];
        }

        return parts[^1] switch
        {
            "lab.yaml" => Validation.LabRules(d, parts[^2]),
            "project.yaml" => Validation.ProjectRules(d, parts[^2], knownPeople),
            _ => Validation.ExperimentRules(d, parts[^2], knownPeople),
        };
    }

    private static readonly string[] FileModes = ["100644", "100755"];   // a link is 120000 and a submodule 160000

    /// <summary>
    /// The pre-commit check: every file the commit adds or changes, as it is staged, wherever it is.
    /// Under projects/ the identifier rules and the records' structure; elsewhere, no sheet or
    /// document is committed at all and a CSV gets the identifier rules.
    /// </summary>
    public static List<(string Level, string Message)> CheckStaged(ProjectRepository repo)
    {
        var entries = StagedFiles(repo.Root);
        var contents = ReadBlobs(repo.Root, [.. entries.Where(e => FileModes.Contains(e.Mode)).Select(e => e.Blob)]);
        var known = repo.People();
        var found = new List<(string, string)>();
        foreach (var (mode, blob, name) in entries)
        {
            var lower = name.ToLowerInvariant();
            if (lower.StartsWith("inbox/", StringComparison.Ordinal))
            {
                found.Add(("ERROR", $"{name}: inbox/ holds originals and is never committed; unstage it"));
                continue;
            }

            var underProjects = lower.StartsWith("projects/", StringComparison.Ordinal);
            if (!FileModes.Contains(mode))
            {
                if (underProjects)
                {
                    found.Add(("ERROR", $"{name}: only files are committed under projects/, not links or submodules"));
                }

                continue;
            }

            var data = contents.GetValueOrDefault(blob, []);
            List<Finding> findings;
            if (underProjects)
            {
                findings = CheckFile(name, data);
                try
                {
                    findings.AddRange(StagedRules(name, Sheets.DecodeText(data, name), known)
                        .Where(r => r.Level == "ERROR").Select(r => Error(name, r.Message)));
                }
                catch (EngineError)
                {
                    // unreadable text, which CheckFile has reported
                }
            }
            else if (NeverCommitted.Contains(Suffix(lower)))
            {
                findings = [Error(name, "spreadsheets, PDFs and documents are never committed; keep the original in "
                                        + "inbox/<project>/ and commit a CSV conversion under the project's metadata/received/")];
            }
            else if (Suffix(lower) == ".csv")
            {
                try
                {
                    findings = DataFindings(name, Sheets.DecodeText(data, name));
                }
                catch (EngineError ex)
                {
                    findings = [Error(name, $"could not be read, so it cannot be checked: {ex.Message}")];
                }
            }
            else
            {
                continue;
            }

            found.AddRange(findings.Select(f => (f.Level, f.Text())));
        }

        return found;
    }

    /// <summary>(mode, blob, path) for every file the commit adds, copies, modifies, renames (at its new path) or changes in type.</summary>
    private static List<(string Mode, string Blob, string Path)> StagedFiles(string root)
    {
        var output = Encoding.UTF8.GetString(Git(root, ["diff", "--cached", "--raw", "-z", "--no-abbrev", "--diff-filter=ACMRT"], null)).Split('\0');
        var entries = new List<(string, string, string)>();
        var i = 0;
        while (i < output.Length && output[i].StartsWith(':'))
        {
            // ":old-mode new-mode old-blob new-blob STATUS", then the path(s)
            var fields = output[i][1..].Split(' ');
            var (mode, blob, status) = (fields[1], fields[3], fields[4]);
            var paths = status.Length > 0 && status[0] is 'R' or 'C' ? 2 : 1;
            entries.Add((mode, blob, output[i + paths]));
            i += 1 + paths;
        }

        return entries;
    }

    /// <summary>What each blob holds, read with one git process rather than one per file.</summary>
    private static Dictionary<string, byte[]> ReadBlobs(string root, IReadOnlyList<string> blobs)
    {
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (blobs.Count == 0)
        {
            return contents;
        }

        var output = Git(root, ["cat-file", "--batch"], Encoding.ASCII.GetBytes(string.Concat(blobs.Select(b => b + "\n"))));
        var pos = 0;
        foreach (var blob in blobs)
        {
            // each answer is "<blob> blob <size>\n<content>\n", or "<blob> missing\n"
            var end = Array.IndexOf(output, (byte)'\n', pos);
            var header = Encoding.ASCII.GetString(output, pos, end - pos).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            pos = end + 1;
            if (header[^1] != "missing")
            {
                var size = int.Parse(header[2], System.Globalization.CultureInfo.InvariantCulture);
                contents[blob] = output[pos..(pos + size)];
                pos += size + 1;
            }
        }

        return contents;
    }

    private static byte[] Git(string root, IReadOnlyList<string> args, byte[]? input)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }

        using var process = Process.Start(start) ?? throw new EngineError("git could not be started");
        var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            // Write on another thread: git answers as it reads, and a full pipe would stop both.
            var writer = Task.Run(() =>
            {
                process.StandardInput.BaseStream.Write(input);
                process.StandardInput.Close();
            });
            using var buffer = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(buffer);
            writer.Wait();
            process.WaitForExit();
            return process.ExitCode == 0 ? buffer.ToArray() : throw new EngineError($"git {args[0]} failed: {stderr.Result.Trim()}");
        }

        using (var buffer = new MemoryStream())
        {
            process.StandardOutput.BaseStream.CopyTo(buffer);
            process.WaitForExit();
            return process.ExitCode == 0 ? buffer.ToArray() : throw new EngineError($"git {args[0]} failed: {stderr.Result.Trim()}");
        }
    }
}
