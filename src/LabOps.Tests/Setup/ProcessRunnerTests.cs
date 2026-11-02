using LabOps.Core.Processes;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Setup;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Each_line_is_passed_on_as_it_is_written_from_both_streams()
    {
        // Setup shows uv's progress while it runs, and uv writes its progress to stderr.
        var tools = new ToolLocator();
        var git = tools.Require(Tool.Git);
        using var temp = new TempDirectory();
        var lines = new List<string>();

        var version = await new ProcessRunner(tools).RunAsync(git, ["--version"], temp.Path,
            onLine: line => { lock (lines) { lines.Add(line); } });
        var notARepository = await new ProcessRunner(tools).RunAsync(git, ["status"], temp.Path,
            onLine: line => { lock (lines) { lines.Add(line); } });

        version.Succeeded.ShouldBeTrue();
        notARepository.Succeeded.ShouldBeFalse();
        lines.ShouldContain(l => l.StartsWith("git version", StringComparison.Ordinal));
        lines.ShouldContain(l => l.Contains("not a git repository", StringComparison.Ordinal));
        notARepository.StandardError.ShouldContain("not a git repository");
    }
}
