using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>One raw file as LabOps read it on Panorama: what the engine's acquisition command takes.</summary>
public sealed record RawFileRead(string Folder, DateTime Acquired, string? MethodFile, string Method, string? Model, string? Serial);

public sealed partial class ProjectsEngine
{
    /// <summary>
    /// acquisition &lt;experiment&gt; --from FILE [--by LOGIN]: records what an experiment's raw files
    /// say about how its data were acquired, as LabOps read them on Panorama (FILE, written by the
    /// app: each file's folder, acquisition time, method and instrument). The raw files are the
    /// record: they replace the `acquisition:` lines, give the data acquisition step the first and
    /// last file's dates and mark it done (each batch from its own folder, when the folders are
    /// numbered like the batches), and name the instrument when the experiment names none.
    /// </summary>
    public CommandResult Acquisition(string item, string fromFile, string? by = null)
    {
        var t = ReadTimeline(item);
        var name = Path.GetFileName(t.Folder);
        if (t.Kind != "experiment")
        {
            throw new EngineError($"raw files belong to an experiment (one measurement of the samples); give one of {name}'s experiments");
        }

        var files = ReadRawFiles(fromFile);
        if (files.Count == 0)
        {
            throw new EngineError("the raw data folder has no raw files yet; read it again once they are on Panorama");
        }

        var entries = files.GroupBy(f => (f.Folder, f.Method, f.MethodFile, f.Model, f.Serial))
            .OrderBy(g => g.Min(f => f.Acquired))
            .Select(g => new PyDict
            {
                ["folder"] = g.Key.Folder,
                ["method_file"] = g.Key.MethodFile,
                ["model"] = g.Key.Model,
                ["serial"] = g.Key.Serial,
                ["files"] = (long)g.Count(),
                ["first"] = DateOnly.FromDateTime(g.Min(f => f.Acquired)),
                ["last"] = DateOnly.FromDateTime(g.Max(f => f.Acquired)),
                ["method"] = g.Key.Method,
            }).ToList();
        var raw = RecordEdits.SetList(t.Raw, Links.AcquisitionKey, entries, Links.AcquisitionComment);

        var notes = new List<string>();
        var (record, _) = Records.Parse(t.Raw, Path.GetFileName(t.Path));
        var serials = Repository.InstrumentSerials();
        var instruments = files.Select(f => f.Serial).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => serials.TryGetValue(s, out var n) ? n : null).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Values.Text(record["instrument"]) is null && instruments.Count == 1)
        {
            raw = YamlText.SetFields(raw, [new("instrument", instruments[0])], after: "lab_contact");
            notes.Add($"instrument {instruments[0]}");
        }

        notes.Add(DateSteps(t.Steps, files, by));
        YamlText.WriteText(t.Path, RecordEdits.WriteSteps(raw, t.Steps));

        var methods = files.Select(f => f.Method).Distinct(StringComparer.Ordinal).Count();
        var models = string.Join(", ", files.Select(f => f.Serial is null ? f.Model : $"{f.Model} ({f.Serial})").OfType<string>().Distinct(StringComparer.Ordinal));
        return Report(t.Kind, t.Folder, t.Path,
            $"{name}: raw files read: {files.Count} file{(files.Count == 1 ? "" : "s")}, {methods} method{(methods == 1 ? "" : "s")}"
            + (models.Length > 0 ? $", {models}" : "")
            + $", {PyText.Str(DateOnly.FromDateTime(files.Min(f => f.Acquired)))} to {PyText.Str(DateOnly.FromDateTime(files.Max(f => f.Acquired)))}; "
            + string.Join("; ", notes));
    }

    /// <summary>The app's list of raw files, refused with a message when it is not one.</summary>
    private static List<RawFileRead> ReadRawFiles(string path)
    {
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new EngineError($"could not read {path}: {ex.Message}");
        }

        if (doc?["files"] is not JsonArray list)
        {
            throw new EngineError($"{path} has no list of raw files (\"files\")");
        }

        var files = new List<RawFileRead>();
        foreach (var file in list)
        {
            if (file is not JsonObject f || Text(f, "folder") is not { } folder || Text(f, "method") is not { } method
                || !DateTime.TryParseExact(Text(f, "acquired"), "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var acquired))
            {
                throw new EngineError($"{path} has a raw file without its folder, method or acquisition time");
            }

            files.Add(new RawFileRead(folder, acquired, Text(f, "method_file"), method, Text(f, "model"), Text(f, "serial")));
        }

        return files;

        static string? Text(JsonObject o, string key) =>
            o[key] is JsonValue v && v.TryGetValue<string>(out var s) && Py.Strip(s).Length > 0 ? Py.Strip(s) : null;
    }

    [GeneratedRegex(@"(\d+)\s*$")]
    private static partial Regex TrailingNumber();

    /// <summary>
    /// Gives the data acquisition step the raw files' dates and marks it done. A step in batches
    /// gets each batch's dates from its own folder when the folders are numbered 1 to N for its N
    /// batches (RawFiles/Plate_1, Plate_2, ...); otherwise the first batch starts with the first
    /// file, the last finishes with the last, and every batch is done. Returns what it did.
    /// </summary>
    private static string DateSteps(List<PyDict> steps, IReadOnlyList<RawFileRead> files, string? by)
    {
        var groups = Steps.BatchGroups(steps);
        var group = groups.FirstOrDefault(g => PyText.Eq(steps[g[0]]["kind"], "data_acquisition"))?.Select(i => steps[i]).ToList();
        var single = steps.FirstOrDefault(s => PyText.Eq(s["kind"], "data_acquisition") && Steps.Batch(s) is null);
        var first = group is not null && (single is null || steps.IndexOf(group[0]) < steps.IndexOf(single)) ? group : null;
        if (first is null && single is null)
        {
            return "no data acquisition step to give their dates";
        }

        if (first is null)
        {
            Done(single!, files);
            return $"{Steps.Name(single!)} done";
        }

        // Folders numbered 1..N, one per batch; folders without a number (process control runs)
        // leave the batches alone.
        var numbered = files.Select(f => f.Folder).Distinct(StringComparer.Ordinal)
            .Select(folder => (Folder: folder, Number: TrailingNumber().Match(folder[(folder.LastIndexOf('/') + 1)..])))
            .Where(x => x.Number.Success)
            .Select(x => (x.Folder, Number: long.Parse(x.Number.Groups[1].Value, CultureInfo.InvariantCulture)))
            .ToList();
        var batches = first.Select(Steps.Batch).ToList();
        if (numbered.Count == first.Count && numbered.Select(n => n.Number).Order().SequenceEqual(batches.OfType<long>().Order()))
        {
            foreach (var step in first)
            {
                var folder = numbered.First(n => n.Number == Steps.Batch(step)).Folder;
                Done(step, [.. files.Where(f => f.Folder == folder)]);
            }

            return $"{Steps.Label(first[0])} done, each batch from its folder";
        }

        foreach (var step in first)
        {
            step["status"] = "done";
            if (!string.IsNullOrEmpty(by))
            {
                step["by"] = by;
            }
        }

        first[0]["started"] = DateOnly.FromDateTime(files.Min(f => f.Acquired));
        first[^1]["finished"] = DateOnly.FromDateTime(files.Max(f => f.Acquired));
        return $"{Steps.Label(first[0])} done (the folders are not numbered like its {first.Count} batches, "
               + "so only the first batch's start and the last one's finish are from the raw files)";

        void Done(PyDict step, IReadOnlyList<RawFileRead> some)
        {
            step["status"] = "done";
            step["started"] = DateOnly.FromDateTime(some.Min(f => f.Acquired));
            step["finished"] = DateOnly.FromDateTime(some.Max(f => f.Acquired));
            if (!string.IsNullOrEmpty(by))
            {
                step["by"] = by;
            }
        }
    }
}
