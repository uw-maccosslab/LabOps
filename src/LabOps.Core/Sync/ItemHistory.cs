using System.Globalization;
using LabOps.Core.Repositories;

namespace LabOps.Core.Sync;

/// <summary>When each quote or project folder (with its experiments) last changed, from git history.</summary>
/// <remarks>
/// File times on disk are no use for this: a sync rewrites every file it brings in, so they show
/// when this computer got a change, not when anyone made it. The last commit touching a folder is
/// the same for everyone; its author date is when the change was made, which a rebase keeps. One
/// <c>git log</c> over the root folder answers for every item at once.
/// </remarks>
public static class ItemHistory
{
    private const string Marker = "@@";

    /// <summary>The last commit time of every item folder, with uncommitted changes counted as now.</summary>
    public static async Task<IReadOnlyDictionary<string, DateTimeOffset>> LastModifiedAsync(
        Repository repository, CancellationToken cancellationToken = default)
    {
        var profile = repository.Profile;
        var log = await repository.Git.RunAsync(
            ["log", $"--format={Marker}%aI", "--name-only", "--no-renames", "--", profile.RootFolder], cancellationToken)
            .ConfigureAwait(false);
        var times = log.Succeeded ? Parse(log.StandardOutput, profile) : new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        var status = await repository.Git.RunAsync(
            ["status", "--porcelain=v1", "--untracked-files=all", "--", profile.RootFolder], cancellationToken).ConfigureAwait(false);
        if (status.Succeeded)
        {
            var now = DateTimeOffset.Now;
            foreach (var line in status.StandardOutput.Split('\n'))
            {
                var path = line.TrimEnd('\r');
                if (path.Length > 3 && profile.IsItemPath(path[3..].Trim('"')))
                {
                    times[profile.ItemFolder(path[3..].Trim('"'))] = now;
                }
            }
        }

        return times;
    }

    /// <summary>Reads <c>git log --format=@@%aI --name-only</c>: newest first, so the first time seen per folder wins.</summary>
    internal static Dictionary<string, DateTimeOffset> Parse(string log, RepositoryProfile profile)
    {
        var times = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        DateTimeOffset? current = null;
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(Marker, StringComparison.Ordinal))
            {
                current = DateTimeOffset.TryParse(line[Marker.Length..], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                    ? t : null;
            }
            else if (current is { } time && profile.IsItemPath(line))
            {
                times.TryAdd(profile.ItemFolder(line), time);
            }
        }

        return times;
    }
}
