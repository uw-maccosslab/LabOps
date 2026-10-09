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
    public void A_list_that_does_not_read_is_reported_and_every_command_still_works()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "instruments:\n  - name: Stellar: 2\n");
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project", "--instrument", "Stellar");

        // The record is written and the answer is ordinary JSON, not a crash after the write.
        repo.Ok("plan", "2026-10-Pilot-DIA", "data_acquisition", "--start", "2026-11-02");
        TestRepo.Steps(experiment)["data_acquisition"]["planned_start"].ShouldBe(new DateOnly(2026, 11, 2));
        repo.Ok("list")["instruments"]!.AsArray().ShouldBeEmpty();
        var problem = repo.Problems().ShouldHaveSingleItem();
        problem.Level.ShouldBe("ERROR");
        problem.Message.ShouldStartWith("config/instruments.yaml: mapping values are not allowed", Case.Sensitive);
    }

    [Fact]
    public void A_people_list_that_does_not_read_is_reported_too()
    {
        using var repo = TestRepo.Create();
        File.WriteAllText(Path.Combine(repo.Root, "config", "people.yaml"), "people:\n  - login: a: b\n");

        repo.Ok("list")["people"]!.AsArray().ShouldBeEmpty();
        repo.Problems().ShouldContain(p => p.Level == "ERROR" && p.Message.StartsWith("config/people.yaml: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bare_list_of_names_is_read_too()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "- Orbitrap Astral\n- Stellar\n");

        repo.Ok("list")["instruments"]!.AsArray().Select(i => i!.GetValue<string>()).ShouldBe(["Orbitrap Astral", "Stellar"]);
    }

    [Fact]
    public void An_instrument_can_be_listed_by_name_alone_and_once_whatever_its_capitals()
    {
        using var repo = TestRepo.Create();
        // Names are matched ignoring case, so stellar is Stellar again: one instrument, as first spelled.
        WriteInstruments(repo, "instruments: [Orbitrap Astral, {name: Stellar}, Orbitrap Astral, stellar]\n");

        repo.Ok("list")["instruments"]!.AsArray().Select(i => i!.GetValue<string>()).ShouldBe(["Orbitrap Astral", "Stellar"]);
    }
}
