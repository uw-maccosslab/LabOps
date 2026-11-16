// Ported from maccosslab-raw-method-check (src/RawMethodCheck/MethodTextParser.cs and the method
// classes of Report.cs), Copyright 2026 University of Washington, Apache License 2.0. LabOps keeps
// its own copy so it reads a raw file's method without shipping RawMethodCheck, ProteoWizard or
// Thermo's RawFileReader: the method text comes from ThermoRawHeader.ReadMethods. The two copies
// are tested on the same method texts (Fixtures/method-text); a fix to either belongs in both.

using System.Globalization;
using System.Text.RegularExpressions;

namespace LabOps.Core.Acquisition;

/// <summary>
/// What a raw file's embedded method says, parsed: the mass spectrometer's method summary and the
/// LC's script, each null when the method has none (or none LabOps recognizes).
/// </summary>
public sealed record ThermoMethod(MsMethodSection? Ms, LcSection? Lc)
{
    /// <summary>
    /// Parses each device's method text, telling the mass spectrometer's from the LC's the way
    /// RawMethodCheck does: by the method editor's storage name, or failing that by the text.
    /// </summary>
    public static ThermoMethod Read(IEnumerable<RawMethodText> methods)
    {
        var list = methods.ToList();
        var ms = list.FirstOrDefault(m => Classify(m) == "ms");
        var lc = list.FirstOrDefault(m => Classify(m) == "lc");
        return new ThermoMethod(
            ms is null ? null : MethodTextParser.ParseMsMethod(ms.Text),
            lc is null ? null : MethodTextParser.ParseLcMethod(lc.Text, lc.Device));
    }

    private static string Classify(RawMethodText method)
    {
        var (name, text) = (method.Device, method.Text);
        if (name.StartsWith("TNG", StringComparison.OrdinalIgnoreCase) || text.Contains("Method Summary", StringComparison.OrdinalIgnoreCase))
        {
            return "ms";
        }

        if (name.Contains("Sii", StringComparison.OrdinalIgnoreCase) || text.Contains("---- Script ----", StringComparison.Ordinal)
            || name.Contains("nLC", StringComparison.OrdinalIgnoreCase) || name.Contains("LC", StringComparison.Ordinal))
        {
            return "lc";
        }

        return "other";
    }
}

/// <summary>
/// Parsers for the instrument method text Thermo embeds in a .raw file: the MS method summary
/// written by the method editor ("TNG-*" sections: Orbitrap Astral, Exploris, Eclipse, Stellar,
/// ...) and the LC script written by the Thermo SII driver (Vanquish Neo, UltiMate 3000, Vanquish
/// Horizon and Flex).
/// </summary>
public static partial class MethodTextParser
{
    [GeneratedRegex(@"^Experiment\s+(\d+)$")]
    private static partial Regex ExperimentLine();

    [GeneratedRegex(@"^Scan Event\s+(\d+)$")]
    private static partial Regex ScanEventLine();

    [GeneratedRegex(@"^(Scan|Filter)\s+(\S.*)$")]
    private static partial Regex BlockLine();

    [GeneratedRegex(@"Last Modified:\s*(.+?)\s+by\s")]
    private static partial Regex LastModifiedText();

    [GeneratedRegex(@"^(initial|\d+(?:\.\d+)?\s*\[min\])\s*(.*)$")]
    private static partial Regex LcTimeLine();

    [GeneratedRegex(@"^\s+([^\s:][^:]*?):\s(.*)$")]
    private static partial Regex LcProperty();

    [GeneratedRegex(@"^\s*""?(-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)")]
    private static partial Regex LeadingNumberText();

    /// <summary>Headers that open a block without the "Scan" or "Filter" word.</summary>
    private static readonly HashSet<string> ShallowHeaders = new(StringComparer.OrdinalIgnoreCase) { "Data Dependent Properties" };

    // -- MS method summary ------------------------------------------------------------------------

