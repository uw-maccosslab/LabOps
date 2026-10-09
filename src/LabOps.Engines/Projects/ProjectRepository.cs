using LabOps.Engines.Python;

namespace LabOps.Engines.Projects;

/// <summary>
/// A clone of LabOps-Projects: where its records are, and how they are found.
///   projects/&lt;Lab&gt;/lab.yaml                         a lab we work with (or the MacCoss lab itself)
///   projects/&lt;Lab&gt;/&lt;Project&gt;/project.yaml           a body of work with one set of samples
///   projects/&lt;Lab&gt;/&lt;Project&gt;/&lt;YYYY-MM-Topic&gt;/experiment.yaml   one measurement and analysis
///   inbox/&lt;Project&gt;/                                the collaborator's originals, never committed
/// Project and experiment names are unique across the repository, so commands take a name.
/// </summary>
public sealed class ProjectRepository
{
    private static readonly TimeZoneInfo LabTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public ProjectRepository(string root)
    {
        Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Projects = Path.Combine(Root, "projects");
        Templates = Path.Combine(Root, "templates");
        Config = Path.Combine(Root, "config");
        Inbox = Path.Combine(Root, "inbox");
    }

    public string Root { get; }

    public string Projects { get; }

    public string Templates { get; }

    public string Config { get; }

    public string Inbox { get; }

    /// <summary>Today in Seattle, where the lab is (the date a step is recorded on).</summary>
    public Func<DateOnly> Today { get; init; } = LabToday;

    public static DateOnly LabToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LabTimeZone).DateTime);

    /// <summary>A path relative to the repository, with forward slashes (projects/UW-MacCoss/MNRF-BioTRACK).</summary>
    public string Rel(string path)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Root, full);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public bool IsUnder(string path, string folder)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> LabFiles() => Records(Projects, 1, "lab.yaml");

    public IReadOnlyList<string> ProjectFiles() => Records(Projects, 2, "project.yaml");

    public IReadOnlyList<string> ExperimentFiles() => Records(Projects, 3, "experiment.yaml");

    /// <summary>`folder/*/.../name` at a depth, sorted as Python sorts paths.</summary>
    public static IReadOnlyList<string> Records(string folder, int depth, string name)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        IEnumerable<string> dirs = [folder];
        for (var i = 0; i < depth; i++)
        {
            dirs = dirs.SelectMany(d => Directory.EnumerateDirectories(d));
        }

        return [.. dirs.Select(d => Path.Combine(d, name)).Where(File.Exists).Order(PathOrder.Instance)];
    }

    /// <summary>("project" or "experiment", its folder), from a name or a path; `want` limits it to one.</summary>
    public (string Kind, string Folder) FindItem(string reference, string? want = null)
    {
        foreach (var kind in (string[])["project", "experiment"])
        {
            if ((want is null || want == kind) && File.Exists(Path.Combine(reference, kind + ".yaml")))
            {
                var full = Path.GetFullPath(reference).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!IsUnder(full, Projects))
                {
                    throw new EngineError($"{reference} is not in this repository's projects/ folder");
                }

                return (kind, full);
            }
        }

        var matches = new List<(string Kind, string Folder)>();
        if (want is null or "project")
        {
            matches.AddRange(ProjectFiles().Select(Path.GetDirectoryName).Where(f => Path.GetFileName(f) == reference).Select(f => ("project", f!)));
        }

        if (want is null or "experiment")
        {
            matches.AddRange(ExperimentFiles().Select(Path.GetDirectoryName).Where(f => Path.GetFileName(f) == reference).Select(f => ("experiment", f!)));
        }

        var what = want ?? "project or experiment";
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new EngineError($"more than one {what} is named {reference}: " + string.Join(", ", matches.Select(m => Rel(m.Folder))));
        }

        throw new EngineError($"no {what} folder or name matches {PyText.ReprString(reference)}");
    }

    public string FindProject(string reference) => FindItem(reference, "project").Folder;

    /// <summary>config/people.yaml's people: login, name and role (null when missing).</summary>
    public IReadOnlyList<(string Login, string? Name, string? Role)> PeopleList()
    {
        var path = Path.Combine(Config, "people.yaml");
        if (!File.Exists(path))
        {
            return [];
        }

        var doc = Yaml.YamlLoader.Load(Yaml.YamlText.ReadText(path, "config/people.yaml")) as PyDict ?? [];
        var entries = doc["people"] is List<object?> list && PyText.Truthy(list) ? list : [];
        return [.. entries.OfType<PyDict>().Where(p => PyText.Truthy(p["login"]))
            .Select(p => (Values.Text(p["login"])!, Values.Text(p["name"]), Values.Text(p["role"])))];
    }

    /// <summary>The logins in config/people.yaml, lowercase.</summary>
    public HashSet<string> People() => [.. PeopleList().Select(p => p.Login.ToLowerInvariant())];
}

/// <summary>
/// How Python sorts paths on Windows: part by part, ignoring case (projects/a/b before projects/a-c,
/// since "a" sorts before "a-c"). Ties fall back to the exact text so the order never varies.
/// </summary>
public sealed class PathOrder : IComparer<string>
{
    public static readonly PathOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        var a = (x ?? "").ToLowerInvariant().Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var b = (y ?? "").ToLowerInvariant().Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var c = string.CompareOrdinal(a[i], b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        var byLength = a.Length.CompareTo(b.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }
}
