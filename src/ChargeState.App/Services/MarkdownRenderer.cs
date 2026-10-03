using System.Net;
using Markdig;

namespace ChargeState.App.Services;

/// <summary>Turns quote.md and calculation.md into a page for the preview pane.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    // Light only, with an explicit white page: WebView2 follows the Windows dark theme, and a
    // page with no background of its own then renders this dark text on black.
    private const string Style = """
        :root { color-scheme: light; }
        html { background: #ffffff; }
        body { font-family: 'Segoe UI', sans-serif; font-size: 14px; line-height: 1.5; color: #1f2933;
               background: #ffffff; margin: 24px 32px; max-width: 900px; }
        h1 { font-size: 22px; color: #1f4e79; margin-top: 0; }
        h2 { font-size: 17px; color: #1f4e79; border-bottom: 1px solid #d9e2ec; padding-bottom: 4px; margin-top: 28px; }
        table { border-collapse: collapse; margin: 12px 0; width: 100%; }
        th, td { border: 1px solid #d9e2ec; padding: 6px 10px; vertical-align: top; text-align: left; }
        th { background: #f0f4f8; }
        td:nth-child(n+2) { font-variant-numeric: tabular-nums; }
        code { background: #f0f4f8; padding: 1px 4px; border-radius: 3px; }
        a { color: #1f6fb2; }
        .empty { color: #829ab1; font-style: italic; }
        """;

    public static string ToHtml(string? markdown, string emptyMessage)
    {
        var body = string.IsNullOrWhiteSpace(markdown)
            ? $"<p class=\"empty\">{WebUtility.HtmlEncode(emptyMessage)}</p>"
            : Markdown.ToHtml(markdown, Pipeline);

        return $"<!doctype html><html><head><meta charset=\"utf-8\"><style>{Style}</style></head><body>{body}</body></html>";
    }
}
