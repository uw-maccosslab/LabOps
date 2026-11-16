using System.Text.Json;
using LabOps.Core.Acquisition;
using LabOps.Core.Engines;
using LabOps.Core.Projects;
using LabOps.Core.Protocols;

namespace LabOps.Tests.Acquisition;

/// <summary>
/// Matching raw files to the LC-MS protocol they followed: the method taken out of a raw file is
/// a .meth file that fingerprints like the raw file, a published version's .meth is found by that
/// fingerprint, and a method no version holds is written up as settings for Claude.
/// </summary>
public sealed class MethodProtocolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"labops-methods-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "method-text", name));

    private static (byte[] File, RawLayout Layout) Raw(string msText)
    {
        var file = RawFileTests.MakeRawFile(new DateTime(2026, 10, 1, 9, 0, 0), lcText: Fixture("neo-trap-elute.lc.txt"), msText: msText);
        return (file, ThermoRawHeader.ReadLayout(file.AsSpan(0, ThermoRawHeader.StartLength), file.Length));
    }

    /// <summary>A protocol whose published versions hold these method files, written under the scratch repository.</summary>
    private ProtocolSummary Protocol(string id, string status, params (int Version, string Name, byte[] Method)[] versions)
    {
        var folder = Path.Combine(_root, "protocols", id, "methods");
        Directory.CreateDirectory(folder);
        foreach (var v in versions)
        {
            File.WriteAllBytes(Path.Combine(folder, v.Name), v.Method);
        }

        return new ProtocolSummary
        {
            Id = id,
            Folder = $"protocols/{id}",
            Title = $"Title of {id}",
            Status = status,
            Versions = [.. versions.Select(v => new ProtocolVersion(v.Version, "2026-10-01", "maccoss", "x", false) { Methods = [v.Name, "program.bdz"] })],
        };
    }

    [Fact]
    public void The_method_taken_out_of_a_raw_file_is_a_meth_file_that_fingerprints_alike()
    {
        var (file, layout) = Raw(Fixture("astral-zoom-dia.ms.txt"));

        var method = ThermoRawHeader.ExtractMethod(file, layout).ShouldNotBeNull();

        // A compound file of its own, with the same devices' text as the raw file holds.
        method.AsSpan(0, 8).ToArray().ShouldBe(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
        ThermoRawHeader.Fingerprint(ThermoRawHeader.ReadMethodFileText(method))
            .ShouldBe(ThermoRawHeader.Fingerprint(ThermoRawHeader.ReadMethods(file, layout)));
    }

    [Fact]
    public void A_published_versions_method_is_found_by_the_raw_files_fingerprint()
    {
        var (dia, diaLayout) = Raw(Fixture("astral-zoom-dia.ms.txt"));
        var (prm, prmLayout) = Raw(Fixture("stellar-prm-synthetic.ms.txt"));
        var diaMethod = ThermoRawHeader.ExtractMethod(dia, diaLayout)!;
        var protocols = new[]
        {
            Protocol("astral-dia-24m", "active", (1, "Astral_DIA_24m.meth", diaMethod), (2, "Astral_DIA_24m-2.meth", diaMethod)),
            Protocol("old-astral-dia", "retired", (3, "dia.meth", diaMethod)),
            // Not a Thermo method: left out, not an error.
            Protocol("broken", "active", (1, "broken.meth", [1, 2, 3])),
        };

        var index = ProtocolMethodIndex.Build(_root, protocols);

        var match = index.Find(ThermoRawHeader.Fingerprint(ThermoRawHeader.ReadMethods(dia, diaLayout))).ShouldNotBeNull();
        // The newest version of a protocol in use, before a retired one.
        (match.Protocol.Id, match.Version, match.MethodFile).ShouldBe(("astral-dia-24m", 2, "Astral_DIA_24m-2.meth"));
        // Any other setting is another method: no match, and so a protocol to write.
        index.Find(ThermoRawHeader.Fingerprint(ThermoRawHeader.ReadMethods(prm, prmLayout))).ShouldBeNull();
        index.Find(null).ShouldBeNull();
        index.Count.ShouldBe(1);
    }

    [Fact]
    public void Publishing_records_the_version_on_experiments_acquired_with_its_method_that_do_not_record_it_yet()
    {
        var (dia, layout) = Raw(Fixture("astral-zoom-dia.ms.txt"));
        var fingerprint = ThermoRawHeader.Fingerprint(ThermoRawHeader.ReadMethods(dia, layout));
        var index = ProtocolMethodIndex.Build(_root, [Protocol("astral-dia-24m", "active", (1, "Astral_DIA_24m.meth", ThermoRawHeader.ExtractMethod(dia, layout)!))]);
        ExperimentSummary Experiment(string name, string? method, bool recorded = false, bool acquisitionStep = true) => new()
        {
            Experiment = name,
            Acquisition = method is null ? [] : [new AcquisitionRead("/x/RawFiles", "DIA.meth", "Orbitrap Astral Zoom", "OA10308", 3, "2026-10-01", "2026-10-03", method)],
            Protocols = recorded ? [new ProtocolLink("astral-dia-24m", 1, "Title", "data_acquisition")] : [],
            Stages = acquisitionStep
                ? [new StageEntry { Stage = "sample_prep", Kind = "sample_prep" }, new StageEntry { Stage = "data_acquisition_batch1", Kind = "data_acquisition" }]
                : [new StageEntry { Stage = "sample_prep", Kind = "sample_prep" }],
        };
        var project = new ProjectSummary
        {
            Project = "Pilot",
            Folder = "projects/Lab/Pilot",
            Experiments =
            [
                Experiment("2026-10-Pilot-DIA", fingerprint),
                Experiment("2026-10-Recorded-DIA", fingerprint, recorded: true),
                Experiment("2026-10-Other-DIA", "000000000000"),
                Experiment("2026-10-Unread-DIA", null),
                Experiment("2026-10-NoStep-DIA", fingerprint, acquisitionStep: false),
            ],
        };
        var list = new ProjectList([new LabSummary { Lab = "Lab", Projects = [project] }], [], []);

        var pending = index.ToRecord(list, "astral-dia-24m", 1);

        pending.Select(r => (r.Experiment.Experiment, r.Step)).ShouldBe([("2026-10-Pilot-DIA", "data_acquisition_batch1"), ("2026-10-NoStep-DIA", (string?)null)]);
        pending.ShouldAllBe(r => r.Project.Folder == "projects/Lab/Pilot");
        // Another protocol, or another version, records nothing.
        index.ToRecord(list, "other-protocol", 1).ShouldBeEmpty();
        index.ToRecord(list, "astral-dia-24m", 2).ShouldBeEmpty();
    }

    [Fact]
    public void A_versions_method_files_come_from_the_protocol_engines_list()
    {
        const string json = """{"version": 2, "date": "2026-10-10", "by": "maccoss", "summary": "x", "imported": false, "methods": ["a.meth", "b.bdz"]}""";

        var version = JsonSerializer.Deserialize<ProtocolVersion>(json, EngineJson.Options)!;

        version.Methods.ShouldBe(["a.meth", "b.bdz"]);
        // An older engine lists none.
        JsonSerializer.Deserialize<ProtocolVersion>("""{"version": 1, "imported": false}""", EngineJson.Options)!.Methods.ShouldBeEmpty();
    }

    [Fact]
    public void The_settings_for_Claude_hold_the_LC_the_MS_and_every_mass_list_row_but_not_who_saved_it()
    {
        var (file, layout) = Raw(Fixture("stellar-prm-synthetic.ms.txt"));
        var texts = ThermoRawHeader.ReadMethods(file, layout);

        var md = MethodSummary.ToMarkdown(texts, "Stellar_PRM.meth", "Stellar");

        md.ShouldStartWith("# Instrument method: Stellar_PRM.meth\n\nRead by LabOps from the method embedded in the raw files (Stellar).");
        md.ShouldContain("| LC system | VanquishNeo |");
        md.ShouldContain("| Analytical column | 150 µm inner diameter × 15 cm");
        md.ShouldContain("| Time (min) | %B | Flow (µL/min) |\n|---|---|---|\n| 0 | 4 | 1.3 |");
        md.ShouldContain("The longest gradient segment runs from 6.5 to 40 %B over 20 min (1 to 21 min).");
        md.ShouldContain("## Mass spectrometer: Stellar Method Summary");
        md.ShouldContain("| Cycle Time (sec) | 1.2 |");
        md.ShouldContain("Mass list (3 rows):");
        md.ShouldContain("| PEPTIDECK+3 |");
        md.ShouldContain($"Method fingerprint: {ThermoRawHeader.Fingerprint(texts)}.");
        md.ShouldNotContain("Creator:");
        md.ShouldNotContain("INSTRUMENT-PC");
    }

    [Fact]
    public void A_method_is_written_for_import_under_its_own_name()
    {
        var (file, layout) = Raw(Fixture("astral-zoom-dia.ms.txt"));
        var capture = new RawMethodCapture(ThermoRawHeader.ExtractMethod(file, layout)!, ThermoRawHeader.ReadMethods(file, layout), @"C:\Xcalibur\methods\Astral DIA 24m.meth");

        var draft = MethodDraft.Write(capture, null, "Orbitrap Astral Zoom");
        try
        {
            draft.MethodFile.ShouldBe("Astral DIA 24m.meth");
            File.ReadAllBytes(draft.MethodPath).ShouldBe(capture.Method);
            Path.GetFileName(draft.SettingsPath).ShouldBe("Astral DIA 24m-settings.md");
            File.ReadAllText(draft.SettingsPath).ShouldContain("Orbitrap Astral Zoom Method Summary");
        }
        finally
        {
            draft.Delete();
        }

        Directory.Exists(draft.Folder).ShouldBeFalse();
        var unnamed = MethodDraft.Write(capture with { MethodFile = null }, null, null);
        unnamed.MethodFile.ShouldBe("method.meth");
        unnamed.Delete();
    }
}