    public static MsMethodSection ParseMsMethod(string text)
    {
        var method = new MsMethodSection();
        MsExperiment? experiment = null;
        MsMethodBlock? block = null;
        int? scanEvent = null;
        var inGlobal = false;
        List<string>? tableHeader = null;
        List<List<string>>? tableRows = null;

        foreach (var rawLine in SplitLines(text))
        {
            var t = rawLine.Trim();

            if (tableRows != null)
            {
                if (t.Contains("End Mass List Table", StringComparison.OrdinalIgnoreCase))
                {
                    var target = block ?? LastBlock(experiment);
                    if (target != null)
                    {
                        target.MassList = new MassListInfo { Columns = tableHeader ?? [], Rows = tableRows };
                    }

                    tableRows = null;
                    tableHeader = null;
                    continue;
                }

                if (t.Length == 0)
                {
                    continue;
                }

                var cells = t.Split('|').Select(c => c.Trim()).ToList();
                while (cells.Count > 0 && cells[^1].Length == 0)
                {
                    cells.RemoveAt(cells.Count - 1);
                }

                if (tableHeader == null)
                {
                    tableHeader = cells;
                }
                else
                {
                    tableRows.Add(cells);
                }

                continue;
            }

            if (t.Length == 0)
            {
                continue;
            }

            if (t.Contains("Mass List Table", StringComparison.OrdinalIgnoreCase) && t.StartsWith('>'))
            {
                tableRows = [];
                continue;
            }

            if (method.Title == null && t.EndsWith("Method Summary", StringComparison.OrdinalIgnoreCase))
            {
                method.Title = t;
                continue;
            }

            if (t.StartsWith("Creator:", StringComparison.OrdinalIgnoreCase))
            {
                var m = LastModifiedText().Match(t);
                if (m.Success)
                {
                    method.LastModified = m.Groups[1].Value.Trim();
                }

                continue;
            }

            if (t.Equals("Global Settings", StringComparison.OrdinalIgnoreCase))
            {
                inGlobal = true;
                continue;
            }

            var em = ExperimentLine().Match(t);
            if (em.Success)
            {
                experiment = new MsExperiment { Number = int.Parse(em.Groups[1].Value, CultureInfo.InvariantCulture) };
                method.Experiments.Add(experiment);
                block = null;
                scanEvent = null;
                inGlobal = false;
                continue;
            }

            var sem = ScanEventLine().Match(t);
            if (sem.Success)
            {
                scanEvent = int.Parse(sem.Groups[1].Value, CultureInfo.InvariantCulture);
                block = null;
                continue;
            }

            var eq = t.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                var bm = BlockLine().Match(t);
                if (bm.Success || ShallowHeaders.Contains(t))
                {
                    experiment ??= AddExperiment(method, 0);
                    block = new MsMethodBlock
                    {
                        Kind = bm.Success ? bm.Groups[1].Value : t,
                        Name = bm.Success ? bm.Groups[2].Value.Trim() : string.Empty,
                        ScanEvent = scanEvent,
                    };
                    experiment.Blocks.Add(block);
                    inGlobal = false;
                    continue;
                }

                // A setting written as a bare phrase ("Use Static Source Gasses").
                if (block != null)
                {
                    block.Flags.Add(t);
                }
                else if (experiment != null && !inGlobal)
                {
                    experiment.Settings[UniqueKey(experiment.Settings, t)] = "true";
                }
                else
                {
                    method.GlobalFlags.Add(t);
                }

                continue;
            }

            var key = t[..eq].Trim();
            var value = t[(eq + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var settings = block?.Settings ?? (experiment != null && !inGlobal ? experiment.Settings : method.GlobalSettings);
            settings[UniqueKey(settings, key)] = value;
        }

        return method;
    }

    /// <summary>
    /// Looks a setting up by any of several spellings (the editors differ: "Normalised" and
    /// "Normalized", "RF Lens(%)" and "RF Lens (%)"), ignoring case, spaces and punctuation.
    /// </summary>
    public static string? Find(IReadOnlyDictionary<string, string>? settings, params string[] keys)
    {
        if (settings == null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            var wanted = Normalize(key);
            foreach (var kv in settings)
            {
                if (Normalize(kv.Key) == wanted)
                {
                    return kv.Value;
                }
            }
        }

        return null;
    }

    /// <summary>The number a value starts with ("1.300 [µl/min]" is 1.3, "60K" is 60); null when it starts with none.</summary>
    public static double? LeadingNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var m = LeadingNumberText().Match(value);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    // -- LC script (Thermo SII for Xcalibur) ------------------------------------------------------

    public static LcSection ParseLcMethod(string text, string? editorName)
    {
        var lc = new LcSection { MethodEditor = editorName };
        LcStep? step = null;
        var inScript = false;

        foreach (var rawLine in SplitLines(text))
        {
            var t = rawLine.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (t.StartsWith("----", StringComparison.Ordinal))
            {
                inScript = t.Contains("Script", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inScript)
            {
                var colon = t.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    var k = t[..colon].Trim();
                    var v = t[(colon + 1)..].Trim();
                    if (k.Equals("Run time", StringComparison.OrdinalIgnoreCase))
                    {
                        lc.RunTimeMinutes = LeadingNumber(v);
                    }
                    else if (k.Equals("Instrument", StringComparison.OrdinalIgnoreCase))
                    {
                        lc.InstrumentLine = v;
                    }
                }

                continue;
            }

            // Time lines start in column 0; properties and commands are indented.
            var indented = rawLine.Length > 0 && char.IsWhiteSpace(rawLine[0]);
            var tm = indented ? Match.Empty : LcTimeLine().Match(t);
            if (tm.Success)
            {
                var name = tm.Groups[2].Value.Trim();
                if (tm.Groups[1].Value == "initial" && name.Length == 0)
                {
                    name = "Instrument Setup";
                }

                step = new LcStep { Time = tm.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal), Name = name.Length > 0 ? name : null };
                lc.Steps.Add(step);
                continue;
            }

            if (step == null)
            {
                continue;
            }

            var pm = LcProperty().Match(rawLine);
            if (pm.Success)
            {
                step.Properties[UniqueKey(step.Properties, pm.Groups[1].Value.Trim())] = pm.Groups[2].Value.Trim();
            }
            else
            {
                step.Commands.Add(t);
            }
        }

        if (lc.Steps.FirstOrDefault(s => s.Time == "initial") is { } setup)
        {
            lc.SetupProperties = setup.Properties;
        }

        lc.Recognized = lc.Steps.Count > 0;
        lc.SystemSetup = BySuffix(lc.SetupProperties, ".SystemSetup");
        lc.InjectionWorkflow = BySuffix(lc.SetupProperties, ".InjectionWorkflow");
        lc.FlowRegime = BySuffix(lc.SetupProperties, ".FlowRegime");
        lc.SolventALabel = Unquote(BySuffix(lc.SetupProperties, ".%A_Solvent"));
        lc.SolventBLabel = Unquote(BySuffix(lc.SetupProperties, ".%B_Solvent"));
        lc.AutosamplerTemperatureC = LeadingNumber(BySuffix(lc.SetupProperties, "SamplerModule.Temperature.Nominal"));
        lc.ColumnTemperatureC = LeadingNumber(
            BySuffix(lc.SetupProperties, "ColumnChamber.Temperature.Nominal")
            ?? BySuffix(lc.SetupProperties, "ColumnCompartment.Temperature.Nominal")
            ?? BySuffix(lc.SetupProperties, "ColumnOven.Temperature.Nominal")
            ?? BySuffix(lc.SetupProperties, "ColumnComp.Temperature.Nominal"));
        lc.AnalyticalColumn = ReadColumn(lc.SetupProperties, ".SeparationColumn1.");
        lc.TrapColumn = ReadColumn(lc.SetupProperties, ".TrapColumn1.");
        BuildGradient(lc);
        return lc;
    }

