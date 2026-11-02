using System.Text.RegularExpressions;

namespace LabOps.Core.Engines;

/// <summary>Turns the progress uv writes while it prepares an engine into words for a lab user.</summary>
/// <remarks>
/// A first <c>uv sync</c> on a new computer downloads Python and every package, and on a slow
/// network or with antivirus scanning each file that can take minutes with nothing else on the
/// screen changing; people took it for stuck and closed Setup. uv says only when a stage starts or
/// ends, so Setup shows the last stage beside a clock. A line uv adds in a later version, or one
/// worded differently, describes nothing and the last stage stays shown, which is why nothing here
/// may fail.
/// </remarks>
public static partial class UvProgress
{
    /// <summary>The stage <paramref name="line"/> reports ("downloading Python (21.0 MiB)"), or null.</summary>
    public static string? Describe(string line)
    {
        var text = line.Trim();
        if (Downloading().Match(text) is { Success: true } downloading)
        {
            return $"downloading {Name(downloading.Groups["name"].Value)} ({downloading.Groups["size"].Value} {downloading.Groups["unit"].Value})";
        }

        if (Downloaded().Match(text) is { Success: true } downloaded)
        {
            return $"downloaded {Name(downloaded.Groups["name"].Value)}";
        }

        if (text.StartsWith("Creating virtual environment", StringComparison.Ordinal))
        {
            return "creating the Python environment";
        }

        // uv says "Prepared" once every package is downloaded and unpacked; installing comes next.
        if (Counted().Match(text) is { Success: true } counted)
        {
            var count = counted.Groups["count"].Value;
            var packages = count == "1" ? "1 package" : $"{count} packages";
            return counted.Groups["verb"].Value == "Prepared" ? $"installing {packages}" : $"installed {packages}";
        }

        return null;
    }

    private static string Name(string name) => name.StartsWith("cpython-", StringComparison.Ordinal) ? "Python" : name;

    [GeneratedRegex(@"^Downloading (?<name>\S+)(?: \(download\))? \((?<size>[\d.]+)(?<unit>[KMG]i?B)\)$")]
    private static partial Regex Downloading();

    [GeneratedRegex(@"^Downloaded (?<name>\S+)")]
    private static partial Regex Downloaded();

    [GeneratedRegex(@"^(?<verb>Prepared|Installed) (?<count>\d+) packages?\b")]
    private static partial Regex Counted();
}
