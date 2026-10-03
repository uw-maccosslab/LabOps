using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ServicesQuotes.Core.Claude;
using ServicesQuotes.Tests.TestSupport;

namespace ServicesQuotes.Tests.Claude;

public sealed class AppToolsTests
{
    [Fact]
    public async Task Approval_returns_the_allow_shape_claude_code_expects()
    {
        var ui = new ScriptedUi { Permission = new PermissionDecision(true) };
        var tools = new AppTools { Ui = ui };
        using var input = JsonDocument.Parse("""{"command":"node -e 1"}""");

        var answer = await tools.Approve("Bash", input.RootElement);

        using var doc = JsonDocument.Parse(answer);
        doc.RootElement.GetProperty("behavior").GetString().ShouldBe("allow");
        doc.RootElement.GetProperty("updatedInput").GetProperty("command").GetString().ShouldBe("node -e 1");
        ui.PermissionRequests.ShouldHaveSingleItem().Description.ShouldBe("Running: node -e 1");
    }

    [Fact]
    public async Task Denial_carries_a_message_for_claude()
    {
        var tools = new AppTools { Ui = new ScriptedUi { Permission = new PermissionDecision(false, "Not today.") } };
        using var input = JsonDocument.Parse("{}");

        using var doc = JsonDocument.Parse(await tools.Approve("Bash", input.RootElement));

        doc.RootElement.GetProperty("behavior").GetString().ShouldBe("deny");
        doc.RootElement.GetProperty("message").GetString().ShouldBe("Not today.");
    }

    [Fact]
    public async Task With_no_chat_pane_nothing_is_approved_and_questions_get_a_reply()
    {
        var tools = new AppTools();
        using var input = JsonDocument.Parse("{}");

        using var doc = JsonDocument.Parse(await tools.Approve("Bash", input.RootElement));
        doc.RootElement.GetProperty("behavior").GetString().ShouldBe("deny");
        (await tools.AskUser("How many samples?")).ShouldContain("Make a reasonable assumption");
    }

    [Fact]
    public async Task Report_reaches_the_chat_pane()
    {
        var ui = new ScriptedUi();
        var tools = new AppTools { Ui = ui };

        await tools.ReportQuoteSummary("MacCoss-2026-X", 1000m, 50m, 20m, flags: ["controls"], questions: ["Rate?"]);

        var report = ui.Reports.ShouldHaveSingleItem();
        report.QuoteNumber.ShouldBe("MacCoss-2026-X");
        report.Total.ShouldBe(1000m);
        report.Flags.ShouldBe(["controls"]);
        report.Questions.ShouldBe(["Rate?"]);
        report.Assumptions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Server_requires_its_token_and_serves_the_three_tools()
    {
        var ui = new ScriptedUi { Answer = "Blue" };
        await using var server = await AppToolServer.StartAsync(new AppTools { Ui = ui });

        server.Endpoint.Host.ShouldBe("127.0.0.1");

        using (var http = new HttpClient())
        {
            var anonymous = await http.PostAsync(server.Endpoint, new StringContent("{}"));
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = server.Endpoint,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {server.Token}" },
        }));

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        names.ShouldBe(["approve", "ask_user", "report_quote_summary"], ignoreOrder: true);

        var result = await client.CallToolAsync("ask_user",
            new Dictionary<string, object?> { ["question"] = "What color?", ["options"] = new[] { "Red", "Blue" } });
        result.Content.OfType<TextContentBlock>().Single().Text.ShouldBe("Blue");
        ui.Questions.ShouldHaveSingleItem().ShouldBe("What color? [Red, Blue]");
    }

    [Fact]
    public async Task Config_file_points_claude_at_the_server_with_the_token()
    {
        using var temp = new TempDirectory();
        await using var server = await AppToolServer.StartAsync(new AppTools());

        var path = server.WriteMcpConfig(temp.Path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entry = doc.RootElement.GetProperty("mcpServers").GetProperty("quotes-app");
        entry.GetProperty("type").GetString().ShouldBe("http");
        entry.GetProperty("url").GetString().ShouldBe(server.Endpoint.ToString());
        entry.GetProperty("headers").GetProperty("Authorization").GetString().ShouldBe($"Bearer {server.Token}");
    }

    private sealed class ScriptedUi : IClaudeHostUi
    {
        public string Answer { get; init; } = "";

        public PermissionDecision Permission { get; init; } = new(false);

        public List<string> Questions { get; } = [];

        public List<QuoteReport> Reports { get; } = [];

        public List<PermissionRequest> PermissionRequests { get; } = [];

        public Task<string> AskUserAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Questions.Add($"{question} [{string.Join(", ", options)}]");
            return Task.FromResult(Answer);
        }

        public Task ShowReportAsync(QuoteReport report, CancellationToken cancellationToken)
        {
            Reports.Add(report);
            return Task.CompletedTask;
        }

        public Task<PermissionDecision> RequestPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
        {
            PermissionRequests.Add(request);
            return Task.FromResult(Permission);
        }
    }
}
