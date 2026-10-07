namespace LabOps.Core.Infrastructure;

/// <summary>
/// Every on-disk location the application uses, resolved in one place.
/// </summary>
/// <remarks>
/// Everything lives under <c>%LOCALAPPDATA%\LabOps</c>: local, not roaming, because the
/// logs and Claude's per-session files are machine specific. Projects and quotes live in the git
/// clones the user chose during setup, never here.
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
        AttachmentsDirectory = Path.Combine(Root, "attachments");
    }

    /// <summary>The application data root.</summary>
    public string Root { get; }

    /// <summary>Directory holding rolling log files.</summary>
    public string LogDirectory { get; }

    /// <summary>Path template passed to the rolling file sink.</summary>
    public string LogFileTemplate => Path.Combine(LogDirectory, "labops-.log");

    /// <summary>User settings. Never contains credentials.</summary>
    public string SettingsFile { get; }

    /// <summary>Per-session MCP configuration files handed to Claude Code.</summary>
    public string SessionDirectory { get; }

    /// <summary>
    /// Copies of the files a person attaches in the chat, which Claude may read. Outside every
    /// repository, so an attachment is never committed or shared.
    /// </summary>
    public string AttachmentsDirectory { get; }

    /// <summary>Held open while the application runs, so a second copy can tell there is one.</summary>
    public string InstanceLockFile => Path.Combine(Root, "instance.lock");

    /// <summary>The data folder of the app's earlier name, ChargeState, beside this one.</summary>
    public static string LegacyRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.LegacyFolderName);

    /// <summary>
    /// The first time LabOps starts, it takes over ChargeState's settings (the clones, the Claude
    /// sessions to resume) and its per-session files, so nobody goes through setup again. The
    /// folder is copied, not moved, so ChargeState keeps working until it is uninstalled; logs and
    /// the browser cache stay behind. Returns whether anything was copied.
    /// </summary>
    public bool AdoptLegacy(string legacyRoot)
    {
        var legacySettings = Path.Combine(legacyRoot, "settings.json");
        if (File.Exists(SettingsFile) || !File.Exists(legacySettings))
        {
            return false;
        }

        Directory.CreateDirectory(Root);
        File.Copy(legacySettings, SettingsFile);
        var sessions = Path.Combine(legacyRoot, "claude");
        if (Directory.Exists(sessions))
        {
            foreach (var file in Directory.EnumerateFiles(sessions, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(SessionDirectory, Path.GetRelativePath(sessions, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }

        return true;
    }

    /// <summary>Creates the directories that must exist before anything else runs.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(SessionDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
    }

    /// <summary>
    /// Deletes the attachments of conversations from an earlier run. Each conversation's folder goes
    /// when it ends; these are what a closed app or a crash left behind, and no conversation outlives
    /// the app.
    /// </summary>
    public void ClearAttachments()
    {
        if (!Directory.Exists(AttachmentsDirectory))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(AttachmentsDirectory))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open by another program: next time.
            }
        }
    }
}
