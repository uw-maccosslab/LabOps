using System.Text;

namespace LabOps.Core.Projects.Dashboard;

/// <summary>
/// The overview's look, one rule per class and no other selectors, so the same table makes the
/// app's stylesheet and Panorama's inline styles. Where an element has several classes, the later
/// ones override the earlier: inline because their declarations come later, and in the stylesheet
/// because their rules come later in this table, which a test holds every view to. Text colors
/// keep at least 4.5:1 contrast with their background.
/// </summary>
internal static class Styles
{
    private static readonly (string Class, string Css)[] Rules =
    [
        ("dash", "font-family:'Segoe UI',Arial,sans-serif;font-size:13px;color:#1f2933;line-height:1.35"),
        ("title", "font-size:17px;font-weight:600;margin:0 0 2px 0"),
        ("sub", "color:#5b6670;margin:0 0 10px 0"),
        ("section", "font-size:13px;font-weight:600;color:#364152;margin:14px 0 4px 0"),
        ("empty", "color:#5b6670;margin:2px 0 6px 0"),
        ("legend", "color:#5b6670;font-size:12px;margin:8px 0 0 0"),
        ("list", "border-collapse:collapse"),
        ("head", "text-align:left;color:#5b6670;font-weight:600;font-size:12px;padding:3px 22px 3px 0;border-bottom:1px solid #d9e0e6"),
        ("cell", "text-align:left;vertical-align:top;padding:3px 22px 3px 0;border-bottom:1px solid #eef1f4"),
        ("num", "text-align:right"),
        ("lab", "color:#5b6670"),
        ("item", "color:#0b5cad;text-decoration:none;font-weight:600"),
        ("item-plain", "font-weight:600"),
        ("badge", "display:inline-block;padding:0 6px;border-radius:9px;font-size:11px;background:#eef1f4;color:#364152"),
        ("badge-late", "background:#fde8e8;color:#b42318"),
        ("badge-progress", "background:#e6f0fb;color:#0b5cad"),
        ("board", "display:flex;gap:10px;align-items:flex-start"),
        ("col", "flex:1 1 0;min-width:150px;background:#f4f6f8;border-radius:6px;padding:6px"),
        ("col-head", "font-weight:600;font-size:12px;color:#364152;margin:2px 2px 6px 2px"),
        ("card", "background:#ffffff;border:1px solid #e3e8ee;border-radius:5px;padding:5px 7px;margin-bottom:6px"),
        ("card-late", "border-left:3px solid #b42318"),
        ("card-line", "color:#5b6670;font-size:12px"),
        // After every class it is combined with, so its red wins in the stylesheet as it does inline.
        ("late", "color:#b42318;font-weight:600"),
        ("tl", "width:100%"),
        ("tl-lab", "font-weight:600;color:#364152;margin:10px 0 2px 0"),
        ("tl-row", "display:flex;align-items:center;border-bottom:1px solid #eef1f4;min-height:22px"),
        ("tl-weeks", "border-bottom:1px solid #d9e0e6;color:#5b6670;font-size:11px"),
        ("tl-name", "width:200px;flex:none;padding-right:8px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis"),
        ("tl-sub", "padding-left:14px;width:186px"),
        ("tl-track", "position:relative;flex:1 1 auto;height:18px"),
        ("week", "position:absolute;top:2px;border-left:1px solid #d9e0e6;padding-left:3px;height:14px"),
        ("bar", "position:absolute;top:4px;height:10px;border-radius:3px;background:#5b8fd0"),
        ("bar-done", "position:absolute;top:4px;height:10px;border-radius:3px;background:#a9b4bf"),
        ("bar-late", "position:absolute;top:4px;height:10px;border-radius:3px;background:#d64545"),
        ("bar-planned", "position:absolute;top:3px;height:10px;border-radius:3px;border:1px dashed #5b6670"),
        ("bar-planned-late", "position:absolute;top:3px;height:10px;border-radius:3px;border:1px dashed #d64545"),
        ("bar-clash", "position:absolute;top:3px;height:12px;border-radius:3px;background:#d64545;opacity:0.85"),
        ("today", "position:absolute;top:0;bottom:0;border-left:2px solid #f59e0b"),
        ("cal", "border-collapse:collapse;width:100%;table-layout:fixed"),
        ("cal-head", "font-size:12px;color:#5b6670;font-weight:600;padding:4px;text-align:left"),
        ("cal-day", "border:1px solid #e3e8ee;vertical-align:top;height:88px;padding:3px;overflow:hidden"),
        ("cal-out", "background:#f9fafb;color:#5b6670"),
        ("cal-today", "border:2px solid #f59e0b"),
        ("day", "font-size:11px;color:#5b6670"),
        ("chip", "display:block;font-size:11px;padding:1px 4px;margin-top:2px;border-radius:3px;background:#e6f0fb;color:#1f2933;"
                 + "text-decoration:none;white-space:nowrap;overflow:hidden;text-overflow:ellipsis"),
        ("chip-planned", "background:#ffffff;border:1px dashed #7b8794"),
        ("chip-done", "background:#e7f6ec"),
        ("chip-late", "background:#fde8e8;color:#b42318;border:1px dashed #d64545"),
        ("more", "font-size:11px;color:#5b6670;margin-top:2px"),
    ];

    private static readonly Dictionary<string, string> ByClass = Rules.ToDictionary(r => r.Class, r => r.Css, StringComparer.Ordinal);

    /// <summary>The app's stylesheet: the page's own margins, then one rule per class.</summary>
    public static string Sheet { get; } = BuildSheet();

    private static string BuildSheet()
    {
        var sheet = new StringBuilder("body{margin:0;background:#ffffff}.dash{padding:12px 16px}");
        foreach (var (cls, css) in Rules)
        {
            sheet.Append('.').Append(cls).Append('{').Append(css).Append('}');
        }

        // The app's page only (Panorama's has no bars): a bar reached with the keyboard is outlined
        // and shows its label under it, which a mouse gets from the tooltip.
        sheet.Append("a[aria-label]:focus{outline:2px solid #f59e0b;outline-offset:1px;z-index:3}")
            .Append("a[aria-label]:focus::after{content:attr(aria-label);position:absolute;left:0;top:14px;white-space:nowrap;")
            .Append("background:#1f2933;color:#ffffff;font-size:11px;padding:2px 6px;border-radius:3px;z-index:3}");
        return sheet.ToString();
    }

    /// <summary>The declarations of space-separated classes, in order, each ending with a semicolon.</summary>
    public static string Inline(string? classes)
    {
        if (string.IsNullOrWhiteSpace(classes))
        {
            return "";
        }

        var css = new StringBuilder();
        foreach (var cls in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            css.Append(ByClass.TryGetValue(cls, out var rule) ? rule + ";" : throw new ArgumentException($"no style for class {cls}"));
        }

        return css.ToString();
    }

    public static bool Has(string cls) => ByClass.ContainsKey(cls);

    /// <summary>Where a class's rule is in the stylesheet, which decides what wins between equal selectors.</summary>
    public static int Order(string cls) => Array.FindIndex(Rules, r => r.Class == cls);
}
