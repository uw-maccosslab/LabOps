using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>What new-project takes besides the lab and the name.</summary>
public sealed record NewProjectOptions(
    string Title, string Funding = "internal", IReadOnlyList<string>? Quotes = null, string? Grant = null, bool Human = false,
    string? LabContact = null, string? Series = null, string? Species = null, string? SampleType = null, long? ExpectedSamples = null);

/// <summary>What new-experiment takes besides the project and the name.</summary>
public sealed record NewExperimentOptions(
    string Title, string? Instrument = null, string? Funding = null, IReadOnlyList<string>? Quotes = null, string? Grant = null,
    IReadOnlyList<string>? WithSteps = null, string? LabContact = null);

/// <summary>What link takes: the value, and the options of its kind of link.</summary>
public sealed record LinkOptions(
    string? Value, string? Kind = null, string? Id = null, string? Page = null, long? Version = null, string? Step = null, string? Title = null);

// The commands that change records. Each edits the record's text line by line (keeping its
// comments), writes it, and answers with the item's summary as list gives it.
public sealed partial class ProjectsEngine
{
    /// <summary>parse_date: date.fromisoformat, or an error naming the option.</summary>
    public static DateOnly ParseDate(string text, string option = "--date") =>
        WikiPage.FromIsoFormat(text) ?? throw new EngineError($"{option} {text} is not a date written YYYY-MM-DD");

    private string Template(string name) => YamlText.ReadText(Path.Combine(Repository.Templates, name), "templates/" + name);

    private static string SetFunding(string raw, string? type, IReadOnlyList<string>? quotes, string? grant)
    {
        raw = YamlText.SetChild(raw, "funding", "type", type);
        raw = YamlText.SetChild(raw, "funding", "quotes", (quotes ?? []).Select(q => (object?)q).ToList());
        return YamlText.SetChild(raw, "funding", "grant", grant);
    }

    /// <summary>new-lab &lt;Lab&gt; --title T --pi P --institution I [--lab-contact LOGIN].</summary>
    public CommandResult NewLab(string lab, string title, string pi, string institution, string? labContact = null)
    {
        if (!Validation.LabPattern().IsMatch(lab))
        {
            throw new EngineError("a lab is letters, digits and hyphens, like ClearwaterZoo-Cole or UW-MacCoss");
        }

        var folder = Path.Combine(Repository.Projects, lab);
        if (File.Exists(Path.Combine(folder, "lab.yaml")) || Directory.Exists(Path.Combine(folder, "lab.yaml")))
        {
            throw new EngineError($"{Repository.Rel(folder)} already exists");
        }

        var fields = new List<KeyValuePair<string, object?>> { new("lab", lab), new("title", title), new("pi", pi), new("institution", institution) };
        if (!string.IsNullOrEmpty(labContact))
        {
            fields.Add(new("lab_contact", labContact));
        }

        YamlText.WriteText(Path.Combine(folder, "lab.yaml"), YamlText.SetFields(Template("lab.example.yaml"), fields));
        return new CommandResult(new JsonObject { ["ok"] = true, ["folder"] = Repository.Rel(folder) }, [$"created {Repository.Rel(folder)}/lab.yaml"]);
    }

    /// <summary>new-project &lt;Lab&gt; &lt;Project&gt; --title T [--funding ...] ...</summary>
    public CommandResult NewProject(string lab, string project, NewProjectOptions o)
    {
        var labFolder = Path.Combine(Repository.Projects, lab);
        if (!File.Exists(Path.Combine(labFolder, "lab.yaml")))
        {
            throw new EngineError($"there is no lab {lab}; create it first with new-lab");
        }

        if (!Validation.ProjectPattern().IsMatch(project))
        {
            throw new EngineError("a project is named with letters, digits and hyphens, starting with a letter, like MNRF-BioTRACK");
        }

        if (Repository.ProjectFiles().Any(f => Path.GetFileName(Path.GetDirectoryName(f)) == project))
        {
            throw new EngineError($"a project named {project} already exists");
        }

        var folder = Path.Combine(labFolder, project);
        var fields = new List<KeyValuePair<string, object?>> { new("project", project), new("title", o.Title), new("human", o.Human) };
        foreach (var (key, value) in (ReadOnlySpan<(string, object?)>)[("lab_contact", o.LabContact), ("series", o.Series),
                     ("species", o.Species), ("sample_type", o.SampleType), ("expected_samples", o.ExpectedSamples)])
        {
            if (value is not null)
            {
                fields.Add(new(key, value));
            }
        }

        var raw = YamlText.SetFields(Template("project.example.yaml"), fields);
        YamlText.WriteText(Path.Combine(folder, "project.yaml"), SetFunding(raw, o.Funding, o.Quotes, o.Grant));
        foreach (var sub in (string[])["metadata/received", "layout"])
        {
            Directory.CreateDirectory(Path.Combine(folder, sub));
        }

        var inbox = Path.Combine(Repository.Inbox, project);
        Directory.CreateDirectory(inbox);
        return new CommandResult(new JsonObject { ["ok"] = true, ["folder"] = Repository.Rel(folder), ["inbox"] = Repository.Rel(inbox) },
            [$"created {Repository.Rel(folder)}/project.yaml; put the collaborator's files in {Repository.Rel(inbox)}/"]);
    }

    /// <summary>_parse_kind: KIND or KIND=LABEL.</summary>
    private static (string Kind, string? Label) ParseKind(string text)
    {
        var eq = text.IndexOf('=');
        var kind = Py.Strip(eq < 0 ? text : text[..eq]);
        if (!Steps.IsKind(kind))
        {
            throw new EngineError($"{PyText.ReprString(kind)} is not a kind of step; use one of {string.Join(", ", Steps.Kinds.Select(k => k.Key))}");
        }

        var label = eq < 0 ? null : Py.Strip(text[(eq + 1)..]);
        label = string.IsNullOrEmpty(label) ? null : label;
        if (kind == "other" && label is null)
        {
            throw new EngineError("a step of kind other needs a label, for example other=Shipped samples back");
        }

        return (kind, label);
    }

