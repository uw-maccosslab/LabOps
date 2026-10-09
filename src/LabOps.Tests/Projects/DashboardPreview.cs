using LabOps.Core.Projects.Dashboard;

namespace LabOps.Tests.Projects;

/// <summary>Writes every view of the made-up lab to DASHBOARD_PREVIEW_DIR, to look at while designing. Opt-in.</summary>
public sealed class DashboardPreview
{
    [Fact]
    public void Every_view_is_written_for_a_look()
    {
        var folder = Environment.GetEnvironmentVariable("DASHBOARD_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(folder))
        {
            Assert.Skip("Set DASHBOARD_PREVIEW_DIR to a folder to write the views there.");
        }

        Directory.CreateDirectory(folder);
        var list = DashboardSample.List();
        foreach (var view in Enum.GetValues<DashboardView>())
        {
            File.WriteAllText(Path.Combine(folder, $"{view}.html"),
                DashboardHtml.Page(list, new DashboardRequest(view, DashboardSample.Today, DashboardFilter.Everything)));
        }

        File.WriteAllText(Path.Combine(folder, "Board-kchen.html"),
            DashboardHtml.Page(list, new DashboardRequest(DashboardView.Board, DashboardSample.Today, new DashboardFilter("kchen"))));
        File.WriteAllText(Path.Combine(folder, "Panorama.html"),
            "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:16px\">"
            + DashboardHtml.PanoramaSummary(list, DashboardSample.Today) + "</body></html>");
    }
}