    private static void BuildGradient(LcSection lc)
    {
        // The analytical program runs from the "Run" step (or the first timed step after the
        // injection) to "Stop Run"; equilibration before the injection is not part of it.
        var start = lc.Steps.FindIndex(s => string.Equals(s.Name, "Run", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            start = lc.Steps.FindIndex(s => s.Time != "initial" && HasGradientProperty(s));
        }

        if (start < 0)
        {
            return;
        }

        double? lastB = null;
        double? lastFlow = null;
        for (var i = start; i < lc.Steps.Count; i++)
        {
            var s = lc.Steps[i];
            if (string.Equals(s.Name, "Stop Run", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!HasGradientProperty(s) || LeadingNumber(s.Time) is not { } time)
            {
                continue;
            }

            var b = LeadingNumber(BySuffix(s.Properties, ".%B.Value")) ?? lastB;
            var flow = LeadingNumber(BySuffix(s.Properties, ".Flow.Nominal")) ?? lastFlow;
            lc.Gradient.Add(new GradientPoint { TimeMin = time, PercentB = b, FlowUlPerMin = flow, Curve = BySuffix(s.Properties, ".Curve") });
            lastB = b;
            lastFlow = flow;
        }

        GradientSegment? best = null;
        for (var i = 1; i < lc.Gradient.Count; i++)
        {
            var (a, z) = (lc.Gradient[i - 1], lc.Gradient[i]);
            if (a.PercentB is not { } from || z.PercentB is not { } to || to <= from)
            {
                continue;
            }

            var segment = new GradientSegment
            {
                StartMin = a.TimeMin,
                EndMin = z.TimeMin,
                StartPercentB = from,
                EndPercentB = to,
                FlowUlPerMin = z.FlowUlPerMin,
            };
            if (best == null || segment.DurationMin > best.DurationMin)
            {
                best = segment;
            }
        }

        lc.LongestGradientSegment = best;
    }

    private static bool HasGradientProperty(LcStep s) =>
        BySuffix(s.Properties, ".%B.Value") != null || BySuffix(s.Properties, ".Flow.Nominal") != null;

    private static ColumnInfo? ReadColumn(Dictionary<string, string> props, string infix)
    {
        var id = LeadingNumber(ByInfix(props, infix + "Diameter"));
        var length = LeadingNumber(ByInfix(props, infix + "Length"));
        return id == null && length == null
            ? null
            : new ColumnInfo
            {
                InnerDiameterUm = id,
                LengthCm = length,
                MaximumPressureBar = LeadingNumber(ByInfix(props, infix + "MaximumPressure")),
                MaximumTemperatureC = LeadingNumber(ByInfix(props, infix + "MaximumTemperature")),
            };
    }

    private static string? BySuffix(Dictionary<string, string> props, string suffix) =>
        props.FirstOrDefault(kv => kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? ByInfix(Dictionary<string, string> props, string infix) =>
        props.FirstOrDefault(kv => kv.Key.EndsWith(infix, StringComparison.OrdinalIgnoreCase)
                                   || kv.Key.Contains(infix + ".", StringComparison.OrdinalIgnoreCase)).Value;

    // -- helpers ----------------------------------------------------------------------------------

    private static MsExperiment AddExperiment(MsMethodSection method, int number)
    {
        var e = new MsExperiment { Number = number };
        method.Experiments.Add(e);
        return e;
    }

    private static MsMethodBlock? LastBlock(MsExperiment? experiment) =>
        experiment == null || experiment.Blocks.Count == 0 ? null : experiment.Blocks[^1];

    /// <summary>A key repeated in one block is kept as "key #2", not overwritten.</summary>
    private static string UniqueKey(Dictionary<string, string> dict, string key)
    {
        if (!dict.ContainsKey(key))
        {
            return key;
        }

        for (var i = 2; ; i++)
        {
            var k = $"{key} #{i}";
            if (!dict.ContainsKey(k))
            {
                return k;
            }
        }
    }

    private static string Normalize(string s) =>
        new string([.. s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)])
            .Replace("normalised", "normalized", StringComparison.Ordinal);

    private static string? Unquote(string? s) => s?.Trim().Trim('"');

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}

// The parsed method. Mutable classes, as in RawMethodCheck, since the parser fills them line by
// line; keeping their shape the same keeps the two copies easy to compare.

/// <summary>The LC method, from the SII script: system, columns, solvents, temperatures and gradient.</summary>
public sealed class LcSection
{
    public string? MethodEditor { get; set; }
    public string? SystemSetup { get; set; }
    public string? InstrumentLine { get; set; }
    public double? RunTimeMinutes { get; set; }
    public string? InjectionWorkflow { get; set; }
    public string? FlowRegime { get; set; }
    public ColumnInfo? AnalyticalColumn { get; set; }
    public ColumnInfo? TrapColumn { get; set; }
    public string? SolventALabel { get; set; }
    public string? SolventBLabel { get; set; }
    public double? AutosamplerTemperatureC { get; set; }
    public double? ColumnTemperatureC { get; set; }
    public List<GradientPoint> Gradient { get; set; } = [];
    public GradientSegment? LongestGradientSegment { get; set; }
    public List<LcStep> Steps { get; set; } = [];
    public Dictionary<string, string> SetupProperties { get; set; } = [];

    /// <summary>False when the text is not an SII script (an EASY-nLC method, for one); nothing else is then filled in.</summary>
    public bool Recognized { get; set; }
}

public sealed class ColumnInfo
{
    public double? InnerDiameterUm { get; set; }
    public double? LengthCm { get; set; }
    public double? MaximumPressureBar { get; set; }
    public double? MaximumTemperatureC { get; set; }
}

public sealed class GradientPoint
{
    public double TimeMin { get; set; }
    public double? PercentB { get; set; }
    public double? FlowUlPerMin { get; set; }
    public string? Curve { get; set; }
}

public sealed class GradientSegment
{
    public double StartMin { get; set; }
    public double EndMin { get; set; }
    public double StartPercentB { get; set; }
    public double EndPercentB { get; set; }
    public double? FlowUlPerMin { get; set; }
    public double DurationMin => Math.Round(EndMin - StartMin, 3);
}

public sealed class LcStep
{
    public string Time { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
    public List<string> Commands { get; set; } = [];
}

/// <summary>The MS method summary: global settings, then each experiment's scans and filters.</summary>
public sealed class MsMethodSection
{
    public string? Title { get; set; }
    public string? LastModified { get; set; }
    public Dictionary<string, string> GlobalSettings { get; set; } = [];
    public List<string> GlobalFlags { get; set; } = [];
    public List<MsExperiment> Experiments { get; set; } = [];
}

public sealed class MsExperiment
{
    public int Number { get; set; }
    public Dictionary<string, string> Settings { get; set; } = [];
    public List<MsMethodBlock> Blocks { get; set; } = [];
}

public sealed class MsMethodBlock
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? ScanEvent { get; set; }
    public Dictionary<string, string> Settings { get; set; } = [];
    public List<string> Flags { get; set; } = [];
    public MassListInfo? MassList { get; set; }
}

/// <summary>A scan's mass list table: DIA windows, or a PRM method's targets.</summary>
public sealed class MassListInfo
{
    public List<string> Columns { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
    public int RowCount => Rows.Count;
}