    /// <summary>new-experiment &lt;Project&gt; &lt;YYYY-MM-Topic&gt; --title T [--instrument I] [--with KIND[=LABEL] ...] ...</summary>
    public CommandResult NewExperiment(string project, string name, NewExperimentOptions o)
    {
        var projectFolder = Repository.FindProject(project);
        if (!Validation.ExperimentPattern().IsMatch(name))
        {
            throw new EngineError("an experiment is named YYYY-MM-Topic, for example 2026-09-BioTRACK-DIA");
        }

        if (Repository.ExperimentFiles().Any(f => Path.GetFileName(Path.GetDirectoryName(f)) == name))
        {
            throw new EngineError($"an experiment named {name} already exists");
        }

        var folder = Path.Combine(projectFolder, name);
        var fields = new List<KeyValuePair<string, object?>> { new("experiment", name), new("title", o.Title) };
        foreach (var (key, value) in (ReadOnlySpan<(string, string?)>)[("lab_contact", o.LabContact), ("instrument", o.Instrument)])
        {
            if (!string.IsNullOrEmpty(value))
            {
                fields.Add(new(key, value));
            }
        }

        var raw = YamlText.SetFields(Template("experiment.example.yaml"), fields);
        if (!string.IsNullOrEmpty(o.Funding))
        {
            raw = SetFunding(raw, o.Funding, o.Quotes, o.Grant);
        }

        if (o.WithSteps is { Count: > 0 })
        {
            var steps = Steps.Read(YamlLoader.Load(raw) as PyDict ?? []);
            var first = new List<PyDict>();
            foreach (var spec in o.WithSteps)
            {
                var (kind, label) = ParseKind(spec);
                first.Add(new PyDict
                {
                    ["id"] = Steps.NewId([.. steps, .. first], kind == "other" ? Steps.Slug(label!) : kind), ["kind"] = kind, ["label"] = label,
                });
            }

            raw = RecordEdits.WriteSteps(raw, [.. first, .. steps]);
        }

        YamlText.WriteText(Path.Combine(folder, "experiment.yaml"), raw);
        return new CommandResult(new JsonObject { ["ok"] = true, ["folder"] = Repository.Rel(folder) }, [$"created {Repository.Rel(folder)}/experiment.yaml"]);
    }

    private sealed record Timeline(string Kind, string Folder, string Path, string Raw, List<PyDict> Steps);

    /// <summary>_timeline: an item's record and its steps, refusing a timeline that is not a list of steps.</summary>
    private Timeline ReadTimeline(string reference)
    {
        var (kind, folder) = Repository.FindItem(reference);
        var path = Path.Combine(folder, kind + ".yaml");
        var raw = YamlText.ReadText(path, Repository.Rel(path));
        var (d, problem) = Records.Parse(raw, Path.GetFileName(path));
        if (problem is not null)
        {
            throw new EngineError($"{Repository.Rel(path)}: {problem}; fix it first (run check)");
        }

        if (d["stages"] is PyDict || d["steps"] is not List<object?>)
        {
            throw new EngineError($"{Repository.Rel(path)} has no `steps:` list; run check");
        }

        var steps = Steps.Read(d);
        if (steps.Any(Steps.IsInvalid))
        {
            throw new EngineError($"fix the steps in {Repository.Rel(path)} first (run check)");
        }

        return new Timeline(kind, folder, path, raw, steps);
    }

    /// <summary>Text a command would put on the wiki page, which the collaborators can read.</summary>
    private static void RefuseContactDetails(string text, string what)
    {
        if (Deidentification.HasContactDetails(text))
        {
            throw new EngineError($"{what} has an email address or phone number; the project's wiki page shows it to the "
                                  + "collaborators, so leave contact details out");
        }
    }

    /// <summary>_report: the changed item's summary and its issues.</summary>
    private CommandResult Report(string kind, string folder, string path, string text)
    {
        var known = Repository.People();
        var (record, problem) = Summaries.LoadRecord(path);
        List<(string, string)> issues;
        JsonObject summary;
        if (kind == "project")
        {
            issues = problem is not null ? [("ERROR", problem)] : Validation.ValidateProject(Repository, path, known, record);
            summary = Summaries.Project(Repository, folder, issues, [], record);
        }
        else
        {
            issues = problem is not null ? [("ERROR", problem)] : Validation.ValidateExperiment(Repository, path, known, record);
            var projectFile = Path.Combine(Path.GetDirectoryName(folder)!, "project.yaml");
            var funding = Values.FundingOf(File.Exists(projectFile) ? Summaries.LoadRecord(projectFile).Record["funding"] : null);
            summary = Summaries.Experiment(Repository, folder, issues, funding, record);
        }

        return new CommandResult(new JsonObject { ["ok"] = true, [kind] = summary }, [text, .. issues.Select(i => $"    {i.Item1}: {i.Item2}")]);
    }

    private static string IsoFormat(object date) => date is PyDateTime dt ? dt.IsoFormat('T') : PyText.Str(date);

    private static DateOnly DatePart(object date) => date is PyDateTime dt ? DateOnly.FromDateTime(dt.Value) : (DateOnly)date;

