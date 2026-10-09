using LabOps.Engines.Projects;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// The line-based edits that let commands change the YAML files without losing their comments.
/// From LabOps-Projects' tests/test_yaml_edits.py; YamlEditTests compares the same edits with
/// project.py's answers on a table of inputs, and these check what each edit is for.
/// </summary>
public sealed class RecordEditTests
{
    private static PyDict Load(string text) => (PyDict)YamlLoader.Load(text)!;

    private static List<PyDict> StepsOf(string text) => [.. ((List<object?>)Load(text)["steps"]!).Cast<PyDict>()];

    private static void ShouldBePy(object? actual, object? expected) =>
        PyText.Eq(actual, expected).ShouldBeTrue($"{PyText.Repr(actual)} should be {PyText.Repr(expected)}");

    [Fact]
    public void Set_fields_keeps_trailing_comments_and_adds_missing_keys()
    {
        var raw = "title: Old  # the title\nstatus: active            # active | on_hold | closed\nnotes: []\n";
        var output = YamlText.SetFields(raw, [new("status", "on_hold"), new("series", "otter-survey")]);
        output.ShouldContain("status: on_hold            # active | on_hold | closed", Case.Sensitive);
        output.ShouldContain("series: otter-survey", Case.Sensitive);
        output.IndexOf("series: otter-survey", StringComparison.Ordinal).ShouldBeGreaterThan(output.IndexOf("status:", StringComparison.Ordinal));
        ShouldBePy(YamlLoader.Load(output),
            new PyDict { ["title"] = "Old", ["status"] = "on_hold", ["series"] = "otter-survey", ["notes"] = new List<object?>() });
    }

    [Fact]
    public void Set_child_edits_one_key_in_a_block_and_keeps_its_comment()
    {
        var raw = "funding:\n  type: internal          # quote | grant | internal\n  quotes: []              # numbers\n"
            + "  grant: null\n\nhuman: false\n";
        var output = YamlText.SetChild(raw, "funding", "type", "quote");
        output = YamlText.SetChild(output, "funding", "quotes", new List<object?> { "MacCoss-2026-NWU-SC" });
        output.ShouldContain("  type: quote          # quote | grant | internal", Case.Sensitive);
        output.ShouldContain("  quotes: [MacCoss-2026-NWU-SC]              # numbers", Case.Sensitive);
        ShouldBePy(Load(output)["funding"],
            new PyDict { ["type"] = "quote", ["quotes"] = new List<object?> { "MacCoss-2026-NWU-SC" }, ["grant"] = null });
        Load(output)["human"].ShouldBeOfType<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Write_steps_writes_one_line_per_step_and_keeps_the_comments_above()
    {
        var raw = "# The timeline.\nsteps:\n  - {id: samples_received, kind: samples_received, status: pending}\n"
            + "  - {id: plate_layout, kind: plate_layout, status: pending}\n\nnotes: []\n";
        var steps = StepsOf(raw);
        steps[0]["status"] = "done";
        steps[0]["started"] = new DateOnly(2026, 10, 1);
        steps[0]["finished"] = new DateOnly(2026, 10, 2);
        steps[0]["note"] = "92 tubes, 2 hemolyzed: see manifest";
        var output = RecordEdits.WriteSteps(raw, steps);
        var lines = output.Split('\n');
        lines[0].ShouldBe("# The timeline.");
        lines[1].ShouldBe("steps:");
        lines[2].ShouldStartWith("  - {id: samples_received, kind: samples_received, status: done, started: 2026-10-01", Case.Sensitive);
        var loaded = Load(output);
        ((PyDict)((List<object?>)loaded["steps"]!)[0]!)["note"].ShouldBe("92 tubes, 2 hemolyzed: see manifest");
        ShouldBePy(loaded["notes"], new List<object?>());
    }

    [Fact]
    public void Write_steps_replaces_steps_someone_wrote_in_block_style()
    {
        var raw = "steps:\n- id: samples_received\n  kind: samples_received\n  status: in_progress\n"
            + "- {id: plate_layout, kind: plate_layout}\nnotes: []\n";
        var steps = StepsOf(raw);
        steps[0]["status"] = "done";
        var output = RecordEdits.WriteSteps(raw, steps);
        ShouldBePy(YamlLoader.Load(output), new PyDict
        {
            ["steps"] = new List<object?>
            {
                new PyDict { ["id"] = "samples_received", ["kind"] = "samples_received", ["status"] = "done" },
                new PyDict { ["id"] = "plate_layout", ["kind"] = "plate_layout", ["status"] = "pending" },
            },
            ["notes"] = new List<object?>(),
        });
    }

    [Fact]
    public void A_comment_between_steps_does_not_duplicate_them()
    {
        var raw = "steps:\n  - {id: a, kind: other, label: A}\n# the rest wait for the second shipment\n"
            + "  - {id: b, kind: other, label: B}\n\n# Notes about the project.\nnotes: []\n";
        var steps = StepsOf(raw);
        steps[0]["status"] = "done";
        var output = RecordEdits.WriteSteps(raw, steps);
        StepsOf(output).Select(s => (string?)s["id"]).ToList().ShouldBe(new List<string?> { "a", "b" });
        output.ShouldContain("\n\n# Notes about the project.\nnotes: []\n", Case.Sensitive);
    }

    [Fact]
    public void Set_fields_replaces_a_value_written_as_a_block()
    {
        var raw = "status: active\nlayout:   # from Octopus\n  plates: 1\n  samples: 80\nsteps: []\n";
        var output = YamlText.SetFields(raw, [new("layout", new PyDict { ["plates"] = 2L, ["samples"] = 160L })]);
        output.ShouldContain("layout: {plates: 2, samples: 160}   # from Octopus\nsteps: []", Case.Sensitive);
        ShouldBePy(YamlLoader.Load(output), new PyDict
        {
            ["status"] = "active",
            ["layout"] = new PyDict { ["plates"] = 2L, ["samples"] = 160L },
            ["steps"] = new List<object?>(),
        });
    }

    [Fact]
    public void Set_fields_adds_a_key_at_the_end_when_there_is_nothing_to_place_it_after()
    {
        var output = YamlText.SetFields("project: X\nsteps: []\n", [new("layout", new PyDict { ["plates"] = 1L })], after: "expected_samples");
        output.ShouldBe("project: X\nsteps: []\nlayout: {plates: 1}\n");
    }
}
