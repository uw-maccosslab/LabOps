using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;

namespace LabOps.Engines.Projects;

/// <summary>
/// Timelines. A project's samples and each experiment have an ordered list of steps, one per line
/// under `steps:`. Each step has an id (what commands call it) and a kind (what it is), and may have
/// a label (how it reads). Steps repeat and go anywhere, so a project can record a second shipment
/// and an experiment unblinded metadata or assay development. A new project starts with the sample
/// steps and a new experiment with the measurement steps; their ids are their kinds.
/// </summary>
public static partial class Steps
{
    public static readonly IReadOnlyList<string> SampleSteps = ["samples_received", "metadata_organized", "plate_layout", "sample_prep"];

    public static readonly IReadOnlyList<string> ExperimentSteps =
        ["data_acquisition", "data_deposited", "signal_processing", "data_analysis", "results_returned"];

    /// <summary>The kinds, in order, with how each reads.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Kinds =
    [
        new("samples_received", "Samples received"),
        new("metadata_organized", "Metadata organized"),
        new("plate_layout", "Plate layout"),
        new("sample_prep", "Sample prep"),
        new("assay_development", "Assay development"),
        new("data_acquisition", "Data acquisition"),
        new("data_deposited", "Data deposited to Panorama"),
        new("signal_processing", "Signal processing"),
        new("data_analysis", "Data analysis"),
        new("results_returned", "Results returned"),
        new("other", "Other"),
    ];

    private static readonly Dictionary<string, string> KindLabels = Kinds.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal) { "pending", "in_progress", "done", "skipped" };

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]*$")]
    public static partial Regex StepIdPattern();

    public static bool IsKind(object? kind) => kind is string k && KindLabels.ContainsKey(k);

    /// <summary>read_steps: the timeline as mappings; an entry that is not a mapping comes back as {"_invalid": value}.</summary>
    public static List<PyDict> Read(PyDict record) =>
        record["steps"] is List<object?> steps
            ? [.. steps.Select(s => s is PyDict d ? d.Copy() : new PyDict { ["_invalid"] = s })]
            : [];

    public static bool IsInvalid(PyDict step) => step.ContainsKey("_invalid");

    public static string Label(PyDict step) =>
        Values.Text(step["label"])
        ?? (KindLabels.TryGetValue(PyText.Str(step["kind"]), out var label) ? label
            : PyText.Str(PyText.Truthy(step["kind"]) ? step["kind"] : step["id"]));

    public static string Status(PyDict step) => Values.Text(step["status"]) ?? "pending";

    /// <summary>The first step that is neither done nor skipped (null when all are).</summary>
    public static PyDict? Current(IEnumerable<PyDict> steps) => steps.FirstOrDefault(s => Status(s) is not ("done" or "skipped"));

    public static PyDict Find(IReadOnlyList<PyDict> steps, string stepId) =>
        steps.FirstOrDefault(s => PyText.Str(s["id"]) == stepId)
        ?? throw new EngineError($"there is no step {PyText.ReprString(stepId)}; the steps are: "
                                 + string.Join(", ", steps.Select(s => PyText.Str(s["id"]))));

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();

    public static string Slug(string text)
    {
        var slug = NotSlug().Replace(text.ToLowerInvariant(), "_").Trim('_');
        return slug.Length > 0 ? slug : "step";
    }

    public static string NewId(IEnumerable<PyDict> steps, string baseId)
    {
        var taken = steps.Select(s => PyText.Str(s["id"])).ToHashSet(StringComparer.Ordinal);
        var candidate = baseId;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{baseId}_{n}";
        }

        return candidate;
    }

    /// <summary>steps with `added` inserted after or before a named step, else at the end.</summary>
    public static List<PyDict> Positioned(List<PyDict> steps, IEnumerable<PyDict> added, string? after, string? before)
    {
        if (!string.IsNullOrEmpty(after) && !string.IsNullOrEmpty(before))
        {
            throw new EngineError("give --after or --before, not both");
        }

        var at = steps.Count;
        if (!string.IsNullOrEmpty(after) || !string.IsNullOrEmpty(before))
        {
            var anchor = steps.IndexOf(Find(steps, !string.IsNullOrEmpty(after) ? after! : before!));
            at = !string.IsNullOrEmpty(after) ? anchor + 1 : anchor;
        }

        return [.. steps[..at], .. added, .. steps[at..]];
    }

    /// <summary>_step_json: one step as the JSON gives it.</summary>
    public static JsonObject Json(PyDict s) => new()
    {
        ["stage"] = Values.Json(Values.Text(s["id"])),
        ["kind"] = Values.Json(Values.Text(s["kind"])),
        ["label"] = Label(s),
        ["status"] = Status(s),
        ["assigned"] = Values.Json(Values.Text(s["assigned"])),
        ["planned_start"] = Values.Json(Values.Text(s["planned_start"])),
        ["planned_finish"] = Values.Json(Values.Text(s["planned_finish"])),
        ["started"] = Values.Json(Values.Text(s["started"])),
        ["finished"] = Values.Json(Values.Text(s["finished"])),
        ["by"] = Values.Json(Values.Text(s["by"])),
        ["note"] = Values.Json(Values.Text(s["note"])),
    };

    /// <summary>_timeline_json: the current step's id and every step.</summary>
    public static (JsonNode? Current, JsonArray Stages) Timeline(PyDict record)
    {
        var steps = Read(record).Where(s => !IsInvalid(s)).ToList();
        var current = Current(steps);
        return (current is null ? null : Values.Json(Values.Text(current["id"])), [.. steps.Select(s => (JsonNode?)Json(s))]);
    }
}
