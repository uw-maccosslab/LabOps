using System.Text.Json;

namespace ChargeState.Core.Claude;

/// <summary>
/// Reads one line of Claude Code's <c>--output-format stream-json</c> output.
/// </summary>
/// <remarks>
/// Only the parts the app shows are read. Everything else (thinking blocks, rate-limit notices,
/// task summaries, event types added in later Claude Code versions) is skipped rather than
/// treated as an error, so a Claude Code update that adds an event cannot break the app. The
/// tests replay a transcript recorded from a real session to catch changes to the parts that
/// are read.
/// </remarks>
public static class StreamJsonParser
{
    public static IReadOnlyList<ClaudeEvent> Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return [];
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return Text(root, "type") switch
            {
                "system" when Text(root, "subtype") == "init" => [ParseInit(root)],
                "assistant" => ParseAssistant(root),
                "user" => ParseToolResults(root),
                "result" => [ParseResult(root)],
                _ => [],
            };
        }
    }

    private static SessionStarted ParseInit(JsonElement root)
    {
        var servers = new List<McpServerState>();
        if (root.TryGetProperty("mcp_servers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in list.EnumerateArray())
            {
                servers.Add(new McpServerState(Text(server, "name") ?? "", Text(server, "status") ?? ""));
            }
        }

        return new SessionStarted(Text(root, "session_id") ?? "", Text(root, "model"), servers);
    }

    private static List<ClaudeEvent> ParseAssistant(JsonElement root)
    {
        var events = new List<ClaudeEvent>();
        foreach (var block in Content(root))
        {
            switch (Text(block, "type"))
            {
                case "text" when Text(block, "text") is { Length: > 0 } text:
                    events.Add(new AssistantText(text));
                    break;
                case "tool_use":
                    var input = block.TryGetProperty("input", out var i) ? i.Clone() : default;
                    events.Add(new ToolStarted(Text(block, "id") ?? "", Text(block, "name") ?? "", input));
                    break;
            }
        }

        return events;
    }

    private static List<ClaudeEvent> ParseToolResults(JsonElement root)
    {
        var events = new List<ClaudeEvent>();
        foreach (var block in Content(root))
        {
            if (Text(block, "type") != "tool_result")
            {
                continue;
            }

            var isError = block.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            events.Add(new ToolFinished(Text(block, "tool_use_id") ?? "", isError, ContentText(block)));
        }

        return events;
    }

    private static TurnFinished ParseResult(JsonElement root)
    {
        var denials = new List<string>();
        if (root.TryGetProperty("permission_denials", out var d) && d.ValueKind == JsonValueKind.Array)
        {
            foreach (var denial in d.EnumerateArray())
            {
                if (Text(denial, "tool_name") is { } tool)
                {
                    denials.Add(tool);
                }
            }
        }

        decimal? cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetDecimal()
            : null;
        var turns = root.TryGetProperty("num_turns", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
        var isError = root.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True;

        return new TurnFinished(isError, Text(root, "result"), Text(root, "session_id"), cost, turns, denials);
    }

    private static IEnumerable<JsonElement> Content(JsonElement root)
    {
        if (root.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            return content.EnumerateArray().ToList();
        }

        return [];
    }

    private static string? ContentText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join("\n", content.EnumerateArray()
                .Where(p => Text(p, "type") == "text").Select(p => Text(p, "text"))),
            _ => null,
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
