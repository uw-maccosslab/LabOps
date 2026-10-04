using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace LabOps.Core.Claude;

/// <summary>Claude's summary of a drafted or changed quote, shown as a card in the chat pane.</summary>
public sealed record QuoteReport(
    string QuoteNumber,
    decimal Total,
    decimal PerSample,
    decimal StudySamples,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> RateOverrides,
    IReadOnlyList<string> NotPriced,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Questions,
    string? ChangeSummary);

/// <summary>A tool Claude wants to use that is not on the allowlist.</summary>
public sealed record PermissionRequest(string ToolName, JsonElement Input, string Description);

/// <summary>The user's answer to a <see cref="PermissionRequest"/>.</summary>
public sealed record PermissionDecision(bool Allow, string? Message = null);

/// <summary>What the chat pane must provide for Claude's app tools. Implemented by the UI.</summary>
public interface IClaudeHostUi
{
    Task<string> AskUserAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken);

    Task ShowReportAsync(QuoteReport report, CancellationToken cancellationToken);

    Task<PermissionDecision> RequestPermissionAsync(PermissionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The tools the app gives Claude, served over MCP by <see cref="AppToolServer"/>.
/// </summary>
/// <remarks>
/// One instance for the life of the app; <see cref="Ui"/> points at whichever chat pane is
/// active. With no pane attached, questions are declined politely rather than left hanging.
/// </remarks>
[McpServerToolType]
public sealed class AppTools
{
    public const string ServerName = "quotes-app";

    /// <summary>The name Claude Code knows the permission tool by.</summary>
    public const string PermissionToolName = $"mcp__{ServerName}__approve";

    public static readonly string[] ModelToolNames =
    [
        $"mcp__{ServerName}__ask_user",
        $"mcp__{ServerName}__report_quote_summary",
    ];

    /// <summary>The chat pane currently talking to Claude.</summary>
    public IClaudeHostUi? Ui { get; set; }

    [McpServerTool(Name = "ask_user", Title = "Ask the user")]
    [Description("Ask the person using the LabOps app one short question and wait for the answer. "
        + "Use only when something essential is missing, such as the sample count or who the quote is for.")]
    public async Task<string> AskUser(
        [Description("The question, in plain language.")] string question,
        [Description("Optional short answers to offer as buttons. The user can also type their own.")] string[]? options = null,
        CancellationToken cancellationToken = default)
    {
        if (Ui is null)
        {
            return "Nobody is available to answer right now. Make a reasonable assumption and list it in your report.";
        }

        var answer = await Ui.AskUserAsync(question, options ?? [], cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(answer) ? "The user did not answer." : answer;
    }

    [McpServerTool(Name = "report_quote_summary", Title = "Report the quote")]
    [Description("Report the finished quote to the user. Call once, at the end, after the quote builds without errors.")]
    public async Task<string> ReportQuoteSummary(
        [Description("The quote number, for example MacCoss-2026-NWU-SC.")] string quoteNumber,
        [Description("Total in US dollars.")] decimal total,
        [Description("Cost per study sample in US dollars.")] decimal perSample,
        [Description("Number of study samples.")] decimal studySamples,
        [Description("Validation warnings and anything unusual.")] string[]? flags = null,
        [Description("Each rate override with its value and reason.")] string[]? rateOverrides = null,
        [Description("Anything the request asked for that the quote does not price.")] string[]? notPriced = null,
        [Description("Every assumption or guess made.")] string[]? assumptions = null,
        [Description("Questions for Mike.")] string[]? questions = null,
        [Description("For a change to an existing quote: what changed and how the total moved.")] string? changeSummary = null,
        CancellationToken cancellationToken = default)
    {
        var report = new QuoteReport(quoteNumber, total, perSample, studySamples,
            flags ?? [], rateOverrides ?? [], notPriced ?? [], assumptions ?? [], questions ?? [], changeSummary);

        if (Ui is not null)
        {
            await Ui.ShowReportAsync(report, cancellationToken).ConfigureAwait(false);
        }

        return "The summary is shown to the user. You are done; do not repeat it.";
    }

    /// <summary>
    /// Answers Claude Code's permission prompts (passed as --permission-prompt-tool). Claude Code
    /// calls this itself, with these exact parameter names, whenever a tool is not pre-approved.
    /// </summary>
    [McpServerTool(Name = "approve", Title = "Permission prompt")]
    [Description("Decides whether a tool call may run. Called by Claude Code, not by the model.")]
    public async Task<string> Approve(
        [Description("The tool that wants to run.")] string tool_name,
        [Description("The tool's input.")] JsonElement input,
        [Description("The tool use id.")] string? tool_use_id = null,
        CancellationToken cancellationToken = default)
    {
        _ = tool_use_id;
        var decision = Ui is null
            ? new PermissionDecision(false, "Nobody is available to approve this.")
            : await Ui.RequestPermissionAsync(
                new PermissionRequest(tool_name, input.Clone(), ToolDescriptions.Describe(tool_name, input)),
                cancellationToken).ConfigureAwait(false);

        return decision.Allow
            ? JsonSerializer.Serialize(new { behavior = "allow", updatedInput = input })
            : JsonSerializer.Serialize(new { behavior = "deny", message = decision.Message ?? "The user declined this step." });
    }
}
