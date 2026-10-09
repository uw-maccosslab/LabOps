using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LabOps.Engines.Projects;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// The Octopus round trip: samples.csv to Octopus input, and an exported layout back in. Ported
/// from LabOps-Projects' tests/test_octopus.py.
/// </summary>
public sealed class OctopusCommandTests
{
    private static readonly string[] Columns = ["QC", "Sample_Group", "Client search name", "age", "sex", "treatment", "batch"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly UTF8Encoding Utf8 = new(false);

    [Fact]
    public void Octopus_input_puts_sample_id_first_and_hides_other_id_columns()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var ids = OctopusExample(folder);
        var answer = repo.Ok("octopus-input", Path.GetFileName(folder));
        var file = answer["file"]!.GetValue<string>();
        var rows = ReadCsv(file);
        rows[0].ShouldBe(["Sample_ID", "QC", "Sample_Group", "Client search name", "age", "sex", "treatment", "batch"]);
        rows.Skip(1).Select(r => r[0]).ShouldBe(ids);
        answer["warnings"]!.AsArray().Select(w => w!.GetValue<string>())
            .ShouldContain(w => w.Contains("Client search name", StringComparison.Ordinal));
        file.Replace('\\', '/').ShouldEndWith("inbox/Test-Project/octopus-input.csv", Case.Sensitive);
    }

    [Fact]
    public void Octopus_input_refuses_ids_that_cannot_be_file_names()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var name = Path.GetFileName(folder);
        foreach (var bad in new[] { "S,1", "S;1", "S/1", "S:1", "S1." })
        {
            TestRepo.Samples(folder, [["Sample_ID", "QC"], [bad, "FALSE"], ["S2", "FALSE"]]);
            Error(repo.Fails("octopus-input", name)).ShouldContain("cannot become raw file names", Case.Sensitive, bad);
        }

