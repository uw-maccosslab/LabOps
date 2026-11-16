using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using LabOps.Core.Panorama;

namespace LabOps.Core.Acquisition;

/// <summary>What one raw file says about its acquisition.</summary>
/// <param name="Folder">The Panorama folder it is in (/MacCoss/.../RawFiles/Plate_1).</param>
/// <param name="Name">Its file name. Shown to the person when it cannot be read; never recorded.</param>
/// <param name="MethodFile">The instrument method's file name, or null when the file names none.</param>
/// <param name="Method">A fingerprint of the embedded method's text (ThermoRawHeader.Fingerprint).</param>
public sealed record RawFileFacts(string Folder, string Name, DateTime Acquired, string? MethodFile, string Method, RawInstrument Instrument);

/// <summary>A raw file that could not be read, and why, for the person.</summary>
public sealed record RawFileProblem(string Folder, string Name, string Message);

/// <summary>What reading a raw data folder found.</summary>
public sealed record RawFolderRead(IReadOnlyList<RawFileFacts> Files, IReadOnlyList<RawFileProblem> Problems)
{
    /// <summary>
    /// The engine's input (labops projects acquisition --from): each file's folder, acquisition
    /// time, method and instrument, in the order acquired. File names stay out, since they often
    /// carry sample identifiers.
    /// </summary>
    public string ToEngineJson() =>
        new JsonObject
        {
            ["files"] = new JsonArray([.. Files.OrderBy(f => f.Acquired).Select(f => (JsonNode)new JsonObject
            {
                ["folder"] = f.Folder,
                ["acquired"] = f.Acquired.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                ["method_file"] = f.MethodFile,
                ["method"] = f.Method,
                ["model"] = f.Instrument.Model ?? f.Instrument.Name,
                ["serial"] = f.Instrument.Serial,
            })]),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>The raw files found in an experiment's raw data folders, and the folders not on Panorama yet.</summary>
public sealed record RawFolderListing(IReadOnlyList<PanoramaEntry> Files, IReadOnlyList<string> Missing);

/// <summary>
/// Reads every Thermo .raw file in a raw data folder on Panorama, and its subfolders, for when
/// and how it was acquired, without downloading it: about a megabyte of each, by range requests
/// (the file's start, which holds the embedded method, and the mass spectrometer's instrument
/// block). A file of 5 GB is read in three requests.
/// </summary>
public sealed class PanoramaRawReader(PanoramaClient client)
{
    /// <summary>How many files are read at once: enough to hide each request's wait, few enough to be a polite client.</summary>
    public const int Parallel = 6;

    /// <summary>How deep subfolders are followed (RawFiles/Plate_1 is one level).</summary>
    public const int MaxDepth = 3;

    /// <summary>
    /// The raw files in the folders and their subfolders. A recorded folder that is not on
    /// Panorama yet (it can be recorded before acquisition starts) is reported as missing rather
    /// than failing the others.
    /// </summary>
    /// <param name="folders">Panorama folders as an experiment records them (/MacCoss/.../@files/RawFiles).</param>
    public async Task<RawFolderListing> ListAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        var files = new List<PanoramaEntry>();
        var missing = new List<string>();
        foreach (var folder in folders)
        {
            var entries = await client.TryListAsync(PanoramaPaths.ToWebDav(folder), cancellationToken).ConfigureAwait(false);
            if (entries is null)
            {
                missing.Add(folder);
                continue;
            }

            await WalkAsync(entries, 0).ConfigureAwait(false);
        }

        return new RawFolderListing(files, missing);

        async Task WalkAsync(IReadOnlyList<PanoramaEntry> entries, int depth)
        {
            foreach (var entry in entries)
            {
                if (entry.IsFolder)
                {
                    if (depth < MaxDepth)
                    {
                        await WalkAsync(await client.ListAsync(entry.Path, cancellationToken).ConfigureAwait(false), depth + 1).ConfigureAwait(false);
                    }
                }
                else if (entry.Name.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(entry);
                }
            }
        }
    }

    /// <summary>
    /// Reads the files; one that cannot be read is a problem, not a failure. A sign-in Panorama
    /// does not accept stops the reading, since every other file would fail the same way.
    /// </summary>
    public async Task<RawFolderRead> ReadAsync(IReadOnlyList<PanoramaEntry> files, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var read = new ConcurrentBag<RawFileFacts>();
        var problems = new ConcurrentBag<RawFileProblem>();
        var done = 0;
        // Counting and reporting under one lock, so the counts reach the person in order (with
        // six files at once, a later count could otherwise be reported before an earlier one).
        var gate = new Lock();
        await System.Threading.Tasks.Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = cancellationToken },
            async (file, token) =>
            {
                var folder = PanoramaPaths.ToFolder(file.Path[..file.Path.LastIndexOf('/')]);
                try
                {
                    read.Add(await ReadFileAsync(file, folder, token).ConfigureAwait(false));
                }
                catch (RawFormatException ex)
                {
                    problems.Add(new RawFileProblem(folder, file.Name, $"{file.Name} {ex.Message}."));
                }
                catch (PanoramaException ex) when (!ex.IsSignInProblem)
                {
                    problems.Add(new RawFileProblem(folder, file.Name, ex.Message));
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or PanoramaException))
                {
                    // The bytes are a file's, not ours: a damaged or half-uploaded one can trip the
                    // compound file reader in ways no list of exception types covers, and one such
                    // file must not stop the reading of the hundreds beside it.
                    problems.Add(new RawFileProblem(folder, file.Name, $"{file.Name} could not be read ({ex.Message})."));
                }

                lock (gate)
                {
                    progress?.Report(++done);
                }
            }).ConfigureAwait(false);

        return new RawFolderRead([.. read.OrderBy(f => f.Acquired)], [.. problems.OrderBy(p => p.Folder, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal)]);
    }

    private async Task<RawFileFacts> ReadFileAsync(PanoramaEntry file, string folder, CancellationToken cancellationToken)
    {
        var first = await client.ReadRangeAsync(file.Path, 0, ThermoRawHeader.StartLength, cancellationToken).ConfigureAwait(false);
        var layout = ThermoRawHeader.ReadLayout(first.Bytes, first.FileLength);
        var start = first.Bytes;
        var methodEnd = ThermoRawHeader.MethodEnd(layout);
        var blockAt = layout.MassSpectrometer!.RunHeader + ThermoRawHeader.InstrumentOffset;
        if (blockAt >= layout.FileLength)
        {
            throw new RawFormatException("ends before its instrument description; it may still be uploading");
        }

        // The rest of the start and the instrument block do not depend on each other, so both
        // are asked for at once: two waits per file instead of three.
        var blockRead = client.ReadRangeAsync(file.Path, blockAt, (int)Math.Min(ThermoRawHeader.InstrumentLength, layout.FileLength - blockAt), cancellationToken);
        var restRead = methodEnd > start.Length
            ? client.ReadRangeAsync(file.Path, start.Length, (int)(methodEnd - start.Length), cancellationToken)
            : null;
        await Task.WhenAll(restRead is null ? new[] { blockRead } : new[] { blockRead, restRead }).ConfigureAwait(false);
        if (restRead is not null)
        {
            start = [.. start, .. (await restRead.ConfigureAwait(false)).Bytes];

            // The method starts a few KB in on every file seen, but a long header could push it
            // past the first read; it is then found in the rest, rather than taken as missing,
            // which would give every such file the same empty fingerprint.
            if (layout.MethodOffset < 0)
            {
                layout = ThermoRawHeader.ReadLayout(start, layout.FileLength);
            }
        }

        var block = await blockRead.ConfigureAwait(false);
        var methods = ThermoRawHeader.ReadMethods(start, layout);
        return new RawFileFacts(folder, file.Name, layout.Acquired, ThermoRawHeader.ReadMethodFile(start, layout),
            ThermoRawHeader.Fingerprint(methods), ThermoRawHeader.ReadInstrument(block.Bytes));
    }
}
