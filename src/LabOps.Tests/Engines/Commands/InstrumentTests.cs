using System.Text;

namespace LabOps.Tests.Engines.Commands;

/// <summary>config/instruments.yaml: the lab's instruments, which experiments name.</summary>
public sealed class InstrumentTests
{
    private static void WriteInstruments(TestRepo repo, string text) =>
        File.WriteAllText(Path.Combine(repo.Root, "config", "instruments.yaml"), text, new UTF8Encoding(false));

    [Fact]
    public void An_experiment_on_an_instrument_the_lab_does_not_list_is_a_warning()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "instruments:\n  - name: Orbitrap Astral\n  - name: Stellar\n");
        repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project", "--instrument", "orbitrap astral");
        repo.NewExperiment("2026-11-Pilot-PRM", "Pilot-Project", "--instrument", "Stellar 2");

        var problems = repo.Problems();

        problems.ShouldHaveSingleItem().ShouldBe(("WARN",
            "2026-11-Pilot-PRM: instrument 'Stellar 2' is not in config/instruments.yaml "
            + "(Orbitrap Astral, Stellar); use one of those names, or add it there"));
        repo.Ok("list")["instruments"]!.AsArray().Select(i => i!.GetValue<string>()).ShouldBe(["Orbitrap Astral", "Stellar"]);
    }

    [Fact]
    public void Without_the_file_no_instrument_is_questioned()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project", "--instrument", "Anything");

        repo.Problems().ShouldBeEmpty();
        repo.Ok("list")["instruments"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public void An_instrument_can_be_listed_by_name_alone()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "instruments: [Orbitrap Astral, {name: Stellar}, Orbitrap Astral]\n");

        repo.Ok("list")["instruments"]!.AsArray().Select(i => i!.GetValue<string>()).ShouldBe(["Orbitrap Astral", "Stellar"]);
    }
}
