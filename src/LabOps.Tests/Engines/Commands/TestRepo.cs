using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LabOps.Engines.CommandLine;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// A throwaway LabOps-Projects repository (the templates, config and README in
/// Fixtures/projects-repo) committed to a git history of its own, with helpers to run
/// `labops projects` in it and to read and edit its records. Commands go through the labops tool's
/// own command line (LabopsCommandLine.Run), in-process, so a test sees exactly what a person or
/// Claude would, without touching a real repository. These tests came from LabOps-Projects'
/// pytest suite (tests/conftest.py's Repo), which they replace once project.py is retired.
/// </summary>
internal sealed class TestRepo : IDisposable
{
    private static readonly TimeZoneInfo Seattle = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    private readonly TempDirectory _temp;

    // An empty file beside the repository that git reads as the global config (see RunGit).
    private readonly string _emptyGitConfig;

    private TestRepo(TempDirectory temp, string root)
    {
        _temp = temp;
        Root = root;
        _emptyGitConfig = temp.Combine("empty.gitconfig");
        File.WriteAllBytes(_emptyGitConfig, []);
    }

    public string Root { get; }

    /// <summary>
    /// Today in Seattle, the date the engine records a step on. Worked out here, as conftest.py's
    /// today() did, rather than asked of the engine (ProjectRepository.LabToday), so a time-zone
    /// mistake there gives a wrong date instead of agreeing with itself.
    /// </summary>
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Seattle));

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Fixtures/octopus: Octopus's own sample metadata (see the README there).</summary>
    public static string OctopusData(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "octopus", name);

    public static TestRepo Create()
    {
        var temp = new TempDirectory();
        var root = temp.Combine("repo");
        Parity.CopyFolder(Path.Combine(AppContext.BaseDirectory, "Fixtures", "projects-repo"), root);
        // Stored without their dots, so they do not apply to LabOps' own fixture folder.
        File.Move(Path.Combine(root, "gitattributes"), Path.Combine(root, ".gitattributes"));
        File.Move(Path.Combine(root, "gitignore"), Path.Combine(root, ".gitignore"));
        Directory.CreateDirectory(Path.Combine(root, "projects"));

        var repo = new TestRepo(temp, root);
        // No template: a template directory (GIT_TEMPLATE_DIR) can hold hooks of its own.
        repo.Git("init", "-q", "-b", "main", "--template=");
        repo.Git("config", "user.name", "Test");
        repo.Git("config", "user.email", "test@example.org");
        repo.Git("config", "core.autocrlf", "false");
        repo.Git("config", "commit.gpgsign", "false");
        repo.Commit("base");
        return repo;
    }

    public void Dispose() => _temp.Dispose();

    // -- running the engine ---------------------------------------------------------------------

    /// <summary>What a command printed, and its exit code.</summary>
    public sealed record Answer(int ExitCode, string Stdout, string Stderr)
    {
        public JsonObject Json => JsonNode.Parse(Stdout)!.AsObject();
    }

    /// <summary>`labops projects --json ARGS` in this repository.</summary>
    public Answer Run(params string[] args) => RunText(["--json", .. args]);

    /// <summary>The command as a person sees it in a terminal.</summary>
    public Answer RunText(params string[] args)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = LabopsCommandLine.Run(["projects", "--root", Root, .. args], stdout, stderr, Root);
        return new Answer(exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Runs a command that must succeed and returns its JSON.</summary>
    public JsonObject Ok(params string[] args)
    {
        var answer = Run(args);
        var json = answer.Json;
        (answer.ExitCode == 0 && json["ok"]?.GetValue<bool>() == true)
            .ShouldBeTrue($"labops projects {string.Join(' ', args)} failed: {answer.Stdout}{answer.Stderr}");
        return json;
    }

    /// <summary>Runs a command that must fail and returns its JSON.</summary>
    public JsonObject Fails(params string[] args)
    {
        var answer = Run(args);
        var json = answer.Json;
        (answer.ExitCode != 0 && json["ok"]?.GetValue<bool>() == false)
            .ShouldBeTrue($"labops projects {string.Join(' ', args)} should have failed: {answer.Stdout}");
        return json;
    }

    /// <summary>(level, message) from `check` (or `check --staged`), whether or not it passed.</summary>
    public List<(string Level, string Message)> Problems(params string[] args) =>
        [.. Run(["check", .. args]).Json["problems"]!.AsArray()
            .Select(p => (p!["level"]!.GetValue<string>(), p["message"]!.GetValue<string>()))];

    // -- labs, projects and experiments ---------------------------------------------------------

    public string NewLab(string lab = "Test-Lab")
    {
        Ok("new-lab", lab, "--title", "Test collaboration", "--pi", "Pat Example, Ph.D.", "--institution", "Example University");
        return Path.Combine(Root, "projects", lab);
    }

    public string NewProject(string project = "Test-Project", string lab = "Test-Lab", params string[] extra)
    {
        if (!File.Exists(Path.Combine(Root, "projects", lab, "lab.yaml")))
        {
            NewLab(lab);
        }

        Ok(["new-project", lab, project, "--title", "Test project", .. extra]);
        return Path.Combine(Root, "projects", lab, project);
    }

    public string NewExperiment(string name = "2026-10-Test-DIA", string project = "Test-Project", params string[] extra)
    {
        var folder = Directory.EnumerateDirectories(Path.Combine(Root, "projects"))
            .Select(lab => Path.Combine(lab, project))
            .FirstOrDefault(p => File.Exists(Path.Combine(p, "project.yaml")))
            ?? NewProject(project);
        Ok(["new-experiment", project, name, "--title", "Test experiment", .. extra]);
        return Path.Combine(folder, name);
    }

    /// <summary>The folder's own record: experiment.yaml, project.yaml or lab.yaml.</summary>
    public static string Record(string folder) =>
        ((string[])["experiment.yaml", "project.yaml", "lab.yaml"]).Select(n => Path.Combine(folder, n)).First(File.Exists);

    public static string Raw(string folder) => File.ReadAllText(Record(folder), Encoding.UTF8);

    public static void WriteRaw(string folder, string text) => File.WriteAllText(Record(folder), text, new UTF8Encoding(false));

    /// <summary>The record as the engine reads it (YAML 1.1, as PyYAML's safe_load).</summary>
    public static PyDict Yaml(string folder) => (PyDict)YamlLoader.Load(Raw(folder))!;

    /// <summary>The timeline by step id, each step without its id and kind.</summary>
    public static Dictionary<string, PyDict> Steps(string folder)
    {
        var steps = new Dictionary<string, PyDict>();
        foreach (var step in ((List<object?>)Yaml(folder)["steps"]!).Cast<PyDict>())
        {
            var rest = new PyDict();
            foreach (var (key, value) in step)
            {
                if (key is not ("id" or "kind"))
                {
                    rest[key] = value;
                }
            }

            steps[(string)step["id"]!] = rest;
        }

        return steps;
    }

    public static void Samples(string project, IEnumerable<IEnumerable<object?>> rows) =>
        WriteCsv(Path.Combine(project, "metadata", "samples.csv"), rows);

    /// <summary>
    /// A CSV of text, None and booleans as Python's csv.writer writes one (lineterminator "\n"): a
    /// field is quoted when it holds a comma, a quote or a line break, a row of one empty field is
    /// "", None is empty, and True and False are spelled as Python does. An integer comes out as
    /// Python's would, but a float or a date does not (Python writes 1.0 and 2026-10-01), so the
    /// tests give those as text.
    /// </summary>
    public static void WriteCsv(string path, IEnumerable<IEnumerable<object?>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            var fields = row.Select(Field).ToList();
            text.Append(fields is [""] ? "\"\"" : string.Join(',', fields)).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));

        static string Field(object? value)
        {
            var s = value switch
            {
                null => "",
                bool b => b ? "True" : "False",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
            return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
        }
    }

    /// <summary>A layout document as Octopus's serializeLayout writes it (src/utils/layoutIO.ts).</summary>
    public static JsonObject OctopusLayout(IReadOnlyList<string> ids, IReadOnlyDictionary<string, Dictionary<string, string>> metadata,
        IReadOnlyList<string> columns, string idColumn = "Sample_ID", int perPlate = 96, string? appVersion = "1.4.0")
    {
        const int rows = 8, cols = 12;
        var samples = new JsonArray();
        for (var i = 0; i < ids.Count; i++)
        {
            var (plate, pos) = (i / perPlate, i % perPlate);
            var values = new JsonObject();
            foreach (var c in columns)
            {
                values[c] = metadata.TryGetValue(ids[i], out var m) && m.TryGetValue(c, out var v) ? v : "";
            }

            samples.Add((JsonNode)new JsonObject
            {
                ["id"] = ids[i],
                ["plate"] = plate + 1,
                ["well"] = $"{(char)('A' + pos / cols)}{pos % cols + 1:00}",
                ["metadata"] = values,
            });
        }

        var layout = new JsonObject { ["format"] = "octopus-layout", ["schemaVersion"] = 1 };
        if (appVersion is not null)
        {
            layout["appVersion"] = appVersion;
        }

        layout["plateCount"] = (ids.Count - 1) / perPlate + 1;
        layout["settings"] = new JsonObject
        {
            ["idColumn"] = idColumn,
            ["covariates"] = new JsonArray(columns.Take(1).Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
            ["qcColumn"] = "QC",
            ["qcValues"] = new JsonArray((JsonNode)"TRUE"),
            ["algorithm"] = "balanced",
            ["keepEmptyInLastPlate"] = true,
            ["plateRows"] = rows,
            ["plateColumns"] = cols,
            ["subjectColumn"] = "",
            ["groupingConstraint"] = "none",
            ["metadataColumns"] = new JsonArray(columns.Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
            ["naPolicy"] = new JsonObject { ["foldBlank"] = false, ["foldSpellings"] = new JsonArray() },
        };
        layout["covariateColors"] = new JsonObject
        {
            ["T"] = new JsonObject { ["color"] = "#1f77b4", ["fill"] = "solid" },
            ["C"] = new JsonObject { ["color"] = "#ff7f0e", ["fill"] = "solid" },
        };
        layout["samples"] = samples;
        return layout;
    }

    // -- git ------------------------------------------------------------------------------------

    public sealed record GitResult(int ExitCode, string Stdout, string Stderr);

    /// <summary>git in the repository; fails the test when git fails, unless `check` is false.</summary>
    public GitResult Git(params string[] args) => Git(true, args);

    public GitResult Git(bool check, params string[] args) => RunGit(Root, check, args);

    /// <summary>git in <paramref name="folder"/>: the repository, or a worktree of it.</summary>
    public GitResult RunGit(string folder, bool check, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }

        // Only this repository's own config, never the developer's: a global commit.gpgsign would
        // fail every commit here (or wait for a passphrase), and a global core.hooksPath would run
        // that person's hooks, which --no-verify does not stop for every hook. With no terminal, a
        // prompt fails at once instead of hanging the test.
        start.Environment["GIT_CONFIG_GLOBAL"] = _emptyGitConfig;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var result = new GitResult(process.ExitCode, stdout, stderr.Result);
        if (check)
        {
            result.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {result.Stderr}");
        }

        return result;
    }

    public void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "--no-verify", "--allow-empty", "-m", message);
    }
}