    /// <summary>stage &lt;item&gt; &lt;step&gt; start|done|skip [--date D] [--by LOGIN] [--note N].</summary>
    public CommandResult Stage(string item, string stepId, string action, string? date = null, string? by = null, string? note = null)
    {
        if (action is not ("start" or "done" or "skip"))
        {
            throw new EngineError($"{action} is not start, done or skip");
        }

        var t = ReadTimeline(item);
        var step = Steps.Find(t.Steps, stepId);
        var when = string.IsNullOrEmpty(date) ? Repository.Today() : ParseDate(date);
        var started = WikiPage.Date(step["started"]);
        // project.py compared a date with a datetime and stopped; compare their days.
        if (action == "done" && started is not null && when < DatePart(started))
        {
            throw new EngineError($"{stepId} started on {IsoFormat(started)}, so it cannot be done on {PyText.Str(when)}; "
                                  + "give the date it was finished with --date");
        }

        if (note is not null)
        {
            RefuseContactDetails(note, "the note");
        }

        switch (action)
        {
            case "start":
                step["status"] = "in_progress";
                step["started"] = PyText.Truthy(step["started"]) ? step["started"] : when;
                step.Remove("finished");
                break;
            case "done":
                step["status"] = "done";
                step["started"] = PyText.Truthy(step["started"]) ? step["started"] : when;
                step["finished"] = when;
                break;
            default:
                step.Remove("started");
                step.Remove("finished");
                step["status"] = "skipped";
                break;
        }

        if (!string.IsNullOrEmpty(by))
        {
            step["by"] = by;
            if (action == "start" && !PyText.Truthy(step["assigned"]))
            {
                step["assigned"] = by;
            }
        }

        if (note is not null)
        {
            step["note"] = note;
        }

        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(t.Raw, t.Steps));
        return Report(t.Kind, t.Folder, t.Path, $"{Path.GetFileName(t.Folder)}: {Steps.Name(step)} {PyText.Str(step["status"]).Replace('_', ' ')}");
    }

    /// <summary>assign &lt;item&gt; &lt;step&gt;... (--to LOGIN | --nobody).</summary>
    public CommandResult Assign(string item, IReadOnlyList<string> stepIds, string? to, bool nobody)
    {
        if (!string.IsNullOrEmpty(to) == nobody)
        {
            throw new EngineError("give --to LOGIN, or --nobody to clear the assignment");
        }

        var t = ReadTimeline(item);
        var chosen = stepIds.Select(s => Steps.Find(t.Steps, s)).ToList();
        foreach (var step in chosen)
        {
            if (!string.IsNullOrEmpty(to))
            {
                step["assigned"] = to;
            }
            else
            {
                step.Remove("assigned");
            }
        }

        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(t.Raw, t.Steps));
        var names = string.Join(", ", chosen.Select(s => PyText.Str(s["id"])));
        var name = Path.GetFileName(t.Folder);
        return Report(t.Kind, t.Folder, t.Path, !string.IsNullOrEmpty(to) ? $"{name}: {names} assigned to {to}" : $"{name}: {names} unassigned");
    }

    /// <summary>
    /// plan &lt;item&gt; &lt;step&gt;... [--start DATE | --no-start] [--finish DATE | --no-finish] | --clear: when
    /// steps should start and finish. A date not mentioned keeps the one recorded; --no-start and
    /// --no-finish remove one, --clear both. Whatever is asked is one write, so a plan is never
    /// left half changed.
    /// </summary>
    public CommandResult Plan(string item, IReadOnlyList<string> stepIds, string? start, string? finish, bool clear,
        bool noStart = false, bool noFinish = false)
    {
        if (clear ? start is not null || finish is not null || noStart || noFinish : start is null && finish is null && !noStart && !noFinish)
        {
            throw new EngineError("give --start DATE and/or --finish DATE, or --clear to remove the plan");
        }

        if ((start is not null && noStart) || (finish is not null && noFinish))
        {
            throw new EngineError("give a date or remove it, not both: --start or --no-start, --finish or --no-finish");
        }

        var from = start is null ? (DateOnly?)null : ParseDate(start, "--start");
        var to = finish is null ? (DateOnly?)null : ParseDate(finish, "--finish");
        var t = ReadTimeline(item);
        var chosen = stepIds.Select(s => Steps.Find(t.Steps, s)).ToList();
        foreach (var step in chosen)
        {
            if (clear || noStart)
            {
                step.Remove("planned_start");
            }

            if (clear || noFinish)
            {
                step.Remove("planned_finish");
            }

            if (from is { } f)
            {
                step["planned_start"] = f;
            }

            if (to is { } u)
            {
                step["planned_finish"] = u;
            }

            if (step["planned_start"] is DateOnly s && step["planned_finish"] is DateOnly e && e < s)
            {
                throw new EngineError($"step {PyText.Str(step["id"])} would be planned to finish ({Iso(e)}) before it starts ({Iso(s)})");
            }
        }

        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(t.Raw, t.Steps));
        var names = string.Join(", ", chosen.Select(s => PyText.Str(s["id"])));
        var what = new List<string>();
        if (clear)
        {
            what.Add("plan cleared");
        }
        else if (from is { } f && to is { } u)
        {
            what.Add($"planned {Iso(f)} to {Iso(u)}");
        }
        else
        {
            what.AddRange(from is { } f2 ? [$"planned to start {Iso(f2)}"] : noStart ? ["planned start removed"] : []);
            what.AddRange(to is { } u2 ? [$"planned to finish {Iso(u2)}"] : noFinish ? ["planned finish removed"] : []);
        }

        return Report(t.Kind, t.Folder, t.Path, $"{Path.GetFileName(t.Folder)}: {names} {string.Join(", ", what)}");

        static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>add-step &lt;item&gt; &lt;kind&gt; [--label L] [--after STEP | --before STEP] [--id ID] [--assigned LOGIN].</summary>
    public CommandResult AddStep(string item, string kind, string? label = null, string? after = null, string? before = null, string? id = null, string? assigned = null)
    {
        var t = ReadTimeline(item);
        var (stepKind, stepLabel) = ParseKind(kind + (string.IsNullOrEmpty(label) ? "" : $"={label}"));
        if (stepLabel is not null)
        {
            RefuseContactDetails(stepLabel, "the label");
        }

        var stepId = !string.IsNullOrEmpty(id) ? id : Steps.NewId(t.Steps, stepKind == "other" ? Steps.Slug(stepLabel!) : stepKind);
        var step = new PyDict { ["id"] = stepId, ["kind"] = stepKind, ["label"] = stepLabel, ["assigned"] = assigned };
        if (!Steps.StepIdPattern().IsMatch(stepId) || t.Steps.Any(s => PyText.Str(s["id"]) == stepId))
        {
            throw new EngineError($"{PyText.ReprString(stepId)} cannot be a step id: use lowercase letters, digits, - and _, not used by another step");
        }

        var steps = Steps.Positioned(t.Steps, [step], after, before);
        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(t.Raw, steps));
        return Report(t.Kind, t.Folder, t.Path, $"{Path.GetFileName(t.Folder)}: added step {stepId} ({Steps.Label(step)})");
    }

    /// <summary>remove-step &lt;item&gt; &lt;step&gt;: only a step not yet started and with no record.</summary>
    public CommandResult RemoveStep(string item, string stepId)
    {
        var t = ReadTimeline(item);
        var step = Steps.Find(t.Steps, stepId);
        if (Steps.Batch(step) is { } batch)
        {
            throw new EngineError($"step {stepId} is batch {batch} of {Steps.Label(step)}; change the number of batches with "
                                  + $"split {Path.GetFileName(t.Folder)} {stepId} --batches N");
        }

        if (Steps.Status(step) != "pending" || ((string[])["started", "finished", "by", "note"]).Any(k => PyText.Truthy(step[k])))
        {
            throw new EngineError($"step {stepId} has been started or has a record; skip it instead of removing it");
        }

        if (t.Steps.Count == 1)
        {
            throw new EngineError("a timeline needs at least one step");
        }

        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(t.Raw, t.Steps.Where(s => !ReferenceEquals(s, step))));
        return Report(t.Kind, t.Folder, t.Path, $"{Path.GetFileName(t.Folder)}: removed step {stepId}");
    }

    /// <summary>The most batches a step is split into: a run of 96-well plates is rarely more than a dozen.</summary>
    public const int MaxBatches = 48;

    /// <summary>
    /// split &lt;item&gt; &lt;step&gt; --batches N: a step's work in N batches (sample prep of three plates),
    /// each a step of its own numbered 1 to N, which starts, finishes, is assigned and planned on its
    /// own. A step already in batches gets more (after its last) or fewer (its last ones go, when
    /// nothing is recorded or planned for them).
    /// </summary>
    public CommandResult Split(string item, string stepId, int batches)
    {
        if (batches is < 2 or > MaxBatches)
        {
            throw new EngineError($"give --batches from 2 to {MaxBatches}; a step done all at once needs no batches");
        }

        var t = ReadTimeline(item);
        var step = Steps.Find(t.Steps, stepId);
        var at = t.Steps.FindIndex(s => ReferenceEquals(s, step));
        var name = Path.GetFileName(t.Folder);
        var label = Steps.Label(step);
        var raw = t.Raw;
        var steps = t.Steps;
        string text;
        if (Steps.BatchGroups(t.Steps).FirstOrDefault(g => g.Contains(at)) is not { } group)
        {
            if (Steps.Status(step) == "skipped")
            {
                throw new EngineError($"step {stepId} was skipped, so there is no work to split into batches");
            }

            var made = Batches(t.Steps, step, batches, null);
            steps = [.. t.Steps[..at], .. made, .. t.Steps[(at + 1)..]];
            raw = MoveProtocols(raw, Path.GetFileName(t.Path), stepId, PyText.Str(made[0]["id"]));
            text = $"{name}: {label} in {batches} batches ({string.Join(", ", made.Select(s => PyText.Str(s["id"])))})";
        }
        else if (group.Count == batches)
        {
            return Report(t.Kind, t.Folder, t.Path, $"{name}: {label} is in {batches} batches already");
        }
        else if (batches > group.Count)
        {
            var last = t.Steps[group[^1]];
            var baseId = Steps.BatchBase(PyText.Str(t.Steps[group[0]]["id"]));
            var added = new List<PyDict>();
            for (var b = group.Count + 1; b <= batches; b++)
            {
                added.Add(new PyDict
                {
                    ["id"] = Steps.NewId([.. t.Steps, .. added], $"{baseId}_batch{b}"),
                    ["kind"] = last["kind"], ["label"] = last["label"], ["batch"] = (long)b, ["assigned"] = last["assigned"],
                });
            }

            steps = [.. t.Steps[..(group[^1] + 1)], .. added, .. t.Steps[(group[^1] + 1)..]];
            text = $"{name}: {label} in {batches} batches, added {string.Join(", ", added.Select(s => PyText.Str(s["id"])))}";
        }
        else
        {
            // A batch goes only while nothing is recorded, planned or linked for it: a protocol
            // recorded for it would be left naming a step that is gone.
            var (record, _) = Records.Parse(raw, Path.GetFileName(t.Path));
            var linked = Values.ListOf(record, "protocols", Values.ProtocolFields).Select(p => PyText.Str(p["step"])).ToHashSet(StringComparer.Ordinal);
            var gone = group.Skip(batches).Select(i => t.Steps[i]).ToList();
            if (gone.FirstOrDefault(s => Steps.Status(s) != "pending" || linked.Contains(PyText.Str(s["id"]))
                    || ((string[])["started", "finished", "by", "note", "planned_start", "planned_finish"]).Any(k => PyText.Truthy(s[k])))
                is { } kept)
            {
                throw new EngineError($"batch {Steps.Batch(kept)} of {label} ({PyText.Str(kept["id"])}) has a record, a plan or a "
                                      + $"protocol, so {label} cannot go down to {batches} batches");
            }

            steps = [.. t.Steps.Where(s => !gone.Contains(s))];
            text = $"{name}: {label} in {batches} batches, removed {string.Join(", ", gone.Select(s => PyText.Str(s["id"])))}";
        }

        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(raw, steps));
        return Report(t.Kind, t.Folder, t.Path, text);
    }

    /// <summary>
    /// A step as <paramref name="count"/> batches. What is known of the whole goes where it is
    /// known to be true: its start (and planned start) on the first batch, its finish (and planned
    /// finish) on the last, the work under way on the first, and done on every batch. Each batch's
    /// note is the one given (a plate's samples), else the step's note on the first.
    /// </summary>
    private static List<PyDict> Batches(List<PyDict> steps, PyDict step, int count, IReadOnlyList<string>? notes)
    {
        var id = PyText.Str(step["id"]);
        var status = Steps.Status(step);
        var others = steps.Where(s => !ReferenceEquals(s, step)).ToList();
        var made = new List<PyDict>();
        for (var b = 1; b <= count; b++)
        {
            var first = b == 1;
            var last = b == count;
            made.Add(new PyDict
            {
                ["id"] = Steps.NewId([.. others, .. made], $"{id}_batch{b}"),
                ["kind"] = step["kind"],
                ["label"] = step["label"],
                ["batch"] = (long)b,
                ["status"] = status switch { "done" => "done", "in_progress" when first => "in_progress", _ => "pending" },
                ["assigned"] = step["assigned"],
                ["planned_start"] = first ? step["planned_start"] : null,
                ["planned_finish"] = last ? step["planned_finish"] : null,
                ["started"] = first && status is "done" or "in_progress" ? step["started"] : null,
                ["finished"] = last && status == "done" ? step["finished"] : null,
                ["by"] = status == "done" || (first && status == "in_progress") ? step["by"] : null,
                ["note"] = notes is not null ? notes[b - 1] : first ? step["note"] : null,
            });
        }

        return made;
    }

    /// <summary>The protocols recorded for a step that is now in batches, recorded for its first batch.</summary>
    private static string MoveProtocols(string raw, string file, string from, string to)
    {
        var (d, problem) = Records.Parse(raw, file);
        var items = problem is null ? Values.ListOf(d, "protocols", Values.ProtocolFields) : [];
        var moved = items.Where(i => PyText.Eq(i["step"], from)).ToList();
        foreach (var i in moved)
        {
            i["step"] = to;
        }

        return moved.Count > 0 ? RecordEdits.SetList(raw, "protocols", items, Links.ProtocolComment) : raw;
    }

    private (string Kind, string Folder, string Path, string Raw, PyDict Record) LinkTarget(string item, string what)
    {
        var (kind, folder) = Repository.FindItem(item);
        if (what == "wiki" && kind != "project")
        {
            throw new EngineError("the wiki page belongs to the project; give the project, not the experiment");
        }

        if (what == "panorama" && kind != "experiment")
        {
            throw new EngineError("Panorama folders belong to an experiment (the data and results of one measurement); "
                                  + $"give one of {Path.GetFileName(folder)}'s experiments");
        }

        var path = Path.Combine(folder, kind + ".yaml");
        var raw = YamlText.ReadText(path, Repository.Rel(path));
        var (d, problem) = Records.Parse(raw, Path.GetFileName(path));
        return problem is null ? (kind, folder, path, raw, d) : throw new EngineError($"{Repository.Rel(path)}: {problem}; fix it first (run check)");
    }

    /// <summary>
    /// review &lt;item&gt; &lt;file&gt; [column...] --by LOGIN: records that a person read the file's free-text
    /// columns (all of them, or those named) and found nothing identifying, so check stops warning
    /// about them until their text changes. The file is the project's or experiment's own, given
    /// from its folder (metadata/samples.csv) or from the repository's root.
    /// </summary>
    public CommandResult Review(string item, string file, IReadOnlyList<string> columns, string by)
    {
        if (Stripped(by) is not { } who)
        {
            throw new EngineError("give --by LOGIN: who read the columns");
        }

        var (kind, folder, path, raw, record) = LinkTarget(item, "review");
        var relative = file.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(relative.StartsWith("projects/", StringComparison.Ordinal)
            ? Path.Combine(Repository.Root, relative) : Path.Combine(folder, relative));
        if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)
            || Reviews.Owner(Repository, Repository.Rel(full)) is not { } owner || !string.Equals(owner, folder, StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineError($"{file} is not a file of {Path.GetFileName(folder)} (give it from {Repository.Rel(folder)}/, "
                                  + "for example metadata/samples.csv)");
        }

        var name = Repository.Rel(full);
        var freeText = Deidentification.DataFindings(name, Sheets.DecodeText(File.ReadAllBytes(full), name))
            .Where(f => f.Fingerprint is not null && f.Column is not null).ToList();
        var chosen = columns.Count == 0 ? freeText : [.. columns.Select(c => freeText.FirstOrDefault(f => f.Column == c)
            ?? throw new EngineError($"{name} has no free-text column {PyText.ReprString(c)}; the free-text columns are: "
                                     + (freeText.Count == 0 ? "none" : string.Join(", ", freeText.Select(f => f.Column)))))];
        if (chosen.Count == 0)
        {
            throw new EngineError($"{name} has no free-text column to review");
        }

        var key = Path.GetRelativePath(folder, full).Replace('\\', '/');
        // A date, as the steps' dates are, not text that looks like one (which YAML would quote).
        var today = Repository.Today();
        var kept = (record[Reviews.Key] as List<object?> ?? []).OfType<PyDict>()
            .Where(e => !(Values.Text(e["file"]) == key && chosen.Any(c => c.Column == Values.Text(e["column"]))));
        var added = chosen.Select(c => new PyDict
        {
            ["file"] = key, ["column"] = c.Column, ["values"] = c.Fingerprint, ["by"] = who, ["date"] = today,
        });
        var entries = kept.Concat(added).OrderBy(e => Values.Text(e["file"]), StringComparer.Ordinal)
            .ThenBy(e => Values.Text(e["column"]), StringComparer.Ordinal).ToList();
        YamlText.WriteText(path, RecordEdits.SetList(raw, Reviews.Key, entries, Reviews.Comment));
        return Report(kind, folder, path, $"{Path.GetFileName(folder)}: {key} reviewed by {who}: "
                                          + string.Join(", ", chosen.Select(c => c.Column)));
    }

    private static string? Stripped(string? text) => Py.Strip(text ?? "") is { Length: > 0 } s ? s : null;

    /// <summary>link &lt;item&gt; panorama|notebook|wiki|protocol [value] [options].</summary>
    public CommandResult Link(string item, string what, LinkOptions o)
    {
        if (what is not ("panorama" or "notebook" or "wiki" or "protocol"))
        {
            throw new EngineError($"{what} is not panorama, notebook, wiki or protocol");
        }

        var (kind, folder, path, raw, d) = LinkTarget(item, what);
        var name = Path.GetFileName(folder);
        if (what == "protocol")
        {
            return LinkProtocol(o, kind, folder, path, raw, d);
        }

        if (what == "wiki")
        {
            if (string.IsNullOrEmpty(o.Value))
            {
                throw new EngineError("give the project's Panorama folder, or paste its address");
            }

            var target = Links.PanoramaFolder(o.Value);
            var page = Py.Strip(o.Page ?? "");
            if (page.Length == 0 && Links.WebAddress().IsMatch(Py.Strip(o.Value)))
            {
                page = Links.QueryValue(Links.SplitUrl(Py.Strip(o.Value)).Query, "name");
            }

            page = page.Length > 0 ? page : "default";
            if (target.Contains("@files", StringComparison.Ordinal))
            {
                throw new EngineError($"{target} is a file area; give the project's folder, where the page goes");
            }

            if (!Validation.WikiPagePattern().IsMatch(page))
            {
                throw new EngineError($"{PyText.ReprString(page)} is not a wiki page name: letters, digits, spaces, hyphens, dots or underscores");
            }

            YamlText.WriteText(path, RecordEdits.SetWiki(raw, new PyDict { ["folder"] = target, ["page"] = page }));
            return Report(kind, folder, path, $"{name}: wiki page {page} in {target}");
        }

        List<PyDict> items;
        string text;
        if (what == "panorama")
        {
            if (string.IsNullOrEmpty(o.Value))
            {
                throw new EngineError("give the Panorama folder, or paste its address");
            }

            if (!Validation.In(o.Kind, Validation.PanoramaKinds))
            {
                throw new EngineError("give --kind raw (the raw data), --kind results (results shared there) or --kind qc "
                                      + "(the process control runs)");
            }

            var target = Links.PanoramaFolder(o.Value);
            items = Values.ListOf(d, "panorama", Values.PanoramaFields);
            var found = items.FirstOrDefault(i => WikiPage.SameFolder(i["folder"], target));
            if (found is not null)
            {
                found["folder"] = target;
                found["kind"] = o.Kind;
            }
            else
            {
                items.Add(new PyDict { ["folder"] = target, ["kind"] = o.Kind });
            }

            text = $"{name}: Panorama {o.Kind} folder {target}";
        }
        else
        {
            var url = Stripped(o.Value);
            var nid = Stripped(o.Id);
            if (url is null && nid is null)
            {
                throw new EngineError("give the notebook's link, its ID (--id ELN-...), or both");
            }

            if (url is not null && !Links.WebAddress().IsMatch(url))
            {
                throw new EngineError($"{url} is not a link; give the notebook's ID with --id instead");
            }

            url ??= Links.NotebookUrl(nid);
            items = Values.ListOf(d, "notebooks", Values.NotebookFields);
            var found = items.FirstOrDefault(i => (nid is not null && PyText.Eq(i["id"], nid)) || (url is not null && PyText.Eq(i["url"], url)));
            if (found is not null)
            {
                if (nid is not null)
                {
                    found["id"] = nid;
                }

                if (url is not null)
                {
                    found["url"] = url;
                }
            }
            else
            {
                items.Add(new PyDict { ["id"] = nid, ["url"] = url });
            }

            text = $"{name}: notebook {nid ?? url}";
        }

        YamlText.WriteText(path, RecordEdits.WriteList(raw, what == "panorama" ? "panorama" : "notebooks", items));
        return Report(kind, folder, path, text);
    }

    private CommandResult LinkProtocol(LinkOptions o, string kind, string folder, string path, string raw, PyDict d)
    {
        var name = Path.GetFileName(folder);
        var pid = Py.Strip(o.Value ?? "");
        if (!Validation.ProtocolIdPattern().IsMatch(pid))
        {
            throw new EngineError("give the protocol's ID in LabOps-Protocols, for example spin-trap-digestion");
        }

        if (o.Version is null or < 1)
        {
            throw new EngineError("give --version: the version of the protocol that was used (1, 2, ...)");
        }

        var step = Stripped(o.Step);
        var steps = Steps.Read(d).Where(s => !Steps.IsInvalid(s)).Select(s => PyText.Str(s["id"])).ToList();
        if (step is not null && !steps.Contains(step))
        {
            throw new EngineError($"{name} has no step {PyText.ReprString(step)}; its steps are {string.Join(", ", steps)}");
        }

        var items = Values.ListOf(d, "protocols", Values.ProtocolFields);
        var found = items.FirstOrDefault(i => PyText.Eq(i["id"], pid) && PyText.Eq(PyText.Truthy(i["step"]) ? i["step"] : null, step));
        // The title given, else the one recorded with the link before.
        var title = Stripped(o.Title) is { } given ? given : found?["title"];
        if (PyText.Truthy(title))
        {
            RefuseContactDetails(PyText.Str(title), "the title");
        }

        var entry = new PyDict { ["id"] = pid, ["version"] = o.Version.Value, ["title"] = title, ["step"] = step };
        if (found is not null)
        {
            foreach (var key in found.Keys.ToList())
            {
                found.Remove(key);
            }

            foreach (var (key, value) in entry)
            {
                found[key] = value;
            }
        }
        else
        {
            items.Add(entry);
        }

        YamlText.WriteText(path, RecordEdits.SetList(raw, "protocols", items, Links.ProtocolComment));
        return Report(kind, folder, path, $"{name}: protocol {pid} version {o.Version.Value}" + (step is not null ? $" for {step}" : ""));
    }

    /// <summary>unlink &lt;item&gt; panorama|notebook|wiki|protocol [value] [--step STEP | --all].</summary>
    public CommandResult Unlink(string item, string what, string? value, string? step = null, bool all = false)
    {
        if (what is not ("panorama" or "notebook" or "wiki" or "protocol"))
        {
            throw new EngineError($"{what} is not panorama, notebook, wiki or protocol");
        }

        var (kind, folder, path, raw, d) = LinkTarget(item, what);
        var name = Path.GetFileName(folder);
        if (all && what != "protocol")
        {
            throw new EngineError("--all is for protocols: every link to one protocol, whatever its step");
        }

        if (what == "protocol")
        {
            // One link: the one for --step, or without it the one for the item as a whole (what
            // LabOps's Remove means). --all removes every link to the protocol.
            var pid = Py.Strip(value ?? "");
            var forStep = Stripped(step);
            if (all && forStep is not null)
            {
                throw new EngineError("give --step for one step's link, or --all for every link, not both");
            }

            var items = Values.ListOf(d, "protocols", Values.ProtocolFields);
            var keep = items.Where(i => !(PyText.Eq(i["id"], pid) && (all || PyText.Eq(PyText.Truthy(i["step"]) ? i["step"] : null, forStep)))).ToList();
            if (keep.Count == items.Count)
            {
                var linked = items.Where(i => PyText.Eq(i["id"], pid))
                    .Select(i => PyText.Truthy(i["step"]) ? PyText.Str(i["step"]) : "the item as a whole").Order(StringComparer.Ordinal).ToList();
                throw new EngineError($"{name} has no protocol {pid}" + (all ? "" : $" for {forStep ?? "the item as a whole"}")
                                      + (linked.Count > 0 ? $"; it is linked for {string.Join(", ", linked)} (give --step, or --all for every link)" : ""));
            }

            YamlText.WriteText(path, RecordEdits.WriteList(raw, "protocols", keep));
            var whose = all ? " (every link)" : forStep is not null ? $" for {forStep}" : "";
            return Report(kind, folder, path, $"{name}: removed protocol {pid}{whose}");
        }

        if (what == "wiki")
        {
            if (d["wiki"] is not PyDict)
            {
                throw new EngineError($"{name} has no wiki page recorded");
            }

            YamlText.WriteText(path, RecordEdits.SetWiki(raw, null));
            return Report(kind, folder, path, $"{name}: wiki page no longer recorded (it stays on Panorama)");
        }

        if (string.IsNullOrEmpty(value))
        {
            throw new EngineError("give the Panorama folder (or address), or the notebook's ID or link");
        }

        var key = what == "panorama" ? "panorama" : "notebooks";
        var list = Values.ListOf(d, key, key == "panorama" ? Values.PanoramaFields : Values.NotebookFields);
        List<PyDict> kept;
        if (key == "panorama")
        {
            var target = Links.PanoramaFolder(value);
            kept = [.. list.Where(i => !WikiPage.SameFolder(i["folder"], target))];
        }
        else
        {
            var v = Py.Strip(value);
            kept = [.. list.Where(i => !(PyText.Eq(i["id"], v) || PyText.Eq(i["url"], v)))];
        }

        if (kept.Count == list.Count)
        {
            throw new EngineError($"{name} has no {(key == "panorama" ? "Panorama folder" : "notebook")} {value}");
        }

        YamlText.WriteText(path, RecordEdits.WriteList(raw, key, kept));
        return Report(kind, folder, path, $"{name}: removed {value}");
    }

    // ---------------------------------------------------------------- Octopus
    // Octopus (github.com/UWPR/octopus) is a browser app with no API. It reads one CSV and exports a
    // layout JSON (format "octopus-layout", schemaVersion 1) and a Thermo sequence CSV. Its
    // randomization is unseeded, so the exported layout, kept beside the exact input that produced
    // it, is the record of where each sample went.

    [GeneratedRegex("[<>:\"/\\\\|?*\\x00-\\x1f;,]")]
    private static partial Regex WindowsForbidden();

    private static readonly HashSet<string> WindowsReserved =
        ["con", "prn", "aux", "nul", .. Enumerable.Range(1, 9).Select(i => $"com{i}"), .. Enumerable.Range(1, 9).Select(i => $"lpt{i}")];

    private static readonly HashSet<string> OctopusIdHeaders = ["searchname", "uwsampleid", "sampleid"];

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NotAlphanumeric();

    /// <summary>Headers and rows for Octopus from samples.csv, with warnings; refuses anything Octopus or the instrument would reject.</summary>
    private (List<string> Headers, List<List<string>> Rows, List<string> Warnings) OctopusRows(string folder)
    {
        var samples = Path.Combine(folder, "metadata", "samples.csv");
        if (!File.Exists(samples))
        {
            throw new EngineError($"{Repository.Rel(samples)} does not exist; organize the metadata first");
        }

        var problems = Validation.ValidateSamples(Repository, samples).Where(p => p.Item1 == "ERROR").Select(p => p.Item2).ToList();
        if (problems.Count > 0)
        {
            throw new EngineError(string.Join("; ", problems));
        }

        var (_, headers, all) = Sheets.Read(samples)[0];
        var rows = all.Select(r => r.Select(Sheets.CellText).ToList()).ToList();
        var warnings = new List<string>();

        // Octopus picks its ID column automatically; make sure it can only pick Sample_ID.
        var outHeaders = new List<string>();
        foreach (var header in headers)
        {
            var h = header;
            if (h != "Sample_ID" && OctopusIdHeaders.Contains(NotAlphanumeric().Replace(h.ToLowerInvariant(), "")))
            {
                warnings.Add($"renamed column {PyText.ReprString(h)} to 'Client {h}' so Octopus uses Sample_ID as the ID");
                h = $"Client {h}";
            }

            outHeaders.Add(h);
        }

        var order = new List<int> { outHeaders.IndexOf("Sample_ID") };
        order.AddRange(outHeaders.Select((h, i) => (h, i)).Where(x => x.h != "Sample_ID" && x.h.Length > 0).Select(x => x.i));
        outHeaders = [.. order.Select(i => outHeaders[i])];
        rows = [.. rows.Select(r => order.Select(i => r[i]).ToList())];
        if (outHeaders.Distinct(StringComparer.Ordinal).Count() != outHeaders.Count)
        {
            throw new EngineError("samples.csv has repeated column names; Octopus needs every header to be unique");
        }

        var bad = rows.Select(r => r[0]).Where(sid => WindowsForbidden().IsMatch(sid) || sid.EndsWith(' ') || sid.EndsWith('.')
                                                       || WindowsReserved.Contains(sid.Split('.')[0].ToLowerInvariant())).ToList();
        if (bad.Count > 0)
        {
            throw new EngineError("these Sample_IDs cannot become raw file names (no ; , < > : \" / \\ | ? * and no "
                                  + $"trailing dot or space): {string.Join(", ", bad.Take(5))}");
        }

        if (rows.Any(r => r.Any(v => v.Contains('\n', StringComparison.Ordinal) || v.Contains('\r', StringComparison.Ordinal))))
        {
            throw new EngineError("samples.csv has line breaks inside values; put each value on one line");
        }

        return (outHeaders, rows, warnings);
    }

    /// <summary>octopus-input &lt;project&gt;: write the CSV for Octopus (in inbox/).</summary>
    public CommandResult OctopusInput(string project)
    {
        var folder = Repository.FindProject(project);
        var (headers, rows, warnings) = OctopusRows(folder);
        var output = Path.Combine(Repository.Inbox, Path.GetFileName(folder), "octopus-input.csv");
        YamlText.WriteText(output, string.Concat(new[] { headers }.Concat(rows).Select(r => CsvRow(r) + "\n")));
        return new CommandResult(new JsonObject
        {
            ["ok"] = true, ["file"] = output, ["samples"] = rows.Count, ["warnings"] = new JsonArray([.. warnings.Select(w => (JsonNode?)w)]),
        }, [$"wrote {Repository.Rel(output)} ({rows.Count} samples); load it at https://uwpr.github.io/octopus", .. warnings.Select(w => $"    WARN: {w}")]);
    }

    /// <summary>import-layout &lt;project&gt; &lt;layout.json&gt; [--input CSV] [--step STEP] [--by LOGIN].</summary>
    public CommandResult ImportLayout(string project, string layoutFile, string? input = null, string? stepId = null, string? by = null)
    {
        var folder = Repository.FindProject(project);
        var t = ReadTimeline(folder);
        PyDict? target;
        if (!string.IsNullOrEmpty(stepId))
        {
            target = Steps.Find(t.Steps, stepId);
            if (!Equals(target["kind"], "plate_layout"))
            {
                throw new EngineError($"step {stepId} is not a plate layout step");
            }
        }
        else
        {
            var layoutSteps = t.Steps.Where(s => Equals(s["kind"], "plate_layout")).ToList();
            target = layoutSteps.FirstOrDefault(s => Steps.Status(s) is "pending" or "in_progress") ?? layoutSteps.FirstOrDefault();
        }

        if (target is null)
        {
            throw new EngineError("the project has no plate layout step; add one with add-step <project> plate_layout");
        }

        var prepStarted = t.Steps.Any(s => Equals(s["kind"], "sample_prep") && Steps.Status(s) is "in_progress" or "done");
        var layoutDir = Path.Combine(folder, "layout");
        if (File.Exists(Path.Combine(layoutDir, "octopus-layout.json")) && prepStarted)
        {
            throw new EngineError("sample prep has started on the current layout; a new layout would no longer match the "
                                  + "plates. Start a new project for a re-run instead");
        }

        object? doc;
        try
        {
            doc = PyJsonDecoder.Loads(YamlText.ReadText(layoutFile, layoutFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EngineError(File.Exists(layoutFile) || Directory.Exists(layoutFile)
                ? $"{layoutFile} is not a readable JSON file: {ex.Message}"
                // Python names the file as its Path writes it, with this system's separators.
                : $"{layoutFile} is not a readable JSON file: [Errno 2] No such file or directory: "
                  + PyText.ReprString(layoutFile.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (PyJsonException ex)
        {
            throw new EngineError($"{layoutFile} is not a readable JSON file: {ex.Message}");
        }

        if (doc is not PyDict layout || !PyText.Eq(layout["format"], "octopus-layout"))
        {
            throw new EngineError($"{layoutFile} is not an Octopus layout (export it with Export > Layout)");
        }

        if (!PyText.Eq(layout["schemaVersion"], 1L))
        {
            throw new EngineError($"Octopus layout schema version {PyText.Str(layout["schemaVersion"])} is not supported (expected 1)");
        }

        var samples = (PyText.Truthy(layout["samples"]) ? Values.Iterate(layout["samples"]) : []).ToList();
        var layoutIds = samples.Select(s => PyText.Str(s is PyDict sd && sd.ContainsKey("id") ? sd["id"] : "")).ToList();

        var samplesCsv = Path.Combine(folder, "metadata", "samples.csv");
        if (!File.Exists(samplesCsv))
        {
            throw new EngineError($"{Repository.Rel(samplesCsv)} does not exist; the layout is made from it");
        }

        var (_, headers, rows) = Sheets.Read(samplesCsv)[0];
        var idIndex = Validation.IndexOf(headers, "Sample_ID");
        if (idIndex < 0)
        {
            throw new EngineError($"{Repository.Rel(samplesCsv)} has no Sample_ID column");
        }

        var ids = rows.Select(r => Sheets.CellText(r[idIndex])).ToList();
        var missing = ids.Except(layoutIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var extra = layoutIds.Except(ids, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || extra.Count > 0 || layoutIds.Count != layoutIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new EngineError("the layout's samples do not match samples.csv"
                                  + (missing.Count > 0 ? $"; missing {string.Join(", ", missing.Take(5))}" : "")
                                  + (extra.Count > 0 ? $"; not in samples.csv {string.Join(", ", extra.Take(5))}" : ""));
        }

        var source = !string.IsNullOrEmpty(input) ? input : Path.Combine(Repository.Inbox, Path.GetFileName(folder), "octopus-input.csv");
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new EngineError($"{source} does not exist; give the exact CSV that was loaded into Octopus with --input");
        }

        var settings = layout["settings"] as PyDict ?? [];
        var (_, inHeaders, inRows) = Sheets.Read(source)[0];
        var idColumn = settings["idColumn"] is string c && inHeaders.Contains(c) ? c : "Sample_ID";
        var inIndex = Validation.IndexOf(inHeaders, idColumn);
        if (inIndex < 0)
        {
            throw new EngineError($"{source} has no {idColumn} column; it is not the input for this layout");
        }

        if (!inRows.Select(r => Sheets.CellText(r[inIndex])).Order(StringComparer.Ordinal).SequenceEqual(ids.Order(StringComparer.Ordinal)))
        {
            throw new EngineError($"{source} is not the input for this layout: its samples differ");
        }

        var plateCount = layout["plateCount"];
        object plates = PyText.Truthy(plateCount)
            ? plateCount!
            : (long)samples.Select(s => s is PyDict sd ? sd["plate"] : null).Distinct(new PyEquality()).Count();
        var today = Repository.Today();
        var record = new PyDict
        {
            ["plates"] = plates, ["samples"] = (long)samples.Count, ["imported"] = today,
            ["id_column"] = settings["idColumn"], ["covariates"] = PyText.Truthy(settings["covariates"]) ? settings["covariates"] : new List<object?>(),
            ["qc_column"] = PyText.Truthy(settings["qcColumn"]) ? settings["qcColumn"] : null,
            ["subject_column"] = PyText.Truthy(settings["subjectColumn"]) ? settings["subjectColumn"] : null,
            ["octopus_version"] = layout["appVersion"],
        };
        target["status"] = "done";
        target["started"] = PyText.Truthy(target["started"]) ? target["started"] : today;
        target["finished"] = today;
        target["note"] = $"Octopus layout: {PyText.Str(plates)} plate(s), {samples.Count} samples";
        if (!string.IsNullOrEmpty(by))
        {
            target["by"] = by;
        }

        var withLayout = YamlText.SetFields(t.Raw, [new("layout", record)], after: "expected_samples");
        var lines = new List<string>
        {
            $"{Path.GetFileName(folder)}: imported {samples.Count} samples on {PyText.Str(plates)} plate(s); {PyText.Str(target["id"])} done",
        };

        // Octopus balances the samples across plates, and each plate is prepared as a batch: sample
        // prep becomes one batch per plate, when it is one step not yet begun.
        var perPlate = samples.Select(s => s is PyDict sd ? sd["plate"] : null).Where(p => p is not null)
            .GroupBy(p => p, new PyEquality())
            .OrderBy(g => g.Key is long n ? n : long.MaxValue).ThenBy(g => PyText.Str(g.Key), StringComparer.Ordinal)
            .Select(g => (Plate: PyText.Str(g.Key), Count: g.Count())).ToList();
        if (perPlate.Count >= 2 && t.Steps.FirstOrDefault(s => Equals(s["kind"], "sample_prep")) is { } prep)
        {
            var prepId = PyText.Str(prep["id"]);
            var at = t.Steps.FindIndex(s => ReferenceEquals(s, prep));
            if (Steps.BatchGroups(t.Steps).FirstOrDefault(g => g.Contains(at)) is { } group)
            {
                if (group.Count != perPlate.Count)
                {
                    lines.Add($"    WARN: {Steps.Label(prep)} is in {group.Count} batches but the layout has {perPlate.Count} plates; "
                              + $"change it with split {Path.GetFileName(folder)} {prepId} --batches {perPlate.Count}");
                }
            }
            else if (Steps.Status(prep) == "pending" && !((string[])["started", "finished", "by", "note"]).Any(k => PyText.Truthy(prep[k])))
            {
                var made = Batches(t.Steps, prep, perPlate.Count, [.. perPlate.Select(p => $"Plate {p.Plate}: {p.Count} samples")]);
                t.Steps.RemoveAt(at);
                t.Steps.InsertRange(at, made);
                withLayout = MoveProtocols(withLayout, Path.GetFileName(t.Path), prepId, PyText.Str(made[0]["id"]));
                lines.Add($"    {prepId} is now {perPlate.Count} batches, one per plate");
            }
        }

        // Every change is made before anything is written, so a failure leaves no layout without its record.
        var raw = RecordEdits.WriteSteps(withLayout, t.Steps);
        YamlText.WriteText(Path.Combine(layoutDir, "octopus-layout.json"), PyJson.DumpsIndented(layout) + "\n");
        YamlText.WriteText(Path.Combine(layoutDir, "octopus-input.csv"),
            Sheets.DecodeText(File.ReadAllBytes(source), Path.GetFileName(source)).Replace("\r\n", "\n", StringComparison.Ordinal));
        YamlText.WriteText(t.Path, raw);

        var (p, problem) = Summaries.LoadRecord(t.Path);
        var issues = problem is not null ? [("ERROR", problem)] : Validation.ValidateProject(Repository, t.Path, Repository.People(), p);
        return new CommandResult(new JsonObject { ["ok"] = true, ["project"] = Summaries.Project(Repository, folder, issues, [], p) }, lines);
    }

    /// <summary>Python's == and hash, for distinct values.</summary>
    private sealed class PyEquality : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y) => PyText.Eq(x, y);

        public int GetHashCode(object? obj) => new PyKey(obj).GetHashCode();
    }
}
