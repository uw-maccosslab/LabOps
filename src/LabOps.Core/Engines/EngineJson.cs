using System.Text.Json;
using LabOps.Core.Processes;

namespace LabOps.Core.Engines;

/// <summary>A repository's engine reported a problem; <see cref="Exception.Message"/> is written for the user.</summary>
public class EngineException(string message) : Exception(message);

/// <summary>
/// What quote.py and project.py have in common: both run through <c>uv run --frozen</c> in their
/// repository and answer <c>--json</c> with <c>{"ok": true, ...}</c>, or
/// <c>{"ok": false, "error": "..."}</c>.
/// </summary>
/// <remarks>
/// <c>uv run --frozen</c> builds the environment from the committed uv.lock (downloading Python on
/// first use) and never rewrites the lock file, so running an engine cannot leave a change for the
/// app to commit.
/// </remarks>
public static class EngineJson
{
    /// <summary>Long enough for the first run, which may download Python and every package.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Runs <paramref name="script"/> with <c>--json</c> and the arguments, in <paramref name="repository"/>.</summary>
    public static Task<ProcessResult> RunAsync(
        IProcessRunner runner, ToolLocator tools, string repository, string script, IEnumerable<string> args,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "run", "--frozen", "python", script, "--json" };
        arguments.AddRange(args);
        return runner.RunAsync(tools.Require(Tool.Uv), arguments, repository, timeout: Timeout, cancellationToken: cancellationToken);
    }

    /// <summary>Parses an engine's answer.</summary>
    /// <param name="result">The finished process.</param>
    /// <param name="engineName">How to name the engine in a message ("quote engine").</param>
    /// <param name="error">Makes the exception to throw.</param>
    /// <param name="allowNotOk">Return a not-ok answer instead of throwing, when it carries <paramref name="partialKey"/>.</param>
    /// <param name="partialKey">The key a useful not-ok answer has (for example a list of problems).</param>
    public static JsonDocument Parse(
        ProcessResult result, string engineName, Func<string, Exception> error, bool allowNotOk = false, string? partialKey = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException)
        {
            // Not the engine's JSON at all: uv could not start Python, or the script crashed early.
            var detail = result.ErrorText;
            throw error(string.IsNullOrWhiteSpace(detail)
                ? $"The {engineName} did not respond."
                : $"The {engineName} could not run: {detail}");
        }

        var ok = doc.RootElement.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
        if (ok || (allowNotOk && partialKey is not null && doc.RootElement.TryGetProperty(partialKey, out _)))
        {
            return doc;
        }

        var message = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        doc.Dispose();
        throw error(message ?? $"The {engineName} reported a problem.");
    }

    /// <summary>Prepares the Python environment (the first run downloads it). Safe to repeat.</summary>
    public static async Task EnsureEnvironmentAsync(
        IProcessRunner runner, ToolLocator tools, string repository, Func<string, Exception> error, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            tools.Require(Tool.Uv), ["sync", "--frozen"], repository, timeout: Timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw error($"The Python environment could not be prepared: {result.ErrorText}");
        }
    }
}
