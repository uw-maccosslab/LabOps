using System.Text.Json;
using LabOps.Core.Claude;
using LabOps.Core.Repositories;

namespace LabOps.Tests.Claude;

public sealed class ClaudeSessionTests
{
    [Fact]
    public async Task Messages_are_written_as_stream_json_user_lines()
    {
        var transport = new FakeTransport("");
        await using var session = new ClaudeSession(transport);

        await session.SendAsync("Draft a quote for \"Dr. Lee\"\nwith 30 samples");

        using var doc = JsonDocument.Parse(transport.Written.ToString().Trim());
        var root = doc.RootElement;
        root.GetProperty("type").GetString().ShouldBe("user");
        root.GetProperty("message").GetProperty("role").GetString().ShouldBe("user");
        var part = root.GetProperty("message").GetProperty("content")[0];
        part.GetProperty("type").GetString().ShouldBe("text");
        part.GetProperty("text").GetString().ShouldBe("Draft a quote for \"Dr. Lee\"\nwith 30 samples");
        transport.Written.ToString().Count(c => c == '\n').ShouldBe(1);
    }

    [Fact]
    public async Task Replayed_output_raises_events_and_records_the_session()
    {
        var recorded = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-read-file.jsonl"));
        var transport = new FakeTransport(recorded, exitCode: 0);
        var events = new List<ClaudeEvent>();
        var ended = new TaskCompletionSource();

        await using var session = new ClaudeSession(transport);
        session.EventReceived += e =>
        {
            lock (events)
            {
                events.Add(e);
            }

            if (e is SessionEnded)
            {
                ended.TrySetResult();
            }
        };

        transport.Release();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));

        session.SessionId.ShouldBe("202f7acc-e099-4add-8cd4-1d2b8e5d6bb1");
        session.IsBusy.ShouldBeFalse();
        session.HasEnded.ShouldBeTrue();
        events.OfType<TurnFinished>().ShouldHaveSingleItem();
        events.Last().ShouldBeOfType<SessionEnded>().ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Command_line_carries_the_settings_for_headless_quote_work()
    {
        var options = new ClaudeSessionOptions
        {
            ClaudePath = "claude.exe",
            WorkingDirectory = "C:\\Quotes",
            ResumeSessionId = "abc",
            McpConfigPath = "C:\\cfg.json",
            PermissionPromptTool = AppTools.PermissionToolName,
            AllowedTools = ["Read", "Bash(uv run python scripts/quote.py:*)"],
            DisallowedTools = ["Bash(git push:*)"],
            AppendSystemPrompt = "be brief",
        };

        var args = options.BuildArguments();

        args.ShouldContain("--print");
        Following(args, "--input-format").ShouldBe("stream-json");
        Following(args, "--output-format").ShouldBe("stream-json");
        args.ShouldContain("--verbose");
        Following(args, "--permission-mode").ShouldBe("acceptEdits");
        Following(args, "--resume").ShouldBe("abc");
        Following(args, "--mcp-config").ShouldBe("C:\\cfg.json");
        Following(args, "--permission-prompt-tool").ShouldBe("mcp__quotes-app__approve");
        Following(args, "--allowedTools").ShouldBe("Read,Bash(uv run python scripts/quote.py:*)");
        Following(args, "--disallowedTools").ShouldBe("Bash(git push:*)");
        Following(args, "--append-system-prompt").ShouldBe("be brief");
        args.ShouldNotContain("--model");
        args.ShouldNotContain("--effort");
    }

    [Fact]
    public void The_model_and_effort_chosen_in_setup_go_on_the_command_line()
    {
        var args = new ClaudeSessionOptions { ClaudePath = "claude.exe", WorkingDirectory = "C:\\Quotes", Model = "claude-sonnet-5-5", Effort = "high" }
            .BuildArguments();

        Following(args, "--model").ShouldBe("claude-sonnet-5-5");
        Following(args, "--effort").ShouldBe("high");
    }

    [Fact]
    public void Claude_may_read_the_chat_s_attachments_folder()
    {
        var args = new ClaudeSessionOptions
        {
            ClaudePath = "claude.exe", WorkingDirectory = "C:\\Quotes", AdditionalDirectories = ["C:\\LabOps\\attachments"],
        }.BuildArguments();

        Following(args, "--add-dir").ShouldBe("C:\\LabOps\\attachments");
    }

    [Fact]
    public void Claude_may_not_change_git_history_or_browse_the_web()
    {
        ClaudeLauncher.DisallowedTools.ShouldContain("Bash(git commit:*)");
        ClaudeLauncher.DisallowedTools.ShouldContain("Bash(git push:*)");
        ClaudeLauncher.DisallowedTools.ShouldContain("Bash(git reset:*)");
        ClaudeLauncher.DisallowedTools.ShouldContain("WebFetch");
        ClaudeLauncher.DisallowedTools.ShouldContain("Bash(git config:*)");
        var quotes = ClaudeLauncher.AllowedTools(RepositoryProfile.Quotes);
        quotes.ShouldNotContain(t => t.StartsWith("Bash(git commit", StringComparison.Ordinal));
        quotes.ShouldContain("Bash(uv run python scripts/quote.py:*)");
        quotes.ShouldNotContain("Bash(uv run python scripts/project.py:*)");

        var projects = ClaudeLauncher.AllowedTools(RepositoryProfile.Projects);
        projects.ShouldContain("Bash(uv run python scripts/project.py:*)");
        projects.ShouldNotContain("Bash(uv run python scripts/quote.py:*)");
    }

    [Fact]
    public void Each_repository_tells_claude_its_own_skills_and_rules()
    {
        var projects = ClaudeLauncher.SystemPrompt(RepositoryProfile.Projects, "Mike");
        projects.ShouldContain("organize-metadata");
        projects.ShouldContain("labops projects scan");
        projects.ShouldNotContain("report_quote_summary");
        ClaudeLauncher.AllowedTools(RepositoryProfile.Projects).ShouldContain("Bash(labops projects:*)");
        ClaudeLauncher.AllowedTools(RepositoryProfile.Quotes).ShouldNotContain("Bash(labops projects:*)");
        projects.ShouldNotContain("\u2014");

        var quotes = ClaudeLauncher.SystemPrompt(RepositoryProfile.Quotes, null);
        quotes.ShouldContain("new-quote");
        quotes.ShouldContain("report_quote_summary");
        quotes.ShouldContain("do not chain commands");
    }

    [Theory]
    [InlineData("Bash", """{"command":"uv run python scripts/quote.py build MacCoss-2026-NWU-SC --json"}""", "Building the quote (MacCoss-2026-NWU-SC)")]
    [InlineData("Bash", """{"command":"uv run python scripts/quote.py list"}""", "Looking through existing quotes")]
    [InlineData("Bash", """{"command":"labops projects scan inbox/X/sheet.xlsx"}""", "Checking the file for identifying information")]
    [InlineData("Bash", """{"command":"labops projects --json stage X sample_prep done"}""", "Updating the steps")]
    [InlineData("Bash", """{"command":"uv run python scripts/project.py link X wiki /MacCoss/X"}""", "Updating the links")]
    [InlineData("Edit", """{"file_path":"D:\\q\\quotes\\UW-Alder\\2026\\MacCoss-2026-UW-ALDER-GCF15\\quote.yaml"}""", "Editing MacCoss-2026-UW-ALDER-GCF15/quote.yaml")]
    [InlineData("mcp__quotes-app__ask_user", "{}", "")]
    [InlineData("mcp__claude_ai_Gmail__search_threads", "{}", "Reading the email in Gmail")]
    public void Tool_calls_are_described_in_plain_language(string tool, string input, string expected)
    {
        using var doc = JsonDocument.Parse(input);
        ToolDescriptions.Describe(tool, doc.RootElement).ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"C:\Quotes\demo\hello.txt", "hello.txt")]
    [InlineData(@"C:\Quotes\LabOps-Quotes\quotes\UW-Alder\2026\MacCoss-2026-UW-ALDER-GCF15\quote.yaml", "MacCoss-2026-UW-ALDER-GCF15/quote.yaml")]
    [InlineData("quotes/G/2026/A/calculation.md", "A/calculation.md")]
    [InlineData(@"D:\repo\rates\rates.yaml", "rates.yaml")]
    public void Paths_are_shortened_only_for_real_quote_folders(string path, string expected) =>
        ToolDescriptions.ShortPath(path).ShouldBe(expected);

    private static string Following(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>In-memory pipes: captures what the session writes, and plays back recorded output.</summary>
    private sealed class FakeTransport(string output, int exitCode = 0) : IClaudeTransport
    {
        private readonly TaskCompletionSource _released = new();
        private readonly TaskCompletionSource<int> _exited = new();

        public StringWriter Written { get; } = new();

        public TextWriter Input => Written;

        public TextReader Output { get; } = new GatedReader(output);

        public Task<int> Exited => _exited.Task;

        public string ErrorTail => "";

        public void Release()
        {
            ((GatedReader)Output).Open();
            _exited.TrySetResult(exitCode);
        }

        public void Stop() => Release();

        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }

        /// <summary>Holds the output back until the test is ready, like a process that has not answered yet.</summary>
        private sealed class GatedReader(string text) : TextReader
        {
            private readonly StringReader _inner = new(text);
            private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Open() => _open.TrySetResult();

            public override async Task<string?> ReadLineAsync()
            {
                await _open.Task.ConfigureAwait(false);
                return _inner.ReadLine();
            }
        }
    }
}
