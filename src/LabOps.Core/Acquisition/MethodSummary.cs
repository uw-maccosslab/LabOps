using System.Globalization;
using System.Text;

namespace LabOps.Core.Acquisition;

/// <summary>
/// An instrument method's settings as Markdown, for Claude to write an LC-MS protocol from: the
/// LC (system, columns, temperatures, solvents, gradient), the mass spectrometer's global
/// settings, each experiment's scans and filters with their mass lists (DIA windows, PRM targets),
/// and each device's method text as the instrument wrote it. Settings only: the lines naming who
/// saved the method ("Creator: ... Last Modified: ...") are left out, and no sample is named.
/// </summary>
public static class MethodSummary
{
    public static string ToMarkdown(IReadOnlyList<RawMethodText> texts, string? methodFile, string? instrument)
    {
        var method = ThermoMethod.Read(texts);
        var md = new StringBuilder();
        md.Append("# Instrument method").Append(methodFile is null ? "" : $": {methodFile}").Append("\n\n");
        md.Append("Read by LabOps from the method embedded in the raw files")
            .Append(instrument is null ? "" : $" ({instrument})")
            .Append(". These are the method's settings as the instrument recorded them; nothing here names a sample. ")
            .Append($"Method fingerprint: {ThermoRawHeader.Fingerprint(texts)}.\n\n");

        if (method.Lc is { } lc)
        {
            WriteLc(md, lc);
        }

        if (method.Ms is { } ms)
        {
            WriteMs(md, ms);
        }

        md.Append("## Method text, as the instrument wrote it\n\n");
        foreach (var text in texts)
        {
            md.Append("### ").Append(text.Device).Append("\n\n```text\n");
            foreach (var line in text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (!Identifying(line))
                {
                    md.Append(line.TrimEnd()).Append('\n');
                }
            }

            md.Append("```\n\n");
        }

        return md.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// A line naming who saved the method ("Creator: ... Last Modified: ... by PC\user") or the
    /// instrument computer the LC was driven from ("Instrument: Vanquish_Neo on PC-NAME"): not a
    /// setting, and not something a protocol should carry.
    /// </summary>
    private static bool Identifying(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("Creator:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Instrument:", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteLc(StringBuilder md, LcSection lc)
    {
        md.Append("## LC\n\n");
        if (!lc.Recognized)
        {
            md.Append($"The LC method ({lc.MethodEditor}) is not in a format LabOps reads; its text is at the end.\n\n");
            return;
        }

        Table(md, "Setting", "Value",
        [
            ("LC system", lc.SystemSetup),
            ("Injection workflow", lc.InjectionWorkflow),
            ("Flow regime", lc.FlowRegime),
            ("Analytical column", Column(lc.AnalyticalColumn)),
            ("Trap column", Column(lc.TrapColumn)),
            ("Column temperature", Number(lc.ColumnTemperatureC, " °C")),
            ("Autosampler temperature", Number(lc.AutosamplerTemperatureC, " °C")),
            ("Solvent A (label in the method)", lc.SolventALabel),
            ("Solvent B (label in the method)", lc.SolventBLabel),
            ("Run time", Number(lc.RunTimeMinutes, " min")),
        ]);

        if (lc.Gradient.Count > 0)
        {
            md.Append("### Gradient\n\n| Time (min) | %B | Flow (µL/min) |\n|---|---|---|\n");
            foreach (var p in lc.Gradient)
            {
                md.Append($"| {Number(p.TimeMin)} | {Number(p.PercentB)} | {Number(p.FlowUlPerMin)} |\n");
            }

            md.Append('\n');
            if (lc.LongestGradientSegment is { } s)
            {
                md.Append($"The longest gradient segment runs from {Number(s.StartPercentB)} to {Number(s.EndPercentB)} %B over "
                          + $"{Number(s.DurationMin)} min ({Number(s.StartMin)} to {Number(s.EndMin)} min).\n\n");
            }
        }
    }

    private static void WriteMs(StringBuilder md, MsMethodSection ms)
    {
        md.Append("## Mass spectrometer").Append(ms.Title is null ? "" : $": {ms.Title}").Append("\n\n");
        if (ms.GlobalSettings.Count > 0 || ms.GlobalFlags.Count > 0)
        {
            md.Append("### Global settings\n\n");
            Settings(md, ms.GlobalSettings, ms.GlobalFlags);
        }

        foreach (var experiment in ms.Experiments)
        {
            var name = MethodTextParser.Find(experiment.Settings, "Experiment Name");
            md.Append($"### Experiment {experiment.Number}").Append(name is null ? "" : $": {name}").Append("\n\n");
            if (experiment.Settings.Count > 0)
            {
                Settings(md, experiment.Settings, []);
            }

            foreach (var block in experiment.Blocks)
            {
                md.Append($"#### {$"{block.Kind} {block.Name}".Trim()}")
                    .Append(block.ScanEvent is { } e ? $" (scan event {e})" : "").Append("\n\n");
                Settings(md, block.Settings, block.Flags);
                if (block.MassList is { RowCount: > 0 } list)
                {
                    md.Append($"Mass list ({list.RowCount} row{(list.RowCount == 1 ? "" : "s")}):\n\n");
                    md.Append("| ").Append(string.Join(" | ", list.Columns.Select(Cell))).Append(" |\n");
                    md.Append('|').Append(string.Concat(Enumerable.Repeat("---|", Math.Max(1, list.Columns.Count)))).Append('\n');
                    foreach (var row in list.Rows)
                    {
                        md.Append("| ").Append(string.Join(" | ", row.Select(Cell))).Append(" |\n");
                    }

                    md.Append('\n');
                }
            }
        }
    }

    private static void Settings(StringBuilder md, IReadOnlyDictionary<string, string> settings, IReadOnlyList<string> flags)
    {
        if (settings.Count > 0)
        {
            Table(md, "Setting", "Value", [.. settings.Select(kv => (kv.Key, (string?)kv.Value))]);
        }

        if (flags.Count > 0)
        {
            md.Append("Also set: ").Append(string.Join("; ", flags)).Append(".\n\n");
        }
    }

    private static void Table(StringBuilder md, string left, string right, IReadOnlyList<(string Name, string? Value)> rows)
    {
        var shown = rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToList();
        if (shown.Count == 0)
        {
            return;
        }

        md.Append($"| {left} | {right} |\n|---|---|\n");
        foreach (var (name, value) in shown)
        {
            md.Append($"| {Cell(name)} | {Cell(value!)} |\n");
        }

        md.Append('\n');
    }

    private static string? Column(ColumnInfo? c)
    {
        if (c is null)
        {
            return null;
        }

        var size = string.Join(" × ", new[] { Number(c.InnerDiameterUm, " µm inner diameter"), Number(c.LengthCm, " cm") }.OfType<string>());
        var limits = string.Join(", ", new[] { Number(c.MaximumPressureBar, " bar"), Number(c.MaximumTemperatureC, " °C") }.OfType<string>());
        return limits.Length == 0 ? size : $"{size} (at most {limits})";
    }

    private static string? Number(double? value, string unit = "") =>
        value is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) + unit : null;

    /// <summary>A table cell: a pipe would end it, and a line break would end the row.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}
