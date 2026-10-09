using System.Text.RegularExpressions;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>
/// The structure rules for labs, projects, experiments, their timelines, the sample table and the
/// wiki page's written parts. Messages are project.py's word for word: the app and Claude show them.
/// </summary>
public static partial class Validation
{
    public static readonly IReadOnlyList<string> RecordStatuses = ["active", "closed", "on_hold"];   // sorted
    public static readonly IReadOnlyList<string> FundingTypes = ["grant", "internal", "quote"];      // sorted
    public static readonly IReadOnlyList<string> PanoramaKinds = ["qc", "raw", "results"];
    public static readonly IReadOnlyList<string> RequiredSampleColumns = ["Sample_ID", "QC"];
    public const string WikiFile = "wiki.yaml";
    public static readonly IReadOnlyList<string> WikiKeys = ["folders", "plan", "qc_card", "samples", "samples_card", "summary"]; // sorted

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*$")]
    public static partial Regex LabPattern();

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])-[A-Za-z0-9][A-Za-z0-9-]*$")]
    public static partial Regex ExperimentPattern();

    // A project starts with a letter, so its name can never look like an experiment's.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9-]*$")]
    public static partial Regex ProjectPattern();

    // A protocol's ID in LabOps-Protocols: lowercase words joined by hyphens.
    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    public static partial Regex ProtocolIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _.-]*$")]
    public static partial Regex WikiPagePattern();

    [GeneratedRegex(@"^https?://")]
    private static partial Regex WebAddress();

    public static bool In(object? value, IEnumerable<string> set) => value is string s && set.Contains(s);

    public static List<(string, string)> ValidateSteps(PyDict e, IReadOnlySet<string> knownPeople)
    {
        var output = new List<(string, string)>();
        if (e["stages"] is PyDict)
        {
            output.Add(("ERROR", "`stages:` is the older form of the timeline; write it as `steps:` (see the templates)"));
        }

        var steps = Steps.Read(e);
        if (steps.Count == 0)
        {
            output.Add(("ERROR", "there are no steps"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? reached = null;
        foreach (var s in steps)
        {
            var sid = PyText.Truthy(s["id"]) ? PyText.Str(s["id"]) : "";
            var name = $"step {(sid.Length > 0 ? sid : "(no id)")}";
            if (Steps.IsInvalid(s))
            {
                output.Add(("ERROR", "each step is written like {id: sample_prep, kind: sample_prep, status: done, finished: 2026-10-02}"));
                continue;
            }

            if (!Steps.StepIdPattern().IsMatch(sid))
            {
                output.Add(("ERROR", $"{name}: an id is lowercase letters, digits, - and _"));
            }
            else if (seen.Contains(sid))
            {
                output.Add(("ERROR", $"{name}: the id is used more than once"));
            }

            seen.Add(sid);
            var kind = s["kind"];
            if (!Steps.IsKind(kind))
            {
                output.Add(("ERROR", $"{name}: kind must be one of {string.Join(", ", Steps.Kinds.Select(k => k.Key))}"));
            }
            else if (Equals(kind, "other") && !PyText.Truthy(s["label"]))
            {
                output.Add(("ERROR", $"{name}: a step of kind other needs a label"));
            }

            var status = Steps.Status(s);
            if (!Steps.Statuses.Contains(status))
            {
                output.Add(("ERROR", $"{name}: status must be one of {string.Join(", ", Steps.Statuses.Order(StringComparer.Ordinal))}"));
                continue;
            }

            var started = s["started"];
            var finished = s["finished"];
            foreach (var (label, d) in (ReadOnlySpan<(string, object?)>)[("started", started), ("finished", finished)])
            {
                if (d is not null && !IsDate(d))
                {
                    output.Add(("ERROR", $"{name}: {label} must be a date (YYYY-MM-DD)"));
                }
            }

            if (IsDate(started) && IsDate(finished) && Moment(finished!) < Moment(started!))
            {
                output.Add(("ERROR", $"{name}: finished before it started"));
            }

            if (status == "done" && !PyText.Truthy(finished))
            {
                output.Add(("WARN", $"{name} is done but has no finished date"));
            }

            // The plan: when the step should start and finish. Nothing here depends on today, so
            // a record checks the same on any day; being late is for the views to show.
            var plannedStart = s["planned_start"];
            var plannedFinish = s["planned_finish"];
            foreach (var (label, d) in (ReadOnlySpan<(string, object?)>)[("planned_start", plannedStart), ("planned_finish", plannedFinish)])
            {
                if (d is not null && d is not DateOnly)
                {
                    output.Add(("ERROR", $"{name}: {label} must be a date (YYYY-MM-DD)"));
                }
            }

            if (plannedStart is DateOnly plannedFrom && plannedFinish is DateOnly plannedTo && plannedTo < plannedFrom)
            {
                output.Add(("ERROR", $"{name}: planned to finish before it starts"));
            }

            var who = new List<string>();
            foreach (var k in (string[])["assigned", "by"])
            {
                if (PyText.Truthy(s[k]) && !who.Contains(PyText.Str(s[k])))
                {
                    who.Add(PyText.Str(s[k]));
                }
            }

            foreach (var w in who)
            {
                if (knownPeople.Count > 0 && !knownPeople.Contains(w.ToLowerInvariant()))
                {
                    output.Add(("WARN", $"{name}: {w} is not in config/people.yaml"));
                }
            }

            if (status is "in_progress" or "done" && !string.IsNullOrEmpty(reached))
            {
                output.Add(("WARN", $"{name} is {status.Replace('_', ' ')} while {reached} is still pending"));
            }

            if (status == "pending" && reached is null)
            {
                reached = sid;
            }
        }

        return output;
    }

    public static bool IsDate(object? v) => v is DateOnly or PyDateTime;

    // project.py compared a date with a datetime and stopped with a TypeError; compare the moments.
    private static DateTime Moment(object d) => d is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : ((PyDateTime)d).Value;

    private static void Common(PyDict d, string file, string key, string name, List<(string, string)> output)
    {
        if (!PyText.Eq(d[key], name))
        {
            output.Add(("ERROR", $"{file} says {key} {PyText.Repr(d[key])} but the folder is {PyText.ReprString(name)}"));
        }

        foreach (var k in (string[])["title", "status"])
        {
            if (!PyText.Truthy(d[k]))
            {
                output.Add(("ERROR", $"{file} is missing `{k}`"));
            }
        }

        if (PyText.Truthy(d["status"]) && !In(d["status"], RecordStatuses))
        {
            output.Add(("ERROR", $"status must be one of {string.Join(", ", RecordStatuses)}"));
        }
    }

    private static void Funding(object? funding, List<(string, string)> output, bool optional)
    {
        var f = funding as PyDict ?? [];
        var kind = f["type"];
        if (kind is null && optional)
        {
            return;
        }

        if (!In(kind, FundingTypes))
        {
            output.Add(("ERROR", $"funding.type must be one of {string.Join(", ", FundingTypes)}"
                                 + (optional ? " (or null to use the project's)" : "")));
        }
        else if (Equals(kind, "quote") && !PyText.Truthy(f["quotes"]))
        {
            output.Add(("WARN", "funding.type is quote but no quote number is listed"));
        }
        else if (Equals(kind, "grant") && !PyText.Truthy(f["grant"]))
        {
            output.Add(("WARN", "funding.type is grant but no grant is named"));
        }
    }

    private static void Links(PyDict d, List<(string, string)> output, bool panorama = false)
    {
        foreach (var n in Values.Links(d["notebooks"], Values.NotebookFields))
        {
            if (n["id"] is null && n["url"] is null)
            {
                output.Add(("WARN", "a notebook has neither an id nor a url"));
            }
            else if (n["url"] is { } url && !WebAddress().IsMatch(url))
            {
                output.Add(("WARN", $"notebook {n["id"] ?? n["url"]}: the url should start with https://"));
            }
        }

        if (!panorama)
        {
            return;
        }

        foreach (var f in Values.Links(d["panorama"], Values.PanoramaFields))
        {
            if (f["folder"] is null)
            {
                output.Add(("WARN", "a Panorama entry has no folder"));
            }
            else if (!In(f["kind"], PanoramaKinds))
            {
                output.Add(("WARN", $"Panorama folder {f["folder"]}: kind should be raw, results or qc"));
            }
        }
    }

    private static void Protocols(PyDict d, List<(string, string)> output)
    {
        var steps = Steps.Read(d).Where(s => !Steps.IsInvalid(s)).Select(s => PyText.Str(s["id"])).ToHashSet(StringComparer.Ordinal);
        foreach (var p in Values.Protocols(d))
        {
            if (p.Id is null || !ProtocolIdPattern().IsMatch(p.Id))
            {
                output.Add(("WARN", $"protocol {PyText.Repr(p.Id)} is not a LabOps-Protocols ID (lowercase words joined by hyphens)"));
            }
            else if (p.Version is null || PyText.ToBig(p.Version) < 1)
            {
                output.Add(("WARN", $"protocol {p.Id}: give the version that was used (version: 1, 2, ...)"));
            }

            if (p.Step is not null && !steps.Contains(p.Step))
            {
                output.Add(("WARN", $"protocol {PyText.Str(p.Id)}: there is no step {PyText.ReprString(p.Step)}"));
            }
        }
    }

    /// <summary>lab.yaml's own rules: what the file says, and its folder's name.</summary>
    public static List<(string, string)> LabRules(PyDict lab, string name)
    {
        var output = new List<(string, string)>();
        if (!LabPattern().IsMatch(name))
        {
            output.Add(("ERROR", $"lab folder {PyText.ReprString(name)} should be letters, digits and hyphens (like ClearwaterZoo-Cole)"));
        }

        Common(lab, "lab.yaml", "lab", name, output);
        Links(lab, output);
        return output;
    }

    /// <summary>project.yaml's own rules: what the file says, and its folder's name.</summary>
    public static List<(string, string)> ProjectRules(PyDict p, string name, IReadOnlySet<string> knownPeople)
    {
        var output = new List<(string, string)>();
        if (!ProjectPattern().IsMatch(name))
        {
            output.Add(("ERROR", $"project folder {PyText.ReprString(name)} should start with a letter and be letters, digits "
                                 + "and hyphens (like MNRF-BioTRACK)"));
        }

        Common(p, "project.yaml", "project", name, output);
        Funding(p["funding"], output, optional: false);
        Links(p, output);
        Protocols(p, output);
        output.AddRange(Reviews.Rules(p));
        var wiki = p["wiki"];
        if (wiki is not null)
        {
            var target = wiki is PyDict w ? Values.Text(w["folder"]) : null;
            var page = wiki is PyDict w2 ? Values.Text(w2["page"]) ?? "default" : "";
            if (target is null || !target.StartsWith('/') || target.Contains("@files", StringComparison.Ordinal))
            {
                output.Add(("ERROR", "wiki: needs the project's Panorama folder, for example "
                                     + "{folder: /MacCoss/Collaborations/MNRF/BioTRACK, page: default}"));
            }
            else if (!WikiPagePattern().IsMatch(page))
            {
                output.Add(("ERROR", $"wiki: page {PyText.ReprString(page)} should be letters, digits, spaces, hyphens, dots or underscores"));
            }
        }

        output.AddRange(ValidateSteps(p, knownPeople));
        return output;
    }

    /// <summary>experiment.yaml's own rules: what the file says, and its folder's name.</summary>
    public static List<(string, string)> ExperimentRules(PyDict e, string name, IReadOnlySet<string> knownPeople)
    {
        var output = new List<(string, string)>();
        if (!ExperimentPattern().IsMatch(name))
        {
            output.Add(("WARN", $"experiment folder {PyText.ReprString(name)} should be named YYYY-MM-Topic"));
        }

        Common(e, "experiment.yaml", "experiment", name, output);
        Funding(e["funding"], output, optional: true);
        Links(e, output, panorama: true);
        Protocols(e, output);
        output.AddRange(Reviews.Rules(e));
        if (e["wiki"] is not null)
        {
            output.Add(("WARN", "the wiki page belongs to the project, not the experiment"));
        }

        output.AddRange(ValidateSteps(e, knownPeople));
        return output;
    }

    public static List<(string, string)> ValidateLab(ProjectRepository repo, string path, PyDict lab) =>
        LabRules(lab, Path.GetFileName(Path.GetDirectoryName(path))!);

    public static List<(string, string)> ValidateProject(ProjectRepository repo, string path, IReadOnlySet<string> knownPeople, PyDict p)
    {
        var folder = Path.GetDirectoryName(path)!;
        var output = ProjectRules(p, Path.GetFileName(folder), knownPeople);
        var lab = Path.GetDirectoryName(folder)!;
        if (!File.Exists(Path.Combine(lab, "lab.yaml")))
        {
            output.Add(("ERROR", $"{repo.Rel(lab)} has no lab.yaml"));
        }

        var samples = Path.Combine(folder, "metadata", "samples.csv");
        if (File.Exists(samples))
        {
            output.AddRange(ValidateSamples(repo, samples));
        }

        var wiki = Path.Combine(folder, WikiFile);
        if (File.Exists(wiki))
        {
            output.AddRange(ValidateWiki(repo, wiki));
        }

        return output;
    }

    public static List<(string, string)> ValidateExperiment(ProjectRepository repo, string path, IReadOnlySet<string> knownPeople, PyDict e)
    {
        var folder = Path.GetDirectoryName(path)!;
        var output = ExperimentRules(e, Path.GetFileName(folder), knownPeople);
        var instruments = repo.Instruments();
        if (instruments.Count > 0 && Values.Text(e["instrument"]) is { } instrument
            && !instruments.Contains(instrument, StringComparer.OrdinalIgnoreCase))
        {
            output.Add(("WARN", $"instrument {PyText.ReprString(instrument)} is not in config/instruments.yaml "
                                + $"({string.Join(", ", instruments)}); use one of those names, or add it there"));
        }

        var project = Path.GetDirectoryName(folder)!;
        if (!File.Exists(Path.Combine(project, "project.yaml")))
        {
            output.Add(("ERROR", $"{repo.Rel(project)} has no project.yaml"));
        }

        if (e["wiki"] is null && File.Exists(Path.Combine(folder, WikiFile)))
        {
            output.Add(("WARN", "the wiki page belongs to the project, not the experiment"));
        }

        foreach (var misplaced in (string[])["metadata/samples.csv", "layout/octopus-layout.json"])
        {
            if (File.Exists(Path.Combine(folder, misplaced)))
            {
                output.Add(("ERROR", $"{misplaced} belongs to the project ({repo.Rel(project)}), not the experiment"));
            }
        }

        return output;
    }

    /// <summary>The structure of the organized sample table (its identifiers are checked separately).</summary>
    public static List<(string, string)> ValidateSamples(ProjectRepository repo, string path)
    {
        var output = new List<(string, string)>();
        var rel = repo.Rel(path);
        IReadOnlyList<string> headers;
        IReadOnlyList<IReadOnlyList<object?>> rows;
        try
        {
            var sheets = Sheets.Read(path);
            if (sheets.Count != 1)
            {
                return [("ERROR", $"{rel} could not be read as one sheet")];
            }

            (_, headers, rows) = sheets[0];
        }
        catch (EngineError)
        {
            return [("ERROR", $"{rel} could not be read as one sheet")];
        }

        foreach (var col in RequiredSampleColumns)
        {
            if (!headers.Contains(col))
            {
                output.Add(("ERROR", $"{rel} has no `{col}` column"));
            }
        }

        if (!headers.Contains("Sample_Group"))
        {
            output.Add(("WARN", $"{rel} has no `Sample_Group` column"));
        }

        var idIndex = IndexOf(headers, "Sample_ID");
        if (idIndex >= 0)
        {
            var ids = rows.Select(r => Sheets.CellText(r[idIndex])).ToList();
            if (ids.Any(x => x.Length == 0))
            {
                output.Add(("ERROR", $"{rel} has rows without a Sample_ID"));
            }

            var dupes = ids.Where(x => x.Length > 0).GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => g.Key).Order(StringComparer.Ordinal).ToList();
            if (dupes.Count > 0)
            {
                output.Add(("ERROR", $"{rel} repeats Sample_ID {string.Join(", ", dupes.Take(5))}"));
            }

            // A Sample_ID becomes a raw file name, and Windows does not tell S1 from s1.
            var spellings = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var x in ids.Where(x => x.Length > 0))
            {
                var key = PyText.CaseFold(x);
                if (!spellings.TryGetValue(key, out var set))
                {
                    spellings[key] = set = new SortedSet<string>(StringComparer.Ordinal);
                }

                set.Add(x);
            }

            var cased = spellings.Values.Where(v => v.Count > 1).Select(v => string.Join(" and ", v)).Order(StringComparer.Ordinal).ToList();
            if (cased.Count > 0)
            {
                output.Add(("ERROR", $"{rel} has Sample_IDs that differ only in capitals ({string.Join("; ", cased.Take(5))}); "
                                     + "they would be the same raw file name on Windows"));
            }
        }

        var qcIndex = IndexOf(headers, "QC");
        if (qcIndex >= 0)
        {
            var bad = rows.Select(r => Sheets.CellText(r[qcIndex])).Where(v => v is not ("TRUE" or "FALSE"))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (bad.Count > 0)
            {
                output.Add(("ERROR", $"{rel}: QC must be TRUE or FALSE (found {string.Join(", ", bad)})"));
            }
        }

        return output;
    }

    public static int IndexOf(IReadOnlyList<string> headers, string header)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i] == header)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The shape of wiki.yaml (its identifiers are part of the pre-commit check).</summary>
    public static List<(string, string)> ValidateWiki(ProjectRepository repo, string path)
    {
        object? w;
        try
        {
            w = YamlLoader.Load(YamlText.ReadText(path, repo.Rel(path)));
        }
        catch (YamlProblemException ex)
        {
            return [("ERROR", $"{repo.Rel(path)} is not valid YAML: {ex.Problem} (line {ex.Line})")];
        }
        catch (EngineError ex)
        {
            return [("ERROR", ex.Message)];
        }

        // load_yaml read an empty or false-y document as {}.
        return WikiRules(PyText.Truthy(w) ? w : new PyDict(), repo.Rel(path));
    }

    /// <summary>The shape of the written parts, so that the page shows them as they were meant.</summary>
    public static List<(string, string)> WikiRules(object? doc, string label)
    {
        if (doc is not PyDict w)
        {
            return [("ERROR", $"{label} should be a mapping of the page's written parts")];
        }

        var output = new List<(string, string)>();
        foreach (var key in w.Keys.Select(PyText.Str).Where(k => !WikiKeys.Contains(k)).Distinct().Order(StringComparer.Ordinal))
        {
            output.Add(("WARN", $"{label}: `{key}` is not shown on the page (use {string.Join(", ", WikiKeys)})"));
        }

        foreach (var key in (string[])["summary", "samples_card", "qc_card"])
        {
            if (w[key] is not null and not string)
            {
                output.Add(("ERROR", $"{label}: `{key}` should be text"));
            }
        }

        if (w["plan"] is not null && !(w["plan"] is List<object?> plan && plan.All(x => x is string)))
        {
            output.Add(("ERROR", $"{label}: `plan` should be a list of short lines"));
        }

        var samples = w["samples"];
        if (samples is not null)
        {
            if (samples is not PyDict s)
            {
                output.Add(("ERROR", $"{label}: `samples` should have `intro` and `sections`"));
            }
            else
            {
                var i = 0;
                foreach (var section in PyText.Truthy(s["sections"]) ? Values.Iterate(s["sections"]) : [])
                {
                    if (section is not PyDict sec || !(PyText.Truthy(sec["text"]) || PyText.Truthy(sec["bullets"])))
                    {
                        output.Add(("ERROR", $"{label}: samples.sections[{i}] needs a heading and text or bullets"));
                    }
                    else if (sec["bullets"] is not null && !(sec["bullets"] is List<object?> b && b.All(x => x is string)))
                    {
                        output.Add(("ERROR", $"{label}: samples.sections[{i}].bullets should be a list of short lines"));
                    }

                    i++;
                }
            }
        }

        if (w["folders"] is not null and not PyDict)
        {
            output.Add(("ERROR", $"{label}: `folders` should map a Panorama folder to a sentence about it"));
        }

        return output;
    }
}
