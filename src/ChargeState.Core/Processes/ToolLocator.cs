namespace ChargeState.Core.Processes;

/// <summary>The external programs the app relies on.</summary>
public enum Tool
{
    Git,
    GitHubCli,
    Claude,
    Uv,
}

/// <summary>
/// Finds git, gh, claude and uv, including ones installed after the app started.
/// </summary>
/// <remarks>
/// Setup installs tools while the app is running, and an installer updates the PATH stored in
/// the registry, not the PATH this process inherited. So the search path is rebuilt from the
/// registry values on every lookup, plus the folders each installer is known to use.
/// uv ships inside the app (tools\uv.exe beside the executable), so Python needs no install.
/// </remarks>
public sealed class ToolLocator
{
    private static readonly Dictionary<Tool, string> ExecutableNames = new()
    {
        [Tool.Git] = "git.exe",
        [Tool.GitHubCli] = "gh.exe",
        [Tool.Claude] = "claude.exe",
        [Tool.Uv] = "uv.exe",
    };

    private readonly string _appDirectory;

    public ToolLocator(string? appDirectory = null)
    {
        _appDirectory = appDirectory ?? AppContext.BaseDirectory;
    }

    /// <summary>The PATH handed to every child process.</summary>
    public string SearchPath => string.Join(Path.PathSeparator, SearchDirectories());

    /// <summary>Full path of the tool, or null when it is not installed.</summary>
    public string? Find(Tool tool)
    {
        var name = ExecutableNames[tool];
        foreach (var directory in SearchDirectories())
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry; skip it.
            }
        }

        return null;
    }

    /// <summary>Full path of the tool, or a message saying which setup step installs it.</summary>
    public string Require(Tool tool) =>
        Find(tool) ?? throw new ToolMissingException(tool);

    private IEnumerable<string> SearchDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var known = new[]
        {
            Path.Combine(_appDirectory, "tools"),
            Path.Combine(home, ".local", "bin"),
            Path.Combine(programFiles, "Git", "cmd"),
            Path.Combine(programFiles, "GitHub CLI"),
            Path.Combine(localAppData, "Programs", "Git", "cmd"),
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links"),
        };

        var fromEnvironment = new[]
        {
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        }
        .Where(p => !string.IsNullOrEmpty(p))
        .SelectMany(p => p!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Select(Environment.ExpandEnvironmentVariables);

        return known.Concat(fromEnvironment).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>A required program is not installed.</summary>
public sealed class ToolMissingException(Tool tool)
    : Exception($"{Describe(tool)} is not installed. Open Setup to install it.")
{
    public Tool Tool { get; } = tool;

    public static string Describe(Tool tool) => tool switch
    {
        Tool.Git => "Git",
        Tool.GitHubCli => "The GitHub command-line tool (gh)",
        Tool.Claude => "Claude Code",
        Tool.Uv => "uv",
        _ => tool.ToString(),
    };
}
