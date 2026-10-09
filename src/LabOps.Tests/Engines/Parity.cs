using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using LabOps.Engines;
using LabOps.Engines.Projects;

namespace LabOps.Tests.Engines;

/// <summary>
/// Runs LabOps-Projects' Python engine (scripts/project.py, from the clone named by
/// LAB_PROJECTS_REPO, in that clone's uv environment) and the C# engine on the same repository,
/// and compares what they answer. The Python engine finds its repository from where its script
/// is, so a test repository gets a copy of the script.
/// </summary>
internal sealed class Parity
{
    private Parity(string clone, string uv)
    {
        Clone = clone;
        Uv = uv;
    }

    /// <summary>The LabOps-Projects clone whose engine and environment are the reference.</summary>
    public string Clone { get; }

    private string Uv { get; }

    /// <summary>Null (and the test skips) when no clone or no uv is set up on this computer.</summary>
    public static Parity? TryCreate()
    {
        var clone = Environment.GetEnvironmentVariable("LAB_PROJECTS_REPO");
        if (string.IsNullOrEmpty(clone) || !File.Exists(Path.Combine(clone, "scripts", "project.py")))
        {
            return null;
        }

        var uv = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, OperatingSystem.IsWindows() ? "uv.exe" : "uv")).FirstOrDefault(File.Exists);
        return uv is null ? null : new Parity(Path.GetFullPath(clone), uv);
    }

    public sealed record PythonAnswer(int ExitCode, string Stdout, string Stderr)
    {
        public JsonObject Json => JsonNode.Parse(Stdout)!.AsObject();

        // Without the final line end only: an empty last row (a sheet with no headers) is a line too.
        public IReadOnlyList<string> Lines => Stdout.Replace("\r\n", "\n", StringComparison.Ordinal) is { Length: > 0 } t
            ? (t.EndsWith('\n') ? t[..^1] : t).Split('\n')
            : [];
    }

    /// <summary>`python scripts/project.py args` in `root`, whose scripts/project.py is used.</summary>
    public PythonAnswer Python(string root, params string[] args) => Run(root, Path.Combine(root, "scripts", "project.py"), args);

    /// <summary>LabOps' tools/engine-golden/&lt;script&gt; in the clone's environment (it has openpyxl).</summary>
    public PythonAnswer Tool(string script, params string[] args)
    {
        var labops = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(labops, "LabOps.sln")))
        {
            labops = Path.GetDirectoryName(labops)!;
        }

        return Run(labops, Path.Combine(labops, "tools", "engine-golden", script), args);
    }

    /// <summary>git in `root`, failing the test when it fails.</summary>
    public static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {error}");
    }

    private PythonAnswer Run(string root, string script, string[] args)
    {
        var start = new ProcessStartInfo(Uv)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in (string[])["run", "--frozen", "--quiet", "--project", Clone, "python", script, .. args])
        {
            start.ArgumentList.Add(a);
        }

        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return new PythonAnswer(process.ExitCode, InOurWords(stdout), InOurWords(stderr.Result));
    }

    /// <summary>
    /// project.py's text in the C# engine's words, found by the words around them, so the rest is
    /// still compared exactly. The example names in project.py's messages are real collaborators;
    /// the C# engine's are made up, since LabOps is public. And the commands a message or a record
    /// comment suggests are labops ones, since project.py is gone once LabOps-Projects moves over.
    /// </summary>
    public static string InOurWords(string text)
    {
        foreach (var (pattern, ours) in Ours)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, ours);
        }

        return text;
    }

    private static readonly (string Pattern, string Ours)[] Ours =
    [
        (@"\(project\.py (?=sheet\)|link <)", "(labops projects "),
        (@"recorded; project\.py link", "recorded; labops projects link"),
        (@"generated by scripts/project\.py index", "generated by labops projects index"),
        (@"(?<=should be letters, digits and hyphens \(like )[A-Za-z0-9-]+(?=\))", "ClearwaterZoo-Cole"),
        (@"(?<=a lab is letters, digits and hyphens, like )[A-Za-z0-9-]+(?= or UW-MacCoss)", "ClearwaterZoo-Cole"),
        (@"(?<=a letter and be letters, digits and hyphens \(like )[A-Za-z0-9-]+(?=\))", "MNRF-BioTRACK"),
        (@"(?<=starting with a letter, like )[A-Za-z0-9-]+", "MNRF-BioTRACK"),
        (@"(?<=named YYYY-MM-Topic, for example )[A-Za-z0-9-]+", "2026-09-BioTRACK-DIA"),
        (@"(?<=\{folder: /MacCoss/Collaborations/)[^,}]+(?=, page: default\})", "MNRF/BioTRACK"),
    ];

    /// <summary>A copy of the clone's engine, templates and config in `root`, which then works as a repository.</summary>
    public void Seed(string root)
    {
        foreach (var folder in (string[])["scripts", "templates", "config"])
        {
            CopyFolder(Path.Combine(Clone, folder), Path.Combine(root, folder));
        }

        File.WriteAllText(Path.Combine(root, "README.md"), "# Test repository\n\n<!-- INDEX:START -->\n<!-- INDEX:END -->\n");
    }

    public static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (file.Contains("__pycache__", StringComparison.Ordinal))
            {
                continue;
            }

            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static readonly HashSet<string> Added = ["planned_start", "planned_finish", "instruments"];

    /// <summary>The first place two JSON values differ, or null when they are the same.</summary>
    public static string? Difference(JsonNode? python, JsonNode? csharp, string at = "$")
    {
        switch (python, csharp)
        {
            case (null, null):
                return null;
            case (JsonObject p, JsonObject c):
                var pk = p.Select(x => x.Key).ToList();
                // Keys the C# engine added after project.py was retired, which project.py never
                // printed: a step's plan, the lab's instruments. Compared only when they hold something.
                var ck = c.Where(x => !(x.Value is null or JsonArray { Count: 0 } && Added.Contains(x.Key) && !p.ContainsKey(x.Key)))
                    .Select(x => x.Key).ToList();
                if (!pk.SequenceEqual(ck))
                {
                    return $"{at}: keys python [{string.Join(", ", pk)}] c# [{string.Join(", ", ck)}]";
                }

                foreach (var key in pk)
                {
                    if (Difference(p[key], c[key], $"{at}.{key}") is { } d)
                    {
                        return d;
                    }
                }

                return null;
            case (JsonArray p, JsonArray c):
                for (var i = 0; i < Math.Max(p.Count, c.Count); i++)
                {
                    if (i >= p.Count || i >= c.Count)
                    {
                        return $"{at}: python has {p.Count} items, c# {c.Count}; first extra: {(i < p.Count ? p[i] : c[i])?.ToJsonString()}";
                    }

                    if (Difference(p[i], c[i], $"{at}[{i}]") is { } d)
                    {
                        return d;
                    }
                }

                return null;
            default:
                return JsonNode.DeepEquals(python, csharp) ? null : $"{at}: python {python?.ToJsonString()} c# {csharp?.ToJsonString()}";
        }
    }

    /// <summary>Every file under `root` (but the engine's own scripts, git's and Python's caches) and its bytes.</summary>
    public static SortedDictionary<string, byte[]> Files(string root)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("scripts/", StringComparison.Ordinal) || rel.StartsWith(".git/", StringComparison.Ordinal)
                || rel.Contains("__pycache__", StringComparison.Ordinal))
            {
                continue;
            }

            files[rel] = File.ReadAllBytes(file);
        }

        return files;
    }

    /// <summary>The first file that differs between two repositories, or null.</summary>
    public static string? FileDifference(string python, string csharp)
    {
        var p = Files(python);
        var c = Files(csharp);
        foreach (var name in p.Keys.Union(c.Keys).Order(StringComparer.Ordinal))
        {
            if (!p.TryGetValue(name, out var pb))
            {
                return $"only c# wrote {name}";
            }

            if (!c.TryGetValue(name, out var cb))
            {
                return $"only python wrote {name}";
            }

            if (!pb.AsSpan().SequenceEqual(cb) && Text(pb) is var po && Text(cb) is var co && po != co)
            {
                return $"{name} differs:\n--- python\n{Encoding.UTF8.GetString(pb)}\n--- c#\n{Encoding.UTF8.GetString(cb)}";
            }
        }

        return null;

        // A text file in our words; any other file as it is (Latin-1 keeps every byte).
        static string Text(byte[] bytes)
        {
            try
            {
                return InOurWords(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }

    /// <summary>The same JSON with a repository's own path written as &lt;root&gt;, so twins can be compared.</summary>
    public static JsonNode? WithoutRoot(JsonNode? node, string root)
    {
        var text = node?.ToJsonString() ?? "null";
        var escaped = System.Text.Json.JsonSerializer.Serialize(root)[1..^1];
        return JsonNode.Parse(text.Replace(escaped, "<root>", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A command that writes: run with --json on `python`, as text on `text`, and in C# on `csharp`
    /// (three copies of one repository), then compare the answers and every file.
    /// </summary>
    public void CompareWriteIn(string python, string text, string csharp, List<string> differences, string[] args, Func<ProjectsEngine, string, CommandResult> run)
    {
        // {root} in an argument is each copy's own folder, for a file a command reads.
        string[] For(string root) => [.. args.Select(a => a.Replace("{root}", root, StringComparison.Ordinal))];
        var label = string.Join(' ', args);
        var engine = new ProjectsEngine(new ProjectRepository(csharp));
        CommandResult? result = null;
        string? error = null;
        try
        {
            result = run(engine, csharp);
        }
        catch (EngineError ex)
        {
            error = ex.Message;
        }

        var json = Python(python, ["--json", .. For(python)]);
        var plain = Python(text, For(text));
        var answer = json.Stdout.Length > 0 ? JsonNode.Parse(json.Stdout)!.AsObject() : null;
        if (answer?["error"] is { } pythonError)
        {
            // A path can appear as written and as Python's repr, with its backslashes doubled.
            static string? Rooted(string? message, string root) => message
                ?.Replace(root.Replace("\\", "\\\\", StringComparison.Ordinal), "<root>", StringComparison.OrdinalIgnoreCase)
                .Replace(root, "<root>", StringComparison.OrdinalIgnoreCase);
            var expected = Rooted(pythonError.GetValue<string>(), python);
            if (Rooted(error, csharp) != expected)
            {
                differences.Add($"{label}: error\n  python: {expected}\n  c#:     {error ?? "(none)"}");
            }
        }
        else if (answer is null)
        {
            differences.Add($"{label}: python failed ({json.ExitCode}): {json.Stderr.Trim()}");
        }
        else if (error is not null)
        {
            differences.Add($"{label}: c# refused: {error}");
        }
        else
        {
            if (Difference(WithoutRoot(answer, python), WithoutRoot(result!.Payload, csharp)) is { } d)
            {
                differences.Add($"{label}: json {d}");
            }

            var lines = result.Lines.Count == 0 ? [] : string.Join('\n', result.Lines).Split('\n');
            if (LineDifference(plain.Lines.Select(l => l.Replace(text, "<root>", StringComparison.OrdinalIgnoreCase)).ToList(),
                    lines.Select(l => l.Replace(csharp, "<root>", StringComparison.OrdinalIgnoreCase)).ToList()) is { } l)
            {
                differences.Add($"{label}: text {l}");
            }
        }

        if (FileDifference(python, csharp) is { } f)
        {
            differences.Add($"{label}: files {f}");
        }

        if (FileDifference(python, text) is { } t)
        {
            differences.Add($"{label}: the json and text runs of python wrote different files: {t}");
        }
    }

    /// <summary>The first differing line, or null.</summary>
    public static string? LineDifference(IReadOnlyList<string> python, IReadOnlyList<string> csharp)
    {
        for (var i = 0; i < Math.Max(python.Count, csharp.Count); i++)
        {
            var p = i < python.Count ? python[i] : "<none>";
            var c = i < csharp.Count ? csharp[i] : "<none>";
            if (p != c)
            {
                return $"line {i + 1}:\n  python: {p}\n  c#:     {c}";
            }
        }

        return null;
    }

    /// <summary>Runs one command both ways and records any difference in JSON, text or exit code.</summary>
    public void Compare(string root, List<string> differences, string[] args, Func<ProjectsEngine, CommandResult> run, params string[] ignoreKeys)
    {
        var engine = new ProjectsEngine(new ProjectRepository(root) { Today = () => new DateOnly(2026, 10, 8) });
        var label = string.Join(' ', args);
        CommandResult? result = null;
        string? error = null;
        try
        {
            result = run(engine);
        }
        catch (EngineError ex)
        {
            error = ex.Message;
        }

        var json = Python(root, ["--json", .. args]);
        if (error is not null)
        {
            var pythonError = json.Stdout.Length > 0 ? JsonNode.Parse(json.Stdout)?["error"]?.GetValue<string>() : json.Stderr;
            if (pythonError != error)
            {
                differences.Add($"{label}: error\n  python: {pythonError}\n  c#:     {error}");
            }

            return;
        }

        if (json.Stdout.Length == 0)
        {
            differences.Add($"{label}: python failed ({json.ExitCode}): {json.Stderr.Trim()}");
            return;
        }

        var pythonJson = json.Json;
        if (pythonJson["error"] is { } pythonFailed)
        {
            differences.Add($"{label}: python stopped ({json.ExitCode}): {pythonFailed}");
            return;
        }

        foreach (var key in ignoreKeys)
        {
            pythonJson.Remove(key);
            result!.Payload.Remove(key);
        }

        if (Difference(pythonJson, result!.Payload) is { } d)
        {
            differences.Add($"{label}: json {d}");
        }

        if (json.ExitCode != result.ExitCode)
        {
            differences.Add($"{label}: exit code python {json.ExitCode} c# {result.ExitCode}");
        }

        // A CSV cell can hold a line break, so compare the text, not the list of rows.
        var text = Python(root, args);
        var csharpLines = result.Lines.Count == 0 ? [] : string.Join('\n', result.Lines).Split('\n');
        if (LineDifference(text.Lines, csharpLines) is { } l)
        {
            differences.Add($"{label}: text {l}");
        }
    }
}
