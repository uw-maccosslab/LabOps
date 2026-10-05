using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LabOps.Core.Processes;

namespace LabOps.Core.Claude;

/// <summary>How a sign-in to Claude Code ended.</summary>
/// <param name="Succeeded"><c>claude auth login</c> finished and said so.</param>
/// <param name="Cancelled">The person stopped waiting.</param>
/// <param name="Output">What it printed, for the log and for a message when it did not finish.</param>
public sealed record ClaudeLoginResult(bool Succeeded, bool Cancelled, string Output);

/// <summary>
/// Signs in to Claude Code without a console window. <c>claude auth login</c> opens the browser,
/// and when the person chooses Authorize the page sends the answer back to this computer, so
/// nothing has to be typed or read in a console (unlike GitHub's sign-in, which shows a code).
/// </summary>
/// <remarks>
/// Its input is closed: if Claude Code ever falls back to asking for a code to be pasted, the
/// run ends instead of waiting for input nobody can see, and the app offers the console instead.
/// The link it prints is passed on, so the app can show it in case the browser did not open.
/// </remarks>
public sealed partial class ClaudeLogin(ToolLocator tools)
{
    /// <summary>How long to wait for the person to finish in the browser.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <param name="onLink">Called with the sign-in page's address when Claude Code prints it.</param>
    /// <param name="cancellationToken">Stops waiting, and ends the sign-in.</param>
    public async Task<ClaudeLoginResult> SignInAsync(Action<string>? onLink, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(tools.Require(Tool.Claude))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "auth", "login", "--claudeai" })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Claude Code's sign-in could not start.");
        process.StandardInput.Close();

        var output = new StringBuilder();
        var linked = false;
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                lock (output)
                {
                    output.AppendLine(line);
                    if (linked || LinkIn(line) is not { } link)
                    {
                        continue;
                    }

                    linked = true;
                    onLink?.Invoke(link);
                }
            }
        }

        var reading = Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError));
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended on its own meanwhile.
            }

            lock (output)
            {
                return new ClaudeLoginResult(false, cancellationToken.IsCancellationRequested, output.ToString());
            }
        }

        await reading.ConfigureAwait(false);
        return new ClaudeLoginResult(process.ExitCode == 0, false, output.ToString());
    }

    /// <summary>The sign-in page's address in a line Claude Code printed, or null.</summary>
    internal static string? LinkIn(string line) =>
        SignInLink().Match(line) is { Success: true } m ? m.Value.TrimEnd('.', ',', ')') : null;

    [GeneratedRegex(@"https://(?:claude\.ai|console\.anthropic\.com|platform\.claude\.com)/\S+")]
    private static partial Regex SignInLink();
}
