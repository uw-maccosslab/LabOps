using LabOps.App.ViewModels;
using LabOps.Core.Claude;
using LabOps.Core.Infrastructure;

namespace LabOps.Tests.Claude;

/// <summary>Each person chooses the model and effort Claude uses in LabOps, since both use their own Claude plan.</summary>
public sealed class ClaudeChoicesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("labops-claude-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_default_is_named_from_the_person_s_own_claude_code_settings()
    {
        var file = Path.Combine(_folder, "settings.json");
        File.WriteAllText(file, """{ "effortLevel": "xhigh", "theme": "dark" }""");

        var defaults = ClaudeChoices.ReadClaudeDefaults(file);
        defaults.ShouldBe((null, "xhigh"));
        ClaudeChoices.Efforts(null, defaults.Effort)[0].ShouldBe(new ClaudeChoice(null, "Your Claude Code default (xhigh)"));
        ClaudeChoices.Models(null, defaults.Model)[0].ShouldBe(new ClaudeChoice(null, "Your Claude Code default"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Settings_that_cannot_be_read_name_no_default(string? content)
    {
        var file = Path.Combine(_folder, "settings.json");
        if (content is not null)
        {
            File.WriteAllText(file, content);
        }

        ClaudeChoices.ReadClaudeDefaults(file).ShouldBe((null, null));
    }

    [Fact]
    public void Every_level_claude_code_takes_is_offered_and_a_model_set_by_hand_is_kept()
    {
        ClaudeChoices.Efforts(null, null).Select(c => c.Value).ShouldBe([null, "low", "medium", "high", "xhigh", "max"]);
        ClaudeChoices.Models(null, null).Select(c => c.Value).ShouldBe(
            [null, "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5", "claude-sonnet-5", "claude-haiku-4-5"]);

        var models = ClaudeChoices.Models("claude-opus-4-8", null);
        models.Last().ShouldBe(new ClaudeChoice("claude-opus-4-8", "claude-opus-4-8"));
        ClaudeChoices.Find(models, "claude-opus-4-8").ShouldBe(models.Last());
        ClaudeChoices.Find(models, "CLAUDE-SONNET-5-5").Value.ShouldBe("claude-sonnet-5-5");
        ClaudeChoices.Find(models, " ").Value.ShouldBeNull();
    }

    [Fact]
    public void Every_model_is_named_with_its_version()
    {
        ClaudeChoices.Models(null, null).Skip(1).Select(c => c.Label.Split(':')[0])
            .ShouldBe(["Opus 5.5", "Opus 5", "Sonnet 5.5", "Sonnet 5", "Haiku 4.5"]);
        // An alias chosen in LabOps 26.8.1 or 26.8.2 is kept, and says what it means.
        ClaudeChoices.Models("opus", null).Last().ShouldBe(new ClaudeChoice("opus", "Opus, the latest version"));
    }

    [Fact]
    public void A_choice_in_setup_is_saved_at_once_and_the_default_saves_nothing_to_pass()
    {
        var settings = new AppSettings { ClaudeModel = "claude-sonnet-5-5" };
        var saves = 0;
        var vm = new ClaudeSettingsViewModel(settings, () => saves++, (null, "high"));

        vm.Model.Label.ShouldStartWith("Sonnet 5.5");
        vm.Effort.Label.ShouldBe("Your Claude Code default (high)");
        saves.ShouldBe(0);

        vm.Effort = vm.Efforts.Single(e => e.Value == "low");
        settings.ClaudeEffort.ShouldBe("low");
        vm.Model = vm.Models[0];
        settings.ClaudeModel.ShouldBeNull();
        saves.ShouldBe(2);
    }
}
