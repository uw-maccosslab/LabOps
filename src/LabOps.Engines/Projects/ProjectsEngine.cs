using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>What a command answers: the JSON (which starts with "ok"), the lines it prints as text, and its exit code.</summary>
public sealed record CommandResult(JsonObject Payload, IReadOnlyList<string> Lines, int ExitCode = 0);

/// <summary>
/// The commands of LabOps-Projects' engine, over one clone. The app calls them in-process; the
/// labops tool (labops projects ...) runs them for Claude, the pre-commit hook and GitHub Actions.
/// Each is a port of the project.py command of the same name, with the same JSON and text.
/// </summary>
public sealed partial class ProjectsEngine(ProjectRepository repo)
{
    /// <summary>The engine's version: the app's, since the engine ships inside it.</summary>
    public static string Version { get; } =
        (typeof(ProjectsEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public ProjectRepository Repository { get; } = repo;

    private static JsonArray Problems(IEnumerable<(string Level, string Message)> found) => Values.Issues(found);

    /// <summary>The summary's current step entry, or null when every step is done or skipped.</summary>
    private static JsonObject? Current(JsonObject d) =>
        d["stages"]!.AsArray().OfType<JsonObject>().FirstOrDefault(s => JsonNode.DeepEquals(s["stage"], d["current_stage"]));

    private static string Who(JsonObject d) =>
        Current(d) is { } c && c["assigned"]?.GetValue<string>() is { Length: > 0 } a ? $" ({a})" : "";

    private static string Str(JsonNode? n) => n?.GetValue<string>() ?? "";

    /// <summary>list [--active]: every lab, project and experiment.</summary>
    public CommandResult List(bool activeOnly = false)
    {
        var (labs, problems, hidden) = Summaries.Collect(Repository, activeOnly);
        var lines = new List<string>();
        foreach (var lab in labs)
        {
            lines.Add($"{Str(lab["lab"]),-28} {Str(lab["title"])}");
            foreach (var p in lab["projects"]!.AsArray().OfType<JsonObject>())
            {
                var stage = p["current_stage"] is { } s ? Str(s) : "done";
                lines.Add($"  {Str(p["project"]),-30} {Str(p["status"]),-8} samples: {stage}{Who(p)}");
                foreach (var e in p["experiments"]!.AsArray().OfType<JsonObject>())
                {
                    var eStage = e["current_stage"] is { } es ? Str(es) : "complete";
                    lines.Add($"      {Str(e["experiment"]),-30} {Str(e["status"]),-8} {eStage}{Who(e)}");
                }
            }
        }

        if (hidden > 0)
        {
            lines.Add($"({hidden} closed project(s) not shown)");
        }

        var payload = new JsonObject
        {
            ["ok"] = true,
            ["engine_version"] = Version,
            ["labs"] = new JsonArray([.. labs]),
            // "projects" was the top level before labs existed; an older app reads it, finds none,
            // and config/app.yaml tells it to update.
            ["projects"] = new JsonArray(),
            ["people"] = new JsonArray([.. Repository.PeopleList().Select(p => (JsonNode?)new JsonObject
            {
                ["login"] = p.Login, ["name"] = Values.Json(p.Name), ["role"] = Values.Json(p.Role),
            })]),
            ["instruments"] = new JsonArray([.. Repository.Instruments().Select(i => (JsonNode?)i)]),
            ["closed_hidden"] = hidden,
            ["problems"] = Problems(problems),
        };
        return new CommandResult(payload, lines);
    }

    /// <summary>check [--staged]: validate everything, or (the pre-commit check) every staged file.</summary>
    public CommandResult Check(bool staged = false)
    {
        var found = new List<(string Level, string Message)>();
        if (staged)
        {
            found.AddRange(FileChecks.CheckStaged(Repository));
        }
        else
        {
            var (labs, problems, _) = Summaries.Collect(Repository);
            found.AddRange(problems);
            foreach (var lab in labs)
            {
                found.AddRange(Issues(lab, Str(lab["lab"])));
                foreach (var p in lab["projects"]!.AsArray().OfType<JsonObject>())
                {
                    found.AddRange(Issues(p, Str(p["project"])));
                    foreach (var e in p["experiments"]!.AsArray().OfType<JsonObject>())
                    {
                        found.AddRange(Issues(e, Str(e["experiment"])));
                    }
                }
            }

            // A record that cannot be read is reported above, with its project.
            if (Directory.Exists(Repository.Projects))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
                foreach (var path in Directory.EnumerateFiles(Repository.Projects, "*", options).Order(PathOrder.Instance))
                {
                    found.AddRange(FileChecks.CheckFile(Repository.Rel(path), File.ReadAllBytes(path), parseErrors: false)
                        .Select(f => (f.Level, f.Text())));
                }
            }
        }

        var errors = found.Count(f => f.Level == "ERROR");
        var lines = found.Select(f => $"{f.Level}: {f.Message}").Append($"check: {errors} error(s), {found.Count - errors} warning(s)").ToList();
        return new CommandResult(new JsonObject { ["ok"] = errors == 0, ["problems"] = Problems(found) }, lines, errors > 0 ? 1 : 0);
    }

    private static IEnumerable<(string, string)> Issues(JsonObject d, string name) =>
        d["issues"]!.AsArray().OfType<JsonObject>().Select(i => (Str(i["level"]), $"{name}: {Str(i["message"])}"));

    /// <summary>scan &lt;file&gt;: look for identifiers in a sheet. `shownAs` is the path as typed.</summary>
    public CommandResult Scan(string path, string? shownAs = null)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new EngineError($"{shownAs ?? path} does not exist");
        }

