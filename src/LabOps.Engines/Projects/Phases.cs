using System.Globalization;

namespace LabOps.Engines.Projects;

/// <summary>
/// The phases of the work, in order, each gathering steps of some kinds and each with one color,
/// used wherever a step is shown: the app's step rows and project list, the overview, the lab
/// dashboard and the projects' wiki pages. The colors stay apart for every adjacent pair, with
/// normal and with color-blind vision, and none is the dark red that means late. Two are lighter
/// than 3:1 on white, so a phase's color is always shown beside its name or the step's, never alone.
/// </summary>
public static class Phases
{
    /// <summary>A phase: its id, how it reads, its color, and the kinds of step it gathers.</summary>
    public sealed record Phase(string Id, string Label, string Color, IReadOnlyList<string> Kinds);

    public static readonly IReadOnlyList<Phase> All =
    [
        new("samples", "Samples", "#1baf7a", ["samples_received", "metadata_organized", "plate_layout"]),
        new("prep", "Preparation", "#2a78d6", ["sample_prep", "assay_development"]),
        new("acquisition", "Acquisition", "#4a3aa7", ["data_acquisition", "data_deposited"]),
        new("analysis", "Analysis", "#e87ba4", ["signal_processing", "data_analysis"]),
        new("results", "Results", "#008300", ["results_returned"]),
    ];

    /// <summary>The color of a step in no phase (kind other).</summary>
    public const string OtherColor = "#7b8794";

    /// <summary>The red of late work, darker than any phase's color so the two never mix up.</summary>
    public const string LateColor = "#b42318";

    /// <summary>The phase of a step of this kind, or null (kind other, or unknown).</summary>
    public static Phase? Of(string? kind) => kind is null ? null : All.FirstOrDefault(p => p.Kinds.Contains(kind));

    /// <summary>Where the phase of a step of this kind is in <see cref="All"/>, or -1.</summary>
    public static int IndexOf(string? kind)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (kind is not null && All[i].Kinds.Contains(kind))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The color of a step of this kind.</summary>
    public static string ColorOf(string? kind) => Of(kind)?.Color ?? OtherColor;

    /// <summary>
    /// A color faded toward white, for work that is finished: the phase still shows, quieter.
    /// <paramref name="strength"/> is how much of the color is kept, from 0 (white) to 1.
    /// </summary>
    public static string Faded(string color, double strength = 0.45)
    {
        var rgb = int.Parse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int Mix(int channel) => (int)Math.Round(255 - (255 - channel) * strength);
        return string.Create(CultureInfo.InvariantCulture, $"#{Mix(rgb >> 16 & 0xff):x2}{Mix(rgb >> 8 & 0xff):x2}{Mix(rgb & 0xff):x2}");
    }
}
