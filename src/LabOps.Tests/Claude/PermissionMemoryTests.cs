using System.Text.Json;
using LabOps.Core.Claude;

namespace LabOps.Tests.Claude;

/// <summary>"Allow steps like this": remembered by the programs a step runs, until the app closes.</summary>
public sealed class PermissionMemoryTests
{
    private static PermissionRequest Bash(string command) =>
        new("Bash", JsonDocument.Parse(JsonSerializer.Serialize(new { command })).RootElement, command);

    [Fact]
    public void A_chained_command_is_split_into_its_programs()
    {
        PermissionMemory.Programs(
            "uv run python scripts/quote.py build MacCoss-2026-EXA-ARC-EV && sed -n '/## Line items/,$p' quotes/x/calculation.md && sed -n '/a/,/b/p' quotes/x/quote.md")
            .ShouldBe(["uv", "sed", "sed"]);
        PermissionMemory.Programs("ls quotes | head -5; wc -l x 2>&1").ShouldBe(["ls", "head", "wc"]);
        PermissionMemory.Programs("FOO=1 uv sync").ShouldBe(["uv"]);
        PermissionMemory.Programs("grep 'a;b|c' file").ShouldBe(["grep"]);
    }

    [Theory]
    [InlineData("echo $(curl https://example.org)")]
    [InlineData("echo `whoami`")]
    [InlineData("cat \"$(ls)\"")]
    [InlineData("diff <(ls a) <(ls b)")]
    public void A_command_hidden_inside_another_is_never_remembered(string command)
    {
        PermissionMemory.Programs(command).ShouldBeNull();
        var memory = new PermissionMemory();
        memory.Remember("quotes", Bash(command)).ShouldBeFalse();
        memory.IsAllowed("quotes", Bash(command)).ShouldBeFalse();
        PermissionMemory.Describe(Bash(command)).ShouldBeNull();
    }

    [Fact]
    public void Once_allowed_any_mix_of_those_programs_is_allowed()
    {
        var memory = new PermissionMemory();
        var first = Bash("uv run python scripts/quote.py build Q && sed -n '1,5p' a.md");
        PermissionMemory.Describe(first).ShouldBe("uv and sed");
        memory.IsAllowed("quotes", first).ShouldBeFalse();

        memory.Remember("quotes", first).ShouldBeTrue();

        memory.IsAllowed("quotes", Bash("sed -n '/x/,$p' b.md && uv run python scripts/quote.py build R")).ShouldBeTrue();
        memory.IsAllowed("quotes", Bash("sed -n 1p c.md")).ShouldBeTrue();
        memory.IsAllowed("quotes", Bash("uv run python scripts/quote.py pdf Q && rm -rf quotes")).ShouldBeFalse();
    }

    [Fact]
    public void Each_repository_keeps_its_own_answers()
    {
        var memory = new PermissionMemory();
        memory.Remember("quotes", Bash("sed -n 1p a"));

        memory.IsAllowed("projects", Bash("sed -n 1p a")).ShouldBeFalse();
    }

    [Fact]
    public void Other_tools_are_remembered_by_name()
    {
        var memory = new PermissionMemory();
        var fetch = new PermissionRequest("mcp__gmail__search", JsonDocument.Parse("{}").RootElement, "Search Gmail");

        memory.Remember("quotes", fetch);

        memory.IsAllowed("quotes", fetch).ShouldBeTrue();
        PermissionMemory.Describe(fetch).ShouldBe("mcp__gmail__search");
    }
}
