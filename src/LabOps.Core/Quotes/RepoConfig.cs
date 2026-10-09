using LabOps.Core.Infrastructure;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LabOps.Core.Quotes;

/// <summary>
/// Settings shared by every user, read from <c>config/app.yaml</c> in a repository (approvers
/// apply to the quotes repository only, the dashboard to the projects repository).
/// </summary>
/// <remarks>
/// They live in the repository rather than in each user's settings so that one commit changes
/// them for the whole lab, and so the history shows who changed them.
/// </remarks>
/// <param name="Dashboard">Where the lab dashboard goes on Panorama (a lab-only folder), or null for nowhere.</param>
public sealed record RepoConfig(IReadOnlyList<string> Approvers, Version MinAppVersion, LabOps.Core.Projects.WikiLocation? Dashboard = null)
{
    public static RepoConfig Default { get; } = new([], new Version(0, 0, 0));

    /// <summary>Reads config/app.yaml; a missing or unreadable file gives the defaults.</summary>
    public static RepoConfig Load(string repositoryPath)
    {
        var path = Path.Combine(repositoryPath, "config", "app.yaml");
        if (!File.Exists(path))
        {
            return Default;
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return Default;
        }
    }

    internal static RepoConfig Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var raw = deserializer.Deserialize<RawConfig?>(yaml) ?? new RawConfig();
        var folder = raw.Dashboard?.Folder?.Trim();
        return new RepoConfig(
            (raw.Approvers ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
            AppInfo.ParseVersion(raw.MinAppVersion),
            string.IsNullOrEmpty(folder?.Trim('/')) ? null
                : new LabOps.Core.Projects.WikiLocation("/" + folder.Trim('/'), string.IsNullOrWhiteSpace(raw.Dashboard!.Page) ? null : raw.Dashboard.Page.Trim()));
    }

    /// <summary>Whether this GitHub login may send quotes from the app.</summary>
    public bool IsApprover(string? login) =>
        !string.IsNullOrWhiteSpace(login)
        && Approvers.Any(a => string.Equals(a, login, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the repository needs a newer app than <paramref name="appVersion"/>.</summary>
    public bool RequiresNewerThan(Version appVersion) => appVersion < MinAppVersion;

    private sealed class RawConfig
    {
        public List<string>? Approvers { get; set; }

        public string? MinAppVersion { get; set; }

        public RawDashboard? Dashboard { get; set; }
    }

    private sealed class RawDashboard
    {
        public string? Folder { get; set; }

        public string? Page { get; set; }
    }
}