        var sheets = Sheets.Read(path);
        var findings = new List<Deidentification.Finding>();
        var summary = new JsonArray();
        var name = Path.GetFileName(path);
        foreach (var (sheet, headers, rows) in sheets)
        {
            findings.AddRange(Deidentification.SheetFindings(headers, rows, sheets.Count > 1 ? $"{name}:{sheet}" : name));
            summary.Add((JsonNode)new JsonObject
            {
                ["sheet"] = sheet, ["rows"] = rows.Count, ["columns"] = new JsonArray([.. headers.Select(h => (JsonNode?)h)]),
            });
        }

        var errors = findings.Count(f => f.Level == "ERROR");
        var lines = sheets.Select(s => $"{s.Name}: {s.Rows.Count} rows; columns: {string.Join(", ", s.Headers.Select(c => c.Length > 0 ? c : "(blank)"))}").ToList();
        lines.AddRange(findings.Select(f => $"{f.Level}: {f.Text()}"));
        lines.Add($"scan: {errors} error(s), {findings.Count - errors} warning(s)");
        return new CommandResult(new JsonObject
        {
            ["ok"] = true, ["sheets"] = summary, ["findings"] = new JsonArray([.. findings.Select(f => (JsonNode?)f.Json())]), ["errors"] = errors,
        }, lines);
    }

    /// <summary>sheet &lt;file&gt; [--sheet NAME] [--rows N]: a sheet as CSV, for Claude (rows 0 is every row).</summary>
    public CommandResult Sheet(string path, string? sheet = null, int rows = 0)
    {
        var sheets = Sheets.Read(path, sheet);
        var output = new JsonArray();
        var lines = new List<string>();
        foreach (var (name, headers, all) in sheets)
        {
            var shown = rows > 0 ? all.Take(rows) : all;
            var cells = shown.Select(r => r.Select(Sheets.CellText).ToList()).ToList();
            output.Add((JsonNode)new JsonObject
            {
                ["sheet"] = name,
                ["headers"] = new JsonArray([.. headers.Select(h => (JsonNode?)h)]),
                ["rows"] = new JsonArray([.. cells.Select(r => (JsonNode?)new JsonArray([.. r.Select(c => (JsonNode?)c)]))]),
                ["total_rows"] = all.Count,
            });
            if (sheets.Count > 1)
            {
                lines.Add($"# sheet: {name} ({all.Count} rows)");
            }

            lines.Add(CsvRow(headers));
            lines.AddRange(cells.Select(CsvRow));
        }

        return new CommandResult(new JsonObject { ["ok"] = true, ["sheets"] = output }, lines);
    }

    /// <summary>csv.writer's row with the excel dialect: minimal quoting, and a lone empty field quoted so the row is not empty.</summary>
    public static string CsvRow(IReadOnlyList<string> fields)
    {
        if (fields.Count == 1 && fields[0].Length == 0)
        {
            return "\"\"";
        }

        return string.Join(',', fields.Select(f => f.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + f.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : f));
    }

    /// <summary>wiki &lt;project&gt; [--documents JSON] [--out FILE] [--date D]: the project's wiki page.</summary>
    public CommandResult Wiki(string project, string? documentsFile = null, string? outFile = null, DateOnly? date = null)
    {
        var folder = Repository.FindProject(project);
        var documents = new PyDict();
        if (!string.IsNullOrEmpty(documentsFile))
        {
            object? doc;
            try
            {
                var bytes = File.ReadAllBytes(documentsFile);
                var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                doc = PyJsonDecoder.Loads(Encoding.UTF8.GetString(bytes, start, bytes.Length - start));
            }
            catch (Exception ex) when (ex is IOException or PyJsonException or UnauthorizedAccessException or DecoderFallbackException)
            {
                throw new EngineError($"could not read {documentsFile}: {ex.Message}");
            }

            documents = doc as PyDict ?? throw new EngineError($"{documentsFile} should map each Panorama folder to its Skyline documents");
        }

        var page = WikiPage.Render(Repository, folder, documents, date ?? Repository.Today());
        var lines = new List<string>
        {
            $"{Path.GetFileName(folder)}: page {page.PageName} in {page.Folder ?? "(no Panorama folder recorded; labops projects link <project> wiki <folder>)"}",
            $"    written parts: {(page.Written ? Repository.Rel(Path.Combine(folder, Validation.WikiFile)) : "none yet (the update-wiki skill writes them)")}",
        };
        if (!string.IsNullOrEmpty(outFile))
        {
            var target = Path.IsPathRooted(outFile) ? outFile : Path.Combine(Repository.Root, outFile);
            YamlText.WriteText(target, page.Html);
            lines.Add($"    wrote {(Repository.IsUnder(target, Repository.Root) ? Repository.Rel(target) : target)}");
        }

        return new CommandResult(new JsonObject
        {
            ["ok"] = true, ["project"] = page.Project, ["folder"] = Values.Json(page.Folder), ["page"] = page.PageName,
            ["title"] = page.Title, ["html"] = page.Html, ["written"] = page.Written, ["written_hash"] = page.WrittenHash,
        }, lines);
    }

    [GeneratedRegex("<!-- INDEX:START.*?<!-- INDEX:END -->", RegexOptions.Singleline)]
    private static partial Regex IndexBlock();

    /// <summary>
    /// index: rebuild the README's table of the labs, projects and experiments still open. Closed
    /// projects (not read beyond their status) and closed experiments are left out, so the table
    /// stays the size of the current work however many years of projects the repository holds; a
    /// line says how many.
    /// </summary>
    public CommandResult Index()
    {
        var (labs, _, hidden) = Summaries.Collect(Repository, activeOnly: true);
        static string Cell(JsonNode? text) =>
            string.Join(' ', PyText.Split(text?.GetValue<string>() ?? "")).Replace("|", "\\|", StringComparison.Ordinal);
        string Now(JsonObject d) => Current(d) is { } c ? Cell(c["label"]) : "complete";
        string Assigned(JsonObject d) => Current(d) is { } c ? Cell(c["assigned"]) : "";
        static string Money(JsonNode? f)
        {
            var text = Str(f!["type"]);
            var quotes = f["quotes"]!.AsArray().Select(Str).ToList();
            if (quotes.Count > 0)
            {
                text += ": " + string.Join(", ", quotes);
            }
            else if (f["grant"] is { } grant)
            {
                text += $": {Str(grant)}";
            }

            return Cell(text);
        }

        var output = new List<string>
        {
            "| Lab | Project | Experiment | Status | Current step | Assigned | Funding |",
            "| --- | --- | --- | --- | --- | --- | --- |",
        };
        int shownLabs = 0, projects = 0, experiments = 0, closedExperiments = 0;
        foreach (var lab in labs)
        {
            var shown = false;
            foreach (var p in lab["projects"]!.AsArray().OfType<JsonObject>())
            {
                shown = true;
                projects++;
                output.Add($"| [{Str(lab["lab"])}]({Str(lab["folder"])}/) | [{Str(p["project"])}]({Str(p["folder"])}/) | samples | {Cell(p["status"])} | "
                           + $"{Now(p)} | {Assigned(p)} | {Money(p["funding"])} |");
                foreach (var e in p["experiments"]!.AsArray().OfType<JsonObject>())
                {
                    // A closed experiment of an open project is finished work too.
                    if (e["status"]?.GetValue<string>() == "closed")
                    {
                        closedExperiments++;
                        continue;
                    }

                    experiments++;
                    output.Add($"| | | [{Str(e["experiment"])}]({Str(e["folder"])}/) | {Cell(e["status"])} | {Now(e)} | {Assigned(e)} | "
                               + $"{(e["funding_inherited"]!.GetValue<bool>() ? "" : Money(e["funding"]))} |");
                }
            }

            shownLabs += shown ? 1 : 0;
        }

        var left = new List<string>();
        if (hidden > 0)
        {
            left.Add($"{hidden} closed project{(hidden == 1 ? "" : "s")}");
        }

        if (closedExperiments > 0)
        {
            left.Add($"{closedExperiments} closed experiment{(closedExperiments == 1 ? "" : "s")} of open projects");
        }

        if (left.Count > 0)
        {
            output.Add("");
            output.Add($"Not listed: {string.Join(" and ", left)}; `labops projects list` lists everything.");
        }

        var block = "<!-- INDEX:START (generated by labops projects index; do not edit) -->\n\n"
                    + string.Join('\n', output) + "\n\n<!-- INDEX:END -->";
        var readme = Path.Combine(Repository.Root, "README.md");
        var text = YamlText.ReadText(readme, "README.md");
        text = text.Contains("<!-- INDEX:START", StringComparison.Ordinal)
            ? IndexBlock().Replace(text, _ => block)
            : Py.RStrip(text, "\n") + "\n\n## Index\n\n" + block + "\n";
        YamlText.WriteText(readme, text);
        return new CommandResult(
            new JsonObject
            {
                ["ok"] = true, ["labs"] = shownLabs, ["projects"] = projects, ["experiments"] = experiments,
                ["closed_hidden"] = hidden, ["closed_experiments_hidden"] = closedExperiments,
            },
            [$"README index: {shownLabs} labs, {projects} projects, {experiments} experiments"
             + (left.Count > 0 ? $" (not listed: {string.Join(" and ", left)})" : "")]);
    }
}
