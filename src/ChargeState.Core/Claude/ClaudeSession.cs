using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ChargeState.Core.Processes;

namespace ChargeState.Core.Claude;

/// <summary>How to start Claude Code for one conversation.</summary>
public sealed record ClaudeSessionOptions
{
    public required string ClaudePath { get; init; }

    /// <summary>The quotes repository; Claude reads its CLAUDE.md and skills from here.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Continue an earlier conversation instead of starting fresh.</summary>
    public string? ResumeSessionId { get; init; }

    /// <summary>A model alias such as "opus"; null uses the user's own default.</summary>
    public string? Model { get; init; }

    public string? McpConfigPath { get; init; }

    /// <summary>Full MCP tool name that answers permission prompts, e.g. mcp__quotes-app__approve.</summary>
    public string? PermissionPromptTool { get; init; }

    public string PermissionMode { get; init; } = "acceptEdits";

    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    public IReadOnlyList<string> DisallowedTools { get; init; } = [];

    public string? AppendSystemPrompt { get; init; }

    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>The command line, built in one place so a test can check it.</summary>
    public IReadOnlyList<string> BuildArguments()
    {
        var args = new List<string>
        {
            "--print",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--permission-mode", PermissionMode,
        };

        if (ResumeSessionId is not null)
        {
            args.AddRange(["--resume", ResumeSessionId]);
        }

        if (Model is not null)
        {
            args.AddRange(["--model", Model]);
        }

        if (McpConfigPath is not null)
        {
            args.AddRange(["--mcp-config", McpConfigPath]);
        }

        if (PermissionPromptTool is not null)
        {
            args.AddRange(["--permission-prompt-tool", PermissionPromptTool]);
        }

        if (AllowedTools.Count > 0)
        {
            args.AddRange(["--allowedTools", string.Join(',', AllowedTools)]);
        }

        if (DisallowedTools.Count > 0)
        {
            args.AddRange(["--disallowedTools", string.Join(',', DisallowedTools)]);
        }

        if (AppendSystemPrompt is not null)
        {
            args.AddRange(["--append-system-prompt", AppendSystemPrompt]);
        }

        return args;
    }
}

/// <summary>The pipes a session talks over. A process in the app; in-memory pipes in tests.</summary>
public interface IClaudeTransport : IAsyncDisposable
{
    TextWriter Input { get; }

    TextReader Output { get; }

    /// <summary>Completes with the exit code when the process ends.</summary>
    Task<int> Exited { get; }

    /// <summary>The last part of stderr, for explaining a failure.</summary>
    string ErrorTail { get; }

    void Stop();
}

/// <summary>
/// One conversation with Claude Code, running headless with stream-json in both directions.
/// </summary>
/// <remarks>
/// One long-lived process per conversation rather than one per message: Claude keeps its
/// context, and the app's MCP tools stay connected for follow-up questions. Messages are written
/// to stdin as JSON lines; events are read from stdout on a background task and raised through
/// <see cref="EventReceived"/>, on that background thread.
/// </remarks>
public sealed class ClaudeSession : IAsyncDisposable
{
    private readonly IClaudeTransport _transport;
    private readonly Task _reader;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _busy;

    public ClaudeSession(IClaudeTransport transport)
    {
        _transport = transport;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Raised for every event, on a background thread.</summary>
    public event Action<ClaudeEvent>? EventReceived;

    /// <summary>Known once Claude reports it; store it to resume the conversation later.</summary>
    public string? SessionId { get; private set; }

    /// <summary>True from sending a message until Claude finishes responding to it.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public bool HasEnded { get; private set; }

    /// <summary>Starts a Claude Code process for <paramref name="options"/>.</summary>
    public static ClaudeSession Start(ClaudeSessionOptions options, ProcessRunner runner) =>
        new(ProcessTransport.Start(options, runner));

    /// <summary>Sends the user's message. Claude responds through <see cref="EventReceived"/>.</summary>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(HasEnded, this);
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content = new[] { new { type = "text", text } } },
        });

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _busy, 1);
            await _transport.Input.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _transport.Input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Stops Claude immediately, for the Stop button.</summary>
    public void Stop() => _transport.Stop();

    /// <summary>Ends the conversation politely: closing stdin lets Claude finish and exit.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _transport.Input.Close();
        }
        catch (IOException)
        {
            // Already gone.
        }

        var finished = await Task.WhenAny(_reader, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        if (finished != _reader)
        {
            _transport.Stop();
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _transport.Output.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                foreach (var e in StreamJsonParser.Parse(line))
                {
                    switch (e)
                    {
                        case SessionStarted started:
                            SessionId = started.SessionId;
                            break;
                        case TurnFinished finished:
                            SessionId = finished.SessionId ?? SessionId;
                            Volatile.Write(ref _busy, 0);
                            break;
                    }

                    EventReceived?.Invoke(e);
                }
            }
        }
        catch (IOException)
        {
            // The pipe closed under us; the exit below reports why.
        }
        catch (ObjectDisposedException)
        {
            // Disposed while reading.
        }

        var code = await _transport.Exited.ConfigureAwait(false);
        HasEnded = true;
        Volatile.Write(ref _busy, 0);
        EventReceived?.Invoke(new SessionEnded(code, _transport.ErrorTail));
    }
}

/// <summary>A real Claude Code process.</summary>
internal sealed class ProcessTransport : IClaudeTransport
{
    private const int ErrorTailLength = 4000;

    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    private ProcessTransport(Process process)
    {
        _process = process;
        Exited = WaitAsync();
    }

    public TextWriter Input => _process.StandardInput;

    public TextReader Output => _process.StandardOutput;

    public Task<int> Exited { get; }

    public string ErrorTail
    {
        get
        {
            lock (_stderr)
            {
                return _stderr.ToString().Trim();
            }
        }
    }

    public static ProcessTransport Start(ClaudeSessionOptions options, ProcessRunner runner)
    {
        var info = runner.CreateStartInfo(options.ClaudePath, options.BuildArguments(), options.WorkingDirectory, options.Environment);
        info.RedirectStandardInput = true;

        // No byte-order mark: Claude parses each stdin line as JSON, and a BOM would make the
        // first line invalid.
        info.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Start();
        var transport = new ProcessTransport(process);
        process.ErrorDataReceived += (_, e) => transport.AppendError(e.Data);
        process.BeginErrorReadLine();
        return transport;
    }

    public void Stop()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public ValueTask DisposeAsync()
    {
        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<int> WaitAsync()
    {
        await _process.WaitForExitAsync().ConfigureAwait(false);
        return _process.ExitCode;
    }

    private void AppendError(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_stderr)
        {
            _stderr.AppendLine(line);
            if (_stderr.Length > ErrorTailLength)
            {
                _stderr.Remove(0, _stderr.Length - ErrorTailLength);
            }
        }
    }
}
