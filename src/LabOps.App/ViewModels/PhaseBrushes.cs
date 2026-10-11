using System.Collections.Concurrent;
using System.Windows.Media;
using LabOps.Engines.Projects;

namespace LabOps.App.ViewModels;

/// <summary>
/// The phases' colors (<see cref="Phases"/>) as brushes, one frozen brush per color, so a step
/// in the app is the color the overview, the lab dashboard and the wiki pages give it.
/// </summary>
public static class PhaseBrushes
{
    private static readonly ConcurrentDictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The brush for a step of this kind.</summary>
    public static Brush OfKind(string? kind) => Of(Phases.ColorOf(kind));

    /// <summary>The brush for a color written #rrggbb.</summary>
    public static Brush Of(string color) => Cache.GetOrAdd(color, c =>
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        brush.Freeze();
        return brush;
    });
}