        TestRepo.Samples(folder, [["Sample_ID", "QC", "Group"], ["S1", "FALSE", "a\nb"]]);
        Error(repo.Fails("octopus-input", name)).ShouldContain("line breaks", Case.Sensitive);
        TestRepo.Samples(folder, [["Sample_ID", "QC"], ["S1", "yes"]]);
        Error(repo.Fails("octopus-input", name)).ShouldContain("QC must be TRUE or FALSE", Case.Sensitive);
        TestRepo.Samples(folder, [["Sample_ID", "QC"], ["S1", "FALSE"], ["S1", "TRUE"]]);
        Error(repo.Fails("octopus-input", name)).ShouldContain("repeats Sample_ID S1", Case.Sensitive);
    }

    [Fact]
    public void Import_layout_keeps_the_layout_and_its_exact_input()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var (layout, _) = ImportExample(repo, folder, perPlate: 6);
        var answer = repo.Ok("import-layout", Path.GetFileName(folder), layout, "--by", "maccoss");

        ShouldBePy(ReadJson(Path.Combine(folder, "layout", "octopus-layout.json")), ReadJson(layout));
        File.ReadAllText(Path.Combine(folder, "layout", "octopus-input.csv"), Encoding.UTF8)
            .ShouldBe(File.ReadAllText(Path.Combine(repo.Root, "inbox", Path.GetFileName(folder), "octopus-input.csv"), Encoding.UTF8));
        var e = TestRepo.Yaml(folder);
        var steps = TestRepo.Steps(folder);
        ShouldBePy(e["layout"], new PyDict
        {
            ["plates"] = 2, ["samples"] = 10, ["imported"] = TestRepo.Today, ["id_column"] = "Sample_ID",
            ["covariates"] = new List<object?> { "QC" }, ["qc_column"] = "QC", ["subject_column"] = null, ["octopus_version"] = "1.4.0",
        });
        ShouldBePy(steps["plate_layout"], new PyDict
        {
            ["status"] = "done", ["started"] = TestRepo.Today, ["finished"] = TestRepo.Today, ["by"] = "maccoss",
            ["note"] = "Octopus layout: 2 plate(s), 10 samples",
        });
        ShouldBePy(FromJson(answer["project"]!["files"]), new PyDict { ["samples"] = true, ["layout"] = true, ["wiki"] = false });
        TestRepo.Raw(folder).ShouldContain("# The samples' timeline", Case.Sensitive);

        // The committed layout passes the pre-commit check.
        repo.Git("add", "-A");
        repo.Problems("--staged").Where(p => p.Level == "ERROR").Select(p => p.Message).ShouldBeEmpty();
    }

    [Fact]
    public void Import_layout_refuses_a_layout_that_does_not_match()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var name = Path.GetFileName(folder);
        var (layout, ids) = ImportExample(repo, folder);
        var doc = JsonNode.Parse(File.ReadAllText(layout, Encoding.UTF8))!.AsObject();

        var wrong = doc.DeepClone().AsObject();
        var samples = wrong["samples"]!.AsArray();
        samples.RemoveAt(samples.Count - 1);
        File.WriteAllText(layout, wrong.ToJsonString(), Utf8);
        Error(repo.Fails("import-layout", name, layout)).ShouldContain($"missing {ids[^1]}", Case.Sensitive);

        var newer = doc.DeepClone().AsObject();
        newer["schemaVersion"] = 2;
        File.WriteAllText(layout, newer.ToJsonString(), Utf8);
        Error(repo.Fails("import-layout", name, layout)).ShouldContain("schema version 2", Case.Sensitive);

        File.WriteAllText(layout, """{"samples": []}""", Utf8);
        Error(repo.Fails("import-layout", name, layout)).ShouldContain("not an Octopus layout", Case.Sensitive);

        File.WriteAllText(layout, doc.ToJsonString(), Utf8);
        var other = Path.Combine(repo.Root, "inbox", "other.csv");
        TestRepo.WriteCsv(other, [["Sample_ID", "QC"], ["X1", "FALSE"]]);
        Error(repo.Fails("import-layout", name, layout, "--input", other)).ShouldContain("is not the input for this layout", Case.Sensitive);
        ShouldBePy(TestRepo.Steps(folder)["plate_layout"], new PyDict { ["status"] = "pending" });
    }

    [Fact]
    public void A_new_layout_is_refused_once_sample_prep_has_started()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var name = Path.GetFileName(folder);
        var (layout, _) = ImportExample(repo, folder);
        repo.Ok("import-layout", name, layout);
        repo.Ok("import-layout", name, layout); // still allowed: nothing has been prepared yet
        repo.Ok("stage", name, "sample_prep", "start");
        Error(repo.Fails("import-layout", name, layout)).ShouldContain("sample prep has started", Case.Sensitive);
    }

    [Fact]
    public void Sample_ids_that_differ_only_in_capitals_are_refused()
    {
        // A Sample_ID becomes a raw file name, and Windows does not tell S1 from s1.
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        TestRepo.Samples(folder, [["Sample_ID", "QC"], ["S1", "FALSE"], ["s1", "FALSE"], ["S2", "FALSE"]]);
        Error(repo.Fails("octopus-input", Path.GetFileName(folder))).ShouldContain("differ only in capitals (S1 and s1)", Case.Sensitive);
        repo.Problems().Where(p => p.Level == "ERROR").Select(p => p.Message)
            .ShouldContain(m => m.Contains("differ only in capitals", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_layout_records_the_layout_when_the_project_has_no_expected_samples_line()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        TestRepo.WriteRaw(folder, string.Join('\n',
            TestRepo.Raw(folder).Split('\n').Where(l => !l.StartsWith("expected_samples:", StringComparison.Ordinal))));
        var (layout, ids) = ImportExample(repo, folder);
        repo.Ok("import-layout", Path.GetFileName(folder), layout);
        ShouldBePy(((PyDict)TestRepo.Yaml(folder)["layout"]!)["samples"], ids.Count);
        TestRepo.Steps(folder)["plate_layout"]["status"].ShouldBe("done");
    }

    [Fact]
    public void A_new_layout_replaces_a_layout_record_written_as_a_block()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var name = Path.GetFileName(folder);
        var (layout, ids) = ImportExample(repo, folder);
        repo.Ok("import-layout", name, layout);
        var raw = TestRepo.Raw(folder);
        var line = raw.Split('\n').First(l => l.StartsWith("layout:", StringComparison.Ordinal));
        // json.dumps(v, default=str) of each value: a date becomes a quoted string.
        var block = "layout:\n" + string.Concat(((PyDict)TestRepo.Yaml(folder)["layout"]!).Select(p => $"  {p.Key}: {PyJson.Dumps(p.Value)}\n"));
        TestRepo.WriteRaw(folder, raw.Replace(line + "\n", block, StringComparison.Ordinal)); // tidied by hand: still valid YAML
        ShouldBePy(((PyDict)TestRepo.Yaml(folder)["layout"]!)["samples"], ids.Count);
        repo.Ok("import-layout", name, layout);
        ShouldBePy(((PyDict)TestRepo.Yaml(folder)["layout"]!)["imported"], TestRepo.Today);
    }

    [Fact]
    public void Import_layout_keeps_an_input_saved_by_excel()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var (layout, _) = ImportExample(repo, folder);
        var source = Path.Combine(repo.Root, "inbox", Path.GetFileName(folder), "octopus-input.csv");
        var text = ReplaceFirst(File.ReadAllText(source, Encoding.UTF8), ",batch\n", ",batch (µg)\n"); // saved again by Excel
        // Excel's Windows-1252, which for these characters (ASCII and the micro sign, 0xB5) is the same as Latin-1.
        File.WriteAllBytes(source, Encoding.Latin1.GetBytes(text.Replace("\n", "\r\n", StringComparison.Ordinal)));
        repo.Ok("import-layout", Path.GetFileName(folder), layout);
        File.ReadAllText(Path.Combine(folder, "layout", "octopus-input.csv"), Encoding.UTF8).ShouldBe(text);
    }

    // -- helpers --------------------------------------------------------------------------------

    /// <summary>samples.csv built from Octopus's own example, keeping its `search name` column.</summary>
    private static List<string> OctopusExample(string folder)
    {
        var rows = CsvReader.Parse(File.ReadAllText(TestRepo.OctopusData("simple-metadata.csv"), Encoding.UTF8))
            .Select(r => r.Select(Py.Strip).ToList()).ToList();
        var (header, body) = (rows[0], rows.Skip(1).ToList());
        TestRepo.Samples(folder, [
            ["Sample_ID", "QC", "Sample_Group", .. header],
            .. body.Select(List<object?> (r) => [r[0].ToUpperInvariant(), "FALSE", r[3], .. r]),
        ]);
        return [.. body.Select(r => r[0].ToUpperInvariant())];
    }

    /// <summary>A CSV as Python's csv.reader reads it.</summary>
    private static List<List<string>> ReadCsv(string path) => [.. CsvReader.Parse(File.ReadAllText(path, Encoding.UTF8))];

    private static (string Layout, List<string> Ids) ImportExample(TestRepo repo, string folder, int perPlate = 96)
    {
        var ids = OctopusExample(folder);
        repo.Ok("octopus-input", Path.GetFileName(folder));
        var rows = ReadCsv(Path.Combine(repo.Root, "inbox", Path.GetFileName(folder), "octopus-input.csv"));
        var inputs = new Dictionary<string, Dictionary<string, string>>();
        foreach (var r in rows.Skip(1))
        {
            var values = new Dictionary<string, string>();
            foreach (var (column, value) in rows[0].Zip(r))
            {
                values[column] = value;
            }

            inputs[r[0]] = values;
        }

        var layout = Path.Combine(repo.Root, "inbox", Path.GetFileName(folder), "layout.json");
        File.WriteAllText(layout, TestRepo.OctopusLayout(ids, inputs, Columns, perPlate: perPlate).ToJsonString(Indented), Utf8);
        return (layout, ids);
    }

    private static object? ReadJson(string path) => PyJsonDecoder.Loads(File.ReadAllText(path, Encoding.UTF8));

    private static string Error(JsonObject answer) => answer["error"]!.GetValue<string>();

    /// <summary>A command's JSON as Python's json.loads reads it, to compare with Python's ==.</summary>
    private static object? FromJson(JsonNode? node) => node is null ? null : PyJsonDecoder.Loads(node.ToJsonString());

    private static void ShouldBePy(object? actual, object? expected) =>
        PyText.Eq(actual, expected).ShouldBeTrue($"{PyText.Repr(actual)}\nshould be\n{PyText.Repr(expected)}");

    /// <summary>Python's str.replace(old, new, 1).</summary>
    private static string ReplaceFirst(string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + replacement + text[(at + old.Length)..];
    }
}
