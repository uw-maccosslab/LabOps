using System.Text.RegularExpressions;
using System.Windows.Media;
using LabOps.App.ViewModels;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;
using LabOps.Engines.Projects;

namespace LabOps.Tests.Projects;

/// <summary>
/// Each phase of the work has one color, the same wherever a step is shown: the app's step rows
/// and project list, the overview's views and the lab dashboard (the wiki pages are in
/// WikiCommandTests).
/// </summary>
public sealed partial class PhaseColorTests
{
    private static readonly string[] Colors = [.. Phases.All.Select(p => p.Color)];

    private static string Page(DashboardView view) =>
        DashboardHtml.Page(DashboardSample.List(), new DashboardRequest(view, DashboardSample.Today, DashboardFilter.Everything));

    /// <summary>The opening tag of the bar labeled exactly so.</summary>
    private static string Bar(string html, string label)
    {
        var bar = Regex.Match(html, $"<a class=\"bar[^\"]*\" style=\"[^\"]*\" href=\"[^\"]*\" title=\"{Regex.Escape(label)}\"[^>]*>").Value;
        bar.ShouldNotBeEmpty($"no bar labeled {label}");
        return bar;
    }

    [GeneratedRegex("<a class=\"bar[^\"]*\"[^>]*>")]
    private static partial Regex AnyBar();

    [Fact]
    public void Every_kind_of_step_but_other_is_in_exactly_one_phase()
    {
        foreach (var kind in Steps.Kinds.Select(k => k.Key))
        {
            Phases.All.Count(p => p.Kinds.Contains(kind)).ShouldBe(kind == "other" ? 0 : 1, kind);
        }

        Phases.All.SelectMany(p => p.Kinds).ShouldBeSubsetOf(Steps.Kinds.Select(k => k.Key));
        Phases.ColorOf("other").ShouldBe(Phases.OtherColor);
        Phases.ColorOf(null).ShouldBe(Phases.OtherColor);
    }

    /// <summary>
    /// The colors were checked as a set, in this order: every adjacent pair stays apart with normal
    /// and with color-blind vision, and none is the red of late work. A change is a new check.
    /// </summary>
    [Fact]
    public void The_phases_keep_the_colors_they_were_checked_with()
    {
        Phases.All.Select(p => (p.Label, p.Color)).ShouldBe(
            [("Samples", "#1baf7a"), ("Preparation", "#2a78d6"), ("Acquisition", "#4a3aa7"), ("Analysis", "#e87ba4"), ("Results", "#008300")]);
        Colors.ShouldNotContain(Phases.LateColor);
        Colors.ShouldNotContain(Phases.OtherColor);
    }

    [Fact]
    public void Finished_work_is_its_phase_color_faded_toward_white()
    {
        Phases.Faded("#2a78d6").ShouldBe("#9fc2ed");
        Phases.Faded("#2a78d6", 1).ShouldBe("#2a78d6");
        Phases.Faded("#2a78d6", 0).ShouldBe("#ffffff");
    }

