using System.Text.Json;
using LabOps.Engines;
using LabOps.Engines.Projects;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Tests.Engines;

/// <summary>
/// The line-based record edits against project.py's: set_fields, set_child, write_steps,
/// write_list, set_list, set_wiki and _block, on the templates and on synthetic records. Two of
/// project.py's edits had bugs the C# engine fixes; those cases are listed and checked on their own.
/// </summary>
public sealed class YamlEditTests
{
    private static readonly JsonElement Table = Golden.Table("yaml-edits.json");

    // Inputs where project.py's answer was wrong, with what the C# engine writes instead.
    private static readonly Dictionary<string, string> Fixed = new(StringComparer.Ordinal)
    {
        // A quoted value with " #" inside it: set_fields took part of the value for the comment.
        ["title: 'quoted # not a comment'  # real comment"] = "the comment is the one after the quotes",
        // A value with a # but no space before it: set_child missed the key and added it again.
        ["  type: a#b  # c"] = "the key is replaced in place",
    };

    private static bool IsFixedCase(string raw) => Fixed.Keys.Any(k => raw.Contains(k, StringComparison.Ordinal));

    private static void Compare(string name, Func<JsonElement, string> run)
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Table.GetProperty(name).EnumerateArray())
        {
            var args = c.GetProperty("args");
            if (IsFixedCase(args.GetProperty("raw").GetString()!))
            {
                continue;
            }

            string actual;
            try
            {
                actual = run(args);
            }
            catch (EngineError ex)
            {
                actual = $"<error: {ex.Message}>";
            }

            var expected = c.TryGetProperty("result", out var r) ? r.GetString()! : $"<error: {c.GetProperty("message").GetString()}>";
            mismatches.Check(actual == expected,
                () => $"{name} {args}\n  python: {JsonSerializer.Serialize(expected)}\n  c#:     {JsonSerializer.Serialize(actual)}");
        }

        mismatches.Checked.ShouldBeGreaterThanOrEqualTo(10);
        mismatches.ShouldBeNone();
    }

    private static PyDict Dict(JsonElement tagged) => (PyDict)Golden.Value(tagged)!;

    private static IEnumerable<PyDict> Dicts(JsonElement tagged) => ((List<object?>)Golden.Value(tagged)!).Cast<PyDict>();

    [Fact]
    public void Set_fields_matches() => Compare("set_fields", a => YamlText.SetFields(
        a.GetProperty("raw").GetString()!,
        Dict(a.GetProperty("fields")).Select(p => new KeyValuePair<string, object?>((string)p.Key!, p.Value)),
        a.GetProperty("after").GetString()!));

    [Fact]
    public void Set_child_matches() => Compare("set_child", a => YamlText.SetChild(
        a.GetProperty("raw").GetString()!, a.GetProperty("parent").GetString()!, a.GetProperty("key").GetString()!,
        Golden.Value(a.GetProperty("value"))));

    [Fact]
    public void Write_steps_matches() => Compare("write_steps", a => RecordEdits.WriteSteps(
        a.GetProperty("raw").GetString()!, Dicts(a.GetProperty("steps"))));

    [Fact]
    public void Write_list_matches() => Compare("write_list", a => RecordEdits.WriteList(
        a.GetProperty("raw").GetString()!, a.GetProperty("key").GetString()!, Dicts(a.GetProperty("items"))));

    [Fact]
    public void Set_list_matches() => Compare("set_list", a => RecordEdits.SetList(
        a.GetProperty("raw").GetString()!, a.GetProperty("key").GetString()!, Dicts(a.GetProperty("items")),
        a.GetProperty("comment").GetString()!));

    [Fact]
    public void Set_wiki_matches() => Compare("set_wiki", a => RecordEdits.SetWiki(
        a.GetProperty("raw").GetString()!, Golden.Value(a.GetProperty("value")) as PyDict));

    [Fact]
    public void Block_matches()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Table.GetProperty("block").EnumerateArray())
        {
            var lines = c.GetProperty("raw").GetString()!.Split('\n');
            var key = c.GetProperty("key").GetString()!;
            var result = c.GetProperty("result");
            var expected = result.ValueKind == JsonValueKind.Null ? null : ((int, int)?)(result[0].GetInt32(), result[1].GetInt32());
            var actual = YamlText.Block(lines, key);
            mismatches.Check(actual == expected, () => $"{key} in {JsonSerializer.Serialize(c.GetProperty("raw").GetString())}: python {expected}, c# {actual}");
        }

        mismatches.ShouldBeNone();
    }

    [Fact]
    public void A_hash_inside_quotes_stays_in_the_value()
    {
        var raw = "title: 'quoted # not a comment'  # real comment\nstatus: active\n";
        YamlText.SetFields(raw, [new("title", "New")]).ShouldBe("title: New  # real comment\nstatus: active\n");
    }

    [Fact]
    public void A_value_with_a_hash_and_no_space_is_replaced_not_duplicated()
    {
        var raw = "funding:\n  # leading comment\n  type: a#b  # c\n  grant: x\n";
        YamlText.SetChild(raw, "funding", "type", "quote").ShouldBe("funding:\n  # leading comment\n  type: quote  # c\n  grant: x\n");
    }

    [Fact]
    public void A_comment_after_a_flow_list_that_continues_is_kept()
    {
        var raw = "title: [a,  # first\n  b]\nstatus: active\n";
        YamlText.SetFields(raw, [new("title", "T")]).ShouldBe("title: T  # first\nstatus: active\n");
    }
}
