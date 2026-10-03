namespace ServicesQuotes.Core.Infrastructure;

/// <summary>
/// Every on-disk location the application uses, resolved in one place.
/// </summary>
/// <remarks>
/// Everything lives under <c>%LOCALAPPDATA%\ServicesQuotes</c>: local, not roaming, because the
/// logs and Claude's per-session files are machine specific. The quotes themselves live in the
/// git clone the user chose during setup, never here.
/// </remarks>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.FolderName);

        LogDirectory = Path.Combine(Root, "logs");
        SettingsFile = Path.Combine(Root, "settings.json");
        SessionDirectory = Path.Combine(Root, "claude");
    }

    /// <summary>The application data root.</summary>
    public string Root { get; }

    /// <summary>Directory holding rolling log files.</summary>
    public string LogDirectory { get; }

    /// <summary>Path template passed to the rolling file sink.</summary>
    public string LogFileTemplate => Path.Combine(LogDirectory, "servicesquotes-.log");

    /// <summary>User settings. Never contains credentials.</summary>
    public string SettingsFile { get; }

    /// <summary>Per-session MCP configuration files handed to Claude Code.</summary>
    public string SessionDirectory { get; }

    /// <summary>Held open while the application runs, so a second copy can tell there is one.</summary>
    public string InstanceLockFile => Path.Combine(Root, "instance.lock");

    /// <summary>The default place to clone the quotes repository during setup.</summary>
    public static string DefaultClonePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "services-quotes");

    /// <summary>Creates the directories that must exist before anything else runs.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(SessionDirectory);
    }
}
