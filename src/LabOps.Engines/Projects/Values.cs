using System.Numerics;
using System.Text.Json.Nodes;
using LabOps.Engines.Python;

namespace LabOps.Engines.Projects;

/// <summary>
/// Turning a record's loosely written values into the fixed shapes the JSON promises: text fields
/// are strings or null, numbers are numbers or null, and links are always the same objects,
/// however someone wrote the YAML. Ported from project.py's _text, _int, _entry, _links and the
/// *_json helpers.
/// </summary>
public static class Values
{
    public static readonly string[] NotebookFields = ["id", "url"];
    public static readonly string[] PanoramaFields = ["folder", "kind"];
    public static readonly string[] ProtocolFields = ["id", "version", "title", "step"];

    /// <summary>_text: null for null or "", a date as YYYY-MM-DD, anything else as Python's str().</summary>
    public static string? Text(object? v) => v switch
    {
        null or "" => null,
        DateOnly d => PyText.Str(d),
        PyDateTime dt => dt.IsoFormat('T'),
        _ => PyText.Str(v),
    };

    /// <summary>_int: the number, or null when int() would refuse it.</summary>
    public static object? Int(object? v) => PyText.ToInt(v);

    /// <summary>What `for x in v` walks: a list's items, a string's characters, a mapping's keys.</summary>
    public static IEnumerable<object?> Iterate(object? v) => v switch
    {
        null => [],
        List<object?> list => list,
        string s => s.EnumerateRunes().Select(r => (object?)r.ToString()),
        PyDict d => d.Keys,
        // Python would stop with a TypeError here; one value is the sensible reading.
        _ => [v],
    };

    /// <summary>
    /// _entry: a list entry as a mapping. A bare string fills the first field, and the url too when
    /// it is a link, so `- https://...` under notebooks reads as {id: ..., url: ...}.
    /// </summary>
    public static PyDict Entry(object? item, string[] fields)
    {
        if (item is PyDict d)
        {
            return d.Copy();
        }

        var s = PyText.Str(item);
        var entry = new PyDict();
        foreach (var f in fields)
        {
            if (f == fields[0] || (f == "url" && s.StartsWith("http", StringComparison.Ordinal)))
            {
                entry[f] = s;
            }
        }

        return entry;
    }

    /// <summary>_links: a list of mappings with exactly `fields`, as text.</summary>
    public static List<Dictionary<string, string?>> Links(object? items, string[] fields) =>
        [.. (items is List<object?> list ? list : []).Where(i => i is not null).Select(i => Entry(i, fields))
            .Select(e => fields.ToDictionary(f => f, f => Text(e[f])))];

    /// <summary>_list_of: the record's `key:` list as mappings to edit, each keeping every field it has.</summary>
    public static List<PyDict> ListOf(PyDict d, string key, string[] fields) =>
        d[key] is List<object?> items ? [.. items.Where(i => i is not null).Select(i => Entry(i, fields))] : [];

    public sealed record Protocol(string? Id, object? Version, string? Title, string? Step);

    /// <summary>_protocols_json: the protocols an item used, each at its version and for one of its steps.</summary>
    public static List<Protocol> Protocols(PyDict d) =>
        [.. ListOf(d, "protocols", ProtocolFields).Select(e => new Protocol(Text(e["id"]), Int(e["version"]), Text(e["title"]), Text(e["step"])))];

    public sealed record Funding(string? Type, List<string> Quotes, string? Grant);

    /// <summary>_funding_json.</summary>
    public static Funding FundingOf(object? funding)
    {
        var f = funding as PyDict ?? [];
        var quotes = f["quotes"];
        return new Funding(Text(f["type"]),
            [.. (PyText.Truthy(quotes) ? Iterate(quotes) : []).Select(PyText.Str)], Text(f["grant"]));
    }

    // ---------------------------------------------------------------- JSON

    public static JsonNode? Json(string? s) => s is null ? null : JsonValue.Create(s);

    public static JsonNode? JsonInt(object? v) => v switch
    {
        null => null,
        long l => JsonValue.Create(l),
        BigInteger big => JsonNode.Parse(big.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException($"not an int: {v}"),
    };

    public static JsonArray Json(IEnumerable<Dictionary<string, string?>> links) =>
        [.. links.Select(l => (JsonNode?)new JsonObject(l.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Json(p.Value)))))];

    public static JsonObject Json(Funding f) => new()
    {
        ["type"] = Json(f.Type),
        ["quotes"] = new JsonArray([.. f.Quotes.Select(q => (JsonNode?)JsonValue.Create(q))]),
        ["grant"] = Json(f.Grant),
    };

    public static JsonArray Json(IEnumerable<Protocol> protocols) =>
        [.. protocols.Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = Json(p.Id), ["version"] = JsonInt(p.Version), ["title"] = Json(p.Title), ["step"] = Json(p.Step),
        })];

    /// <summary>_analysis_json.</summary>
    public static JsonObject Analysis(PyDict d)
    {
        var analysis = d["analysis"] as PyDict ?? [];
        return new JsonObject { ["repo"] = Json(Text(analysis["repo"])), ["folder"] = Json(Text(analysis["folder"])) };
    }

    public static JsonArray Issues(IEnumerable<(string Level, string Message)> issues) =>
        [.. issues.Select(i => (JsonNode?)new JsonObject { ["level"] = i.Level, ["message"] = i.Message })];
}
