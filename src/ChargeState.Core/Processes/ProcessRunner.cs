using System.Diagnostics;
using System.Text;

namespace ChargeState.Core.Processes;

/// <summary>What a finished process produced.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The most useful text to show when the command failed: stderr, else stdout.</summary>
    public string ErrorText => string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();
}

/// <summary>Runs a command to completion. An interface so tests can substitute a fake.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs external tools (git, gh, uv) without a console window, capturing UTF-8 output.
/// </summary>
/// <remarks>
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never a joined string, so a
/// quote title or a commit message containing quotes or spaces cannot change what is run.
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ToolLocator _tools;

    public ProcessRunner(ToolLocator tools)
    {
        _tools = tools;
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var info = CreateStartInfo(fileName, arguments, workingDirectory, environment);

        using var process = new Process { StartInfo = info };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (stdout) { stdout.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (stderr) { stderr.AppendLine(e.Data); } } };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ProcessResult(-1, "", $"{Path.GetFileName(fileName)} could not be started: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } t)
        {
            limit.CancelAfter(t);
        }

        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, stdout.ToString(), $"{Path.GetFileName(fileName)} did not finish within {timeout}.");
        }

        // WaitForExitAsync returns once the process exits; this waits for the output pipes to drain.
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Builds the start info shared by short commands and the long-lived Claude process.</summary>
    public ProcessStartInfo CreateStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Tools installed after this app started are on the registry PATH but not on ours.
        info.Environment["PATH"] = _tools.SearchPath;

        // Nothing here may stop to ask a question: there is no console to answer it in, so a
        // prompt would look like a hang. Each tool is told to fail instead.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = "never";
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        info.Environment["NO_COLOR"] = "1";
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null)
                {
                    info.Environment.Remove(key);
                }
                else
                {
                    info.Environment[key] = value;
                }
            }
        }

        return info;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
