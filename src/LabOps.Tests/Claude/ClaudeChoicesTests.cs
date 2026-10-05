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
        ClaudeChoices.Models(null, null).Select(c => c.Value).ShouldBe([null, "opus", "sonnet", "haiku"]);

        var models = ClaudeChoices.Models("claude-sonnet-5-5", null);
        models.Last().ShouldBe(new ClaudeChoice("claude-sonnet-5-5", "claude-sonnet-5-5"));
        ClaudeChoices.Find(models, "claude-sonnet-5-5").ShouldBe(models.Last());
        ClaudeChoices.Find(models, "OPUS").Value.ShouldBe("opus");
        ClaudeChoices.Find(models, " ").Value.ShouldBeNull();
    }

    [Fact]
    public void A_choice_in_setup_is_saved_at_once_and_the_default_saves_nothing_to_pass()
    {
        var settings = new AppSettings { ClaudeModel = "sonnet" };
        var saves = 0;
        var vm = new ClaudeSettingsViewModel(settings, () => saves++, (null, "high"));

        vm.Model.Value.ShouldBe("sonnet");
        vm.Effort.Label.ShouldBe("Your Claude Code default (high)");
        saves.ShouldBe(0);

        vm.Effort = vm.Efforts.Single(e => e.Value == "low");
        settings.ClaudeEffort.ShouldBe("low");
        vm.Model = vm.Models[0];
        settings.ClaudeModel.ShouldBeNull();
        saves.ShouldBe(2);
    }
}
