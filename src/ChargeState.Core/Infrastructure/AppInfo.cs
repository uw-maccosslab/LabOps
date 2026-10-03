using System.Reflection;
using System.Runtime.InteropServices;

namespace ChargeState.Core.Infrastructure;

/// <summary>
/// Identity of the running build. The version is read from the assembly, which gets it from
/// Directory.Build.props, so the About text and the update check can never disagree.
/// </summary>
public static class AppInfo
{
    /// <summary>Product name shown in the UI and used for the app data folder.</summary>
    public const string ProductName = "ChargeState";

    /// <summary>Folder name under %LOCALAPPDATA%. No space, so paths stay easy to type.</summary>
    public const string FolderName = "ChargeState";

    /// <summary>Velopack package identifier. Must stay stable across releases.</summary>
    public const string PackageId = "MacCossLab.ChargeState";

    /// <summary>Repository the update feed is served from.</summary>
    public const string RepositoryUrl = "https://github.com/uw-maccosslab/ChargeState";

    /// <summary>The quotes repository this app works on.</summary>
    public const string QuotesRepository = "uw-maccosslab/services-quotes";

    /// <summary>Informational version, e.g. <c>26.1.0</c>, without build metadata.</summary>
    public static string InformationalVersion { get; } = ResolveInformationalVersion();

    /// <summary>Parsed <see cref="InformationalVersion"/>, for comparison with min_app_version.</summary>
    public static Version Version { get; } = ParseVersion(InformationalVersion);

    /// <summary>Runtime identifier of this build, e.g. <c>win-x64</c>.</summary>
    public static string RuntimeIdentifier { get; } =
        $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Parses a CalVer string such as <c>26.1.0</c> or <c>26.2.0-beta.1</c>, ignoring any
    /// prerelease suffix. Anything unparseable becomes 0.0.0.
    /// </summary>
    public static Version ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Version(0, 0, 0);
        }

        text = text.Trim();
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            text = text[..dash];
        }

        if (!Version.TryParse(text, out var parsed))
        {
            return new Version(0, 0, 0);
        }

        return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
    }

    private static string ResolveInformationalVersion()
    {
        var raw = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }
}
