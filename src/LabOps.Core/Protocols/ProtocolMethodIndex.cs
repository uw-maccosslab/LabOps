using LabOps.Core.Acquisition;
using LabOps.Core.Projects;

namespace LabOps.Core.Protocols;

/// <summary>A published protocol version whose instrument method is exactly the one the raw files were acquired with.</summary>
public sealed record MethodMatch(ProtocolSummary Protocol, int Version, string MethodFile);

/// <summary>An experiment to record a protocol version on, at its data acquisition step (null when it has none).</summary>
public sealed record MethodRecording(ProjectSummary Project, ExperimentSummary Experiment, string? Step);

/// <summary>
/// Which published protocol version holds each instrument method, by the method's fingerprint
/// (<see cref="ThermoRawHeader.Fingerprint"/>), so an experiment's raw files can be matched to the
/// LC-MS protocol they followed. A version holds the instrument methods (.meth) its text links,
/// which never change once published, so a match is exact: any setting that differs is another
/// method, and so another protocol or version. LC-MS protocols have no deviations.
/// </summary>
public sealed class ProtocolMethodIndex
{
    private readonly Dictionary<string, List<MethodMatch>> _byFingerprint;

    private ProtocolMethodIndex(Dictionary<string, List<MethodMatch>> byFingerprint) => _byFingerprint = byFingerprint;

    public static ProtocolMethodIndex Empty { get; } = new([]);

    public int Count => _byFingerprint.Count;

    // A published method file never changes, so its fingerprint is read once per run. The key
    // holds the length and time too, so a file replaced by hand (which check refuses) is read again.
    private static readonly Dictionary<(string Path, long Length, DateTime Written), string?> Fingerprints = [];

    /// <summary>
    /// Reads every published version's instrument methods in a copy of LabOps-Protocols. A method
    /// file that is missing or not a Thermo method is left out, not an error: the protocols'
    /// own check reports a missing file.
    /// </summary>
    public static ProtocolMethodIndex Build(string repositoryPath, IEnumerable<ProtocolSummary> protocols)
    {
        var index = new Dictionary<string, List<MethodMatch>>(StringComparer.Ordinal);
        foreach (var protocol in protocols)
        {
            foreach (var version in protocol.Versions)
            {
                foreach (var name in version.Methods.Where(m => m.EndsWith(".meth", StringComparison.OrdinalIgnoreCase)))
                {
                    var path = Path.Combine(protocol.FolderPath(repositoryPath), "methods", name);
                    if (FingerprintOf(path) is { } fingerprint)
                    {
                        if (!index.TryGetValue(fingerprint, out var list))
                        {
                            index[fingerprint] = list = [];
                        }

                        list.Add(new MethodMatch(protocol, version.Version, name));
                    }
                }
            }
        }

        return new ProtocolMethodIndex(index);
    }

    /// <summary>
    /// The protocol version to record for a method: one in use before a retired one, then the
    /// newest version. Null when no published version holds it.
    /// </summary>
    public MethodMatch? Find(string? fingerprint) =>
        fingerprint is not null && _byFingerprint.TryGetValue(fingerprint, out var matches)
            ? matches.OrderBy(m => m.Protocol.IsRetired).ThenByDescending(m => m.Version).First()
            : null;

    /// <summary>
    /// The experiments to record a just-published version on: those whose raw files were acquired
    /// with exactly a method this index holds for that version, and that do not record that
    /// protocol yet, each with the step to record it on (<see cref="RecordStep"/>). An exact match
    /// leaves nothing for a person to judge, so publishing records it. An experiment that records
    /// another version of the protocol is left alone: a link replaces the one for the same protocol
    /// and step, so a version that only rewords the text would otherwise rewrite what every past
    /// experiment followed and drop its deviations.
    /// </summary>
    public IReadOnlyList<MethodRecording> ToRecord(ProjectList projects, string protocolId, int version) =>
    [
        .. projects.Labs.SelectMany(l => l.Projects).SelectMany(p => p.Experiments.Select(e => (Project: p, Experiment: e)))
            .Where(x => x.Experiment.Acquisition.Any(a => Find(a.Method) is { } m && m.Protocol.Id == protocolId && m.Version == version)
                        && !x.Experiment.Protocols.Any(l => l.Id == protocolId))
            .Select(x => new MethodRecording(x.Project, x.Experiment, RecordStep(x.Experiment))),
    ];

    /// <summary>
    /// Whether an experiment already records a protocol version holding this method, so there is
    /// nothing for Record protocol to do. Any version counts: a later version that only rewords
    /// the text holds the same method, and the version recorded is the one that was followed.
    /// </summary>
    public bool IsRecorded(ExperimentSummary experiment, string fingerprint) =>
        _byFingerprint.TryGetValue(fingerprint, out var matches)
        && experiment.Protocols.Any(l => matches.Any(m => m.Protocol.Id == l.Id && m.Version == l.Version));

    /// <summary>
    /// The kinds of step an LC-MS protocol is recorded on, in order of preference: the step the raw
    /// files were acquired in, else the one they were deposited in. Read raw files and Record
    /// protocol sit on the same step.
    /// </summary>
    public static IReadOnlyList<string> RecordStepKinds { get; } = ["data_acquisition", "data_deposited"];

    /// <summary>The step to record an LC-MS protocol on (see <see cref="RecordStepKinds"/>), or null when the experiment has none.</summary>
    public static string? RecordStep(ExperimentSummary experiment) =>
        RecordStepKinds.Select(k => experiment.Stages.FirstOrDefault(s => (s.Kind ?? s.Stage) == k)?.Stage).FirstOrDefault(s => s is not null);

    /// <summary>An instrument method file's fingerprint, or null when it cannot be read as one.</summary>
    public static string? FingerprintOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }

            var key = (file.FullName, file.Length, file.LastWriteTimeUtc);
            lock (Fingerprints)
            {
                if (Fingerprints.TryGetValue(key, out var known))
                {
                    return known;
                }
            }

            string? fingerprint;
            try
            {
                var texts = ThermoRawHeader.ReadMethodFileText(File.ReadAllBytes(path));
                fingerprint = texts.Count == 0 ? null : ThermoRawHeader.Fingerprint(texts);
            }
            catch (RawFormatException)
            {
                fingerprint = null;
            }

            lock (Fingerprints)
            {
                Fingerprints[key] = fingerprint;
            }

            return fingerprint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
