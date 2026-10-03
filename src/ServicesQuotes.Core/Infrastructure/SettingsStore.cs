using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ServicesQuotes.Core.Infrastructure;

/// <summary>Per-user settings. Nothing here is a secret; sign-ins belong to git, gh and Claude.</summary>
public sealed class AppSettings
{
    /// <summary>Folder holding the clone of the quotes repository, once setup has made one.</summary>
    public string? RepositoryPath { get; set; }

    /// <summary>Claude Code session per quote number, so "continue" picks up the conversation.</summary>
    public Dictionary<string, string> ClaudeSessions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional Claude model alias (for example "opus"); empty uses the user's default.</summary>
    public string? ClaudeModel { get; set; }

    /// <summary>Minutes between background fetches.</summary>
    public int FetchIntervalMinutes { get; set; } = 5;

    /// <summary>Use the beta update channel (for trying a release before everyone gets it).</summary>
    public bool BetaUpdates { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON, atomically.</summary>
/// <remarks>
/// Written to a temporary file and moved into place, so a crash mid-write leaves the previous
/// settings intact rather than a truncated file that would send the user back through setup.
/// </remarks>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly ILogger<SettingsStore> _log;
    private readonly Lock _gate = new();

    public SettingsStore(string path, ILogger<SettingsStore>? log = null)
    {
        _path = path;
        _log = log ?? NullLogger<SettingsStore>.Instance;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options);
            if (settings is null)
            {
                return new AppSettings();
            }

            // A deserialized dictionary loses its comparer; quote numbers are case-insensitive.
            settings.ClaudeSessions = new Dictionary<string, string>(
                settings.ClaudeSessions ?? [], StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Settings at {Path} could not be read; starting from defaults.", _path);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
        }
    }
}
