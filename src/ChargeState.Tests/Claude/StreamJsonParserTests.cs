using ChargeState.Core.Claude;

namespace ChargeState.Tests.Claude;

/// <summary>
/// Replays a transcript recorded from a real Claude Code session (2.1.280), so a CLI update that
/// changes the parts the app reads fails here rather than in a user's chat pane.
/// </summary>
public sealed class StreamJsonParserTests
{
    private static List<ClaudeEvent> Replay(string fixture) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture))
            .SelectMany(StreamJsonParser.Parse)
            .ToList();

    [Fact]
    public void Recorded_session_yields_the_events_the_app_shows()
    {
        var events = Replay("claude-read-file.jsonl");

        var started = events.OfType<SessionStarted>().Single();
        started.SessionId.ShouldBe("202f7acc-e099-4add-8cd4-1d2b8e5d6bb1");
        started.Model.ShouldNotBeNull().ShouldStartWith("claude-haiku");

        var tool = events.OfType<ToolStarted>().Single();
        tool.Name.ShouldBe("Read");
        tool.Describe().ShouldBe("Reading hello.txt");

        var finished = events.OfType<ToolFinished>().Single();
        finished.ToolUseId.ShouldBe(tool.Id);
        finished.IsError.ShouldBeFalse();
        finished.Content.ShouldNotBeNull().ShouldContain("lantern");

        events.OfType<AssistantText>().Single().Text.ShouldBe("lantern");

        var result = events.OfType<TurnFinished>().Single();
        result.IsError.ShouldBeFalse();
        result.Result.ShouldBe("lantern");
        result.SessionId.ShouldBe(started.SessionId);
        result.Turns.ShouldBe(2);
    }

    [Fact]
    public void Thinking_and_unknown_events_are_skipped_not_errors()
    {
        StreamJsonParser.Parse("""{"type":"rate_limit_event","rate_limit_info":{}}""").ShouldBeEmpty();
        StreamJsonParser.Parse("""{"type":"system","subtype":"task_summary"}""").ShouldBeEmpty();
        StreamJsonParser.Parse("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":""}]}}""").ShouldBeEmpty();
        StreamJsonParser.Parse("""{"type":"something_new_in_a_later_version"}""").ShouldBeEmpty();
        StreamJsonParser.Parse("not json at all").ShouldBeEmpty();
        StreamJsonParser.Parse("").ShouldBeEmpty();
    }

    [Fact]
    public void Tool_result_content_can_be_a_list_of_text_parts()
    {
        var events = StreamJsonParser.Parse(
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":[{"type":"text","text":"line 1"},{"type":"text","text":"line 2"}]}]}}""");

        var result = events.ShouldHaveSingleItem().ShouldBeOfType<ToolFinished>();
        result.IsError.ShouldBeTrue();
        result.Content.ShouldBe("line 1\nline 2");
    }

    [Fact]
    public void Permission_denials_are_reported_by_tool_name()
    {
        var events = StreamJsonParser.Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s","num_turns":3,"permission_denials":[{"tool_name":"Bash","tool_use_id":"x","tool_input":{}}]}""");

        events.ShouldHaveSingleItem().ShouldBeOfType<TurnFinished>().PermissionDenials.ShouldBe(["Bash"]);
    }

    [Fact]
    public void Mcp_server_status_comes_from_init()
    {
        var events = StreamJsonParser.Parse(
            """{"type":"system","subtype":"init","session_id":"s","mcp_servers":[{"name":"quotes-app","status":"connected"},{"name":"other","status":"failed"}]}""");

        var started = events.ShouldHaveSingleItem().ShouldBeOfType<SessionStarted>();
        started.McpServers.Single(s => s.Name == "quotes-app").Connected.ShouldBeTrue();
        started.McpServers.Single(s => s.Name == "other").Connected.ShouldBeFalse();
    }
}
