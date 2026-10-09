using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>
/// The edits that write a project's or experiment's lists: its steps one per line, its links
/// (panorama, notebooks, protocols), and its wiki line. Ported from project.py (write_steps,
/// _clean_step, write_list, set_list, set_wiki), whose output this matches byte for byte.
/// </summary>
public static class RecordEdits
{
    /// <summary>The order a step's keys are written in; any others follow in their own order.</summary>
    public static readonly IReadOnlyList<string> StepOrder =
        ["id", "kind", "label", "status", "assigned", "started", "finished", "by", "note"];

    /// <summary>_clean_step: the usual keys in the usual order, empty ones and _private ones left out.</summary>
    public static PyDict CleanStep(PyDict step)
    {
        var keep = new PyDict();
        foreach (var (key, value) in step)
        {
            if (key is string k && k.StartsWith('_'))
            {
                continue;
            }

            if (value is null or "")
            {
                continue;
            }

            keep[key] = value;
        }

        if (!keep.ContainsKey("status"))
        {
            keep["status"] = "pending";
        }

        var ordered = new PyDict();
        foreach (var key in StepOrder)
        {
            if (keep.TryGetValue(key, out var value))
            {
                ordered[key] = value;
            }
        }

        foreach (var (key, value) in keep)
        {
            if (!(key is string k && StepOrder.Contains(k)))
            {
                ordered[key] = value;
            }
        }

        return ordered;
    }

    /// <summary>write_steps: rewrites `steps:` one step per line. Comments inside the block go; those above stay.</summary>
    public static string WriteSteps(string raw, IEnumerable<PyDict> steps)
    {
        var lines = raw.Split('\n').ToList();
        var where = YamlText.Block(lines, "steps") ?? throw new EngineError("the file has no `steps:` section");
        YamlText.Replace(lines, where.Start, where.End,
            ["steps:", .. steps.Select(s => $"  - {YamlEmitter.Scalar(CleanStep(s))}")]);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// write_list: rewrites the top-level list `key:` one entry per line, or `key: []` when empty.
    /// Comments below the list (the templates' examples) stay.
    /// </summary>
    public static string WriteList(string raw, string key, IEnumerable<PyDict> items)
    {
        var lines = raw.Split('\n').ToList();
        var where = YamlText.Block(lines, key) ?? throw new EngineError($"the file has no `{key}:` section");
        var clean = items.Select(i => new PyDict(i.Where(p => p.Value is not (null or "")))).ToList();
        YamlText.Replace(lines, where.Start, where.End,
            clean.Count > 0 ? [$"{key}:", .. clean.Select(i => $"  - {YamlEmitter.Scalar(i)}")] : [$"{key}: []"]);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// set_list: write_list, first adding the list (with `comment` above it) after the item's other
    /// links when the file was written before the list existed.
    /// </summary>
    public static string SetList(string raw, string key, IEnumerable<PyDict> items, string comment)
    {
        var lines = raw.Split('\n').ToList();
        if (YamlText.Block(lines, key) is not null)
        {
            return WriteList(raw, key, items);
        }

        int? anchor = null;
        foreach (var other in (string[])["panorama", "notebooks"])
        {
            if (YamlText.Block(lines, other) is { } where)
            {
                var at = where.End;
                // The template's commented examples belong to the list above them.
                while (at < lines.Count && lines[at].StartsWith("#  ", StringComparison.Ordinal))
                {
                    at++;
                }

                anchor = at;
                break;
            }
        }

        string[] block = ["", $"# {comment}", $"{key}: []"];
        if (anchor is { } a)
        {
            lines.InsertRange(a, block);
        }
        else
        {
            lines = [.. Py.RStrip(raw, "\n").Split('\n'), .. block, ""];
        }

        return WriteList(string.Join('\n', lines), key, items);
    }

    /// <summary>set_wiki: sets the project's `wiki:` line, adding it with a comment at the end when it is new.</summary>
    public static string SetWiki(string raw, PyDict? value)
    {
        var lines = raw.Split('\n').ToList();
        var line = $"wiki: {YamlEmitter.Scalar(value)}";
        if (YamlText.Block(lines, "wiki") is { } where)
        {
            YamlText.Replace(lines, where.Start, where.End, [line]);
            return string.Join('\n', lines);
        }

        return Py.RStrip(raw, "\n") + "\n\n# The project's page on Panorama, which LabOps keeps up to date "
            + "(project.py link <project> wiki).\n" + line + "\n";
    }
}