    [Fact]
    public void The_timeline_draws_each_step_in_its_phase_color_and_says_the_phase()
    {
        var html = Page(DashboardView.Timeline);

        Bar(html, "2026-10-Otter-DIA, Data acquisition (Acquisition): Oct 3 to Oct 9").ShouldContain("background:#4a3aa7", Case.Sensitive);
        Bar(html, "CWZ-Otter-EV, Sample prep (Preparation): Sep 12 to Sep 19")
            .ShouldContain($"background:{Phases.Faded("#2a78d6")}", Case.Sensitive, "finished: faded");
        Bar(html, "2026-10-Otter-DIA, Data deposited to Panorama (Acquisition): Oct 12 to Oct 13 (planned)")
            .ShouldContain("border-color:#4a3aa7", Case.Sensitive, "planned: dashed in the phase's color");
        var late = Bar(html, "2026-09-BioTRACK-DIA, Signal processing (Analysis): Sep 29 to Oct 9, late");
        late.ShouldStartWith("<a class=\"bar bar-late\"", Case.Sensitive);
        late.ShouldContain("background:#e87ba4", Case.Sensitive, "late: ringed in red over its phase's color");
        Styles.Inline("bar-late").ShouldContain(Phases.LateColor, Case.Sensitive);
        html.ShouldContain("<p class=\"key\">", Case.Sensitive);
    }

    [Fact]
    public void An_instrument_schedule_is_in_the_acquisition_color_except_where_bookings_overlap()
    {
        var bars = AnyBar().Matches(Page(DashboardView.Instruments)).Select(m => m.Value).ToList();

        bars.ShouldNotBeEmpty();
        bars.ShouldContain(b => b.StartsWith("<a class=\"bar bar-clash\"", StringComparison.Ordinal));
        foreach (var bar in bars.Where(b => !b.Contains("bar-clash", StringComparison.Ordinal)))
        {
            new[] { "background:#4a3aa7", $"background:{Phases.Faded("#4a3aa7")}", "border-color:#4a3aa7" }
                .ShouldContain(look => bar.Contains(look, StringComparison.Ordinal), bar);
        }
    }

    [Fact]
    public void The_board_heads_each_phase_with_its_color()
    {
        Regex.Matches(Page(DashboardView.Board), "class=\"col-head\" style=\"padding-bottom:3px;border-bottom:3px solid (#[0-9a-f]{6})\"")
            .Select(m => m.Groups[1].Value).ShouldBe(Colors);
    }

    [Fact]
    public void Every_calendar_chip_starts_with_its_phase()
    {
        var html = Page(DashboardView.Calendar);
        var chips = Regex.Matches(html, "<a class=\"chip").Count;

        chips.ShouldBeGreaterThan(0);
        Regex.Matches(html, "<a class=\"chip[^\"]*\"[^>]*><span class=\"swatch\" style=\"background:#[0-9a-f]{6}\" aria-hidden=\"true\"></span>")
            .Count.ShouldBe(chips);
    }

    [Fact]
    public void The_lab_dashboard_shows_the_phases_in_their_colors_inline()
    {
        var html = DashboardHtml.PanoramaSummary(DashboardSample.List(), DashboardSample.Today);

        html.ShouldNotContain("class=", Case.Sensitive);
        foreach (var color in Colors)
        {
            html.ShouldContain($"vertical-align:-1px;background:{color}\" aria-hidden=\"true\"></span>", Case.Sensitive);
        }

        // The late step's square, before its name.
        html.ShouldContain($"background:{Phases.ColorOf("signal_processing")}\" aria-hidden=\"true\"></span>Signal processing", Case.Sensitive);
    }

    [Fact]
    public void A_step_row_in_the_app_has_its_phase_color_and_name()
    {
        var section = new TimelineSection(new ProjectSummary { Project = "Proj" }, "projects/Lab/Proj", "Samples", "", [], []);
        StageRowViewModel Row(string kind) => new(section, new StageEntry { Stage = kind, Kind = kind }, false);

        var prep = Row("sample_prep");
        ((SolidColorBrush)prep.PhaseBrush).Color.ShouldBe((Color)ColorConverter.ConvertFromString("#2a78d6"));
        prep.PhaseBrush.IsFrozen.ShouldBeTrue();
        prep.PhaseName.ShouldBe("Preparation");
        ((SolidColorBrush)Row("other").PhaseBrush).Color.ShouldBe((Color)ColorConverter.ConvertFromString(Phases.OtherColor));
        Row("other").PhaseName.ShouldBe("Other");
    }

    [Fact]
    public void The_project_list_shows_the_phase_of_where_a_project_stands()
    {
        var lab = new LabSummary { Lab = "Zoo-Lab" };
        var open = new ProjectSummary
        {
            Project = "Proj", Stages = [new StageEntry { Stage = "metadata_organized", Kind = "metadata_organized" }], CurrentStage = "metadata_organized",
        };

        var row = new ProjectRow(lab, open, null);
        ((SolidColorBrush)row.ProgressBrush).Color.ShouldBe((Color)ColorConverter.ConvertFromString("#1baf7a"));
        row.ProgressPhase.ShouldBe("Samples");
        var done = new ProjectRow(lab, open with { CurrentStage = null }, null);
        done.ProgressBrush.ShouldBe(Brushes.Transparent);
        done.ProgressPhase.ShouldBeNull();
    }
}
