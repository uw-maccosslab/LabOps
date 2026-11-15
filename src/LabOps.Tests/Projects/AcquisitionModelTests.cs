using LabOps.Core.Acquisition;
using LabOps.Core.Panorama;
using LabOps.Core.Projects;
using LabOps.Tests.Engines.Commands;

namespace LabOps.Tests.Projects;

/// <summary>The app's side of Read raw files: what it hands the engine, and what it reads back.</summary>
public sealed class AcquisitionModelTests
{
    [Fact]
    public async Task What_the_raw_files_said_comes_back_on_the_experiment()
    {
        using var repo = TestRepo.Create();
        File.WriteAllText(Path.Combine(repo.Root, "config", "instruments.yaml"), "instruments:\n  - name: Orbitrap Astral\n    serials: [OA10308]\n");
        repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        var engine = new ProjectEngine { RepositoryPath = repo.Root };
        var instrument = new RawInstrument(null, "Orbitrap Astral Zoom", "OA10308", "2.0");
        var read = new RawFolderRead(
        [
            new RawFileFacts("/MacCoss/Lab/@files/RawFiles", "S-001.raw", new DateTime(2026, 10, 1, 9, 0, 0), "DIA.meth", "aaaaaaaaaaaa", instrument),
            new RawFileFacts("/MacCoss/Lab/@files/RawFiles", "S-002.raw", new DateTime(2026, 10, 3, 9, 0, 0), "DIA.meth", "aaaaaaaaaaaa", instrument),
        ], []);

        await engine.RecordAcquisitionAsync("2026-10-Pilot-DIA", read, "maccoss", TestContext.Current.CancellationToken);
        var list = await engine.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        var experiment = list.Labs.SelectMany(l => l.Projects).SelectMany(p => p.Experiments).Single();
        experiment.Instrument.ShouldBe("Orbitrap Astral");
        experiment.Acquisition.ShouldBe([new AcquisitionRead("/MacCoss/Lab/@files/RawFiles", "DIA.meth", "Orbitrap Astral Zoom", "OA10308", 2, "2026-10-01", "2026-10-03", "aaaaaaaaaaaa")]);
        var step = experiment.Stages.Single(s => s.Stage == "data_acquisition");
        (step.Status, step.Started, step.Finished, step.By).ShouldBe(("done", "2026-10-01", "2026-10-03", "maccoss"));
        // The input file is gone once the engine has read it.
        Directory.GetFiles(Path.GetTempPath(), "labops-raw-files-*.json").ShouldBeEmpty();
    }
}
