using System.Globalization;
using System.Text.RegularExpressions;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;

namespace LabOps.Tests.Projects;

/// <summary>The overview's finer rules: what wins between styles, paused work, whose steps, links, limits.</summary>
public sealed partial class DashboardReviewTests
{
    private static readonly DateOnly Today = DashboardSample.Today;

    private static string Page(DashboardView view, DashboardFilter? filter = null, ProjectList? list = null) =>
        DashboardHtml.Page(list ?? DashboardSample.List(), new DashboardRequest(view, Today, filter ?? DashboardFilter.Everything));

    private static string D(int days) => Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex("class=\"([^\"]*)\"")]
    private static partial Regex ClassAttribute();

    [Fact]
    public void Where_classes_combine_the_later_one_wins_in_the_stylesheet_as_it_does_inline()
    {
        foreach (var view in Enum.GetValues<DashboardView>())
        {
            foreach (Match m in ClassAttribute().Matches(Page(view)))
            {
                var classes = m.Groups[1].Value.Split(' ');
                for (var i = 1; i < classes.Length; i++)
                {
                    Styles.Order(classes[i]).ShouldBeGreaterThan(Styles.Order(classes[i - 1]),
                        $"{view}: \"{m.Groups[1].Value}\" needs {classes[i]}'s rule after {classes[i - 1]}'s");
                }
            }
        }
    }

    private static ProjectList WithStatus(string project, string status)
    {
        var list = DashboardSample.List();
        return list with
        {
            Labs = [.. list.Labs.Select(l => l with { Projects = [.. l.Projects.Select(p => p.Project == project ? p with { Status = status } : p)] })],
        };
    }

    [Fact]
    public void Paused_work_is_not_late()
    {
        var list = WithStatus("RVB-Heron-Liver", "on_hold");

        var attention = Page(DashboardView.Attention, list: list);
        attention.ShouldNotContain("RVB-Heron-Liver", Case.Sensitive);
        attention.ShouldNotContain("2026-10-Heron-DIA", Case.Sensitive, "an experiment of a paused project is paused too");
        var board = Page(DashboardView.Board, list: list);
        board.ShouldContain("on hold", Case.Sensitive);
        Regex.Matches(board, "card card-late").Count.ShouldBe(1, "only the other late item is red");
    }

    [Fact]
    public void A_persons_attention_lists_their_own_steps_and_the_unassigned_ones_of_their_projects()
    {
        var kchen = Page(DashboardView.Attention, new DashboardFilter("kchen"));

        kchen.ShouldContain("Data acquisition", Case.Sensitive);
        kchen.ShouldNotContain("Signal processing", Case.Sensitive, "the otter's signal processing is assigned to maccoss");
        // LKS-Trout-CSF is kchen's as lab contact, and its first step is theirs.
        kchen.ShouldContain("LKS-Trout-CSF", Case.Sensitive);
    }

    [Fact]
    public void Errors_in_a_labs_own_record_are_counted()
    {
        var list = DashboardSample.List() with { Problems = [] };
        list = list with { Labs = [list.Labs[0] with { Issues = [new ProjectIssue("ERROR", "lab: pi is missing")] }, .. list.Labs.Skip(1)] };

        var html = Page(DashboardView.Attention, list: list);

        html.ShouldContain("Records with errors (1)", Case.Sensitive);
        html.ShouldContain("lab: pi is missing", Case.Sensitive);
        DashboardHtml.PanoramaSummary(list, Today).ShouldContain("1 record(s) have errors", Case.Sensitive);
    }

    [Theory]
    [InlineData("/MacCoss/Collaborations/MNRF/BioTRACK", null, "/MacCoss/Collaborations/MNRF/BioTRACK/wiki-page.view?name=default")]
    [InlineData("//evil.example/phish", null, "/evil.example/phish/wiki-page.view?name=default")]
    [InlineData("/Lab/Collab #1?", "Page one", "/Lab/Collab%20%231%3F/wiki-page.view?name=Page%20one")]
    [InlineData("/", null, null)]
    public void A_wiki_link_stays_on_panorama_with_each_part_encoded(string folder, string? page, string? path) =>
        DashboardHtml.WikiPath(new WikiLocation(folder, page)).ShouldBe(path);

    [Fact]
    public void An_experiment_is_indented_only_under_its_own_project()
    {
        var html = Page(DashboardView.Timeline);

        // MAC-QC-Standards has nothing in these weeks, so its experiments stand on their own.
        html.ShouldContain("<div class=\"tl-name\"><a class=\"item\" href=\"https://labops.invalid/open/2026-10-QC-Stellar\"", Case.Sensitive);
        html.ShouldContain("<div class=\"tl-name tl-sub\"><a class=\"item\" href=\"https://labops.invalid/open/2026-10-Otter-DIA\"", Case.Sensitive);
    }

    private static ProjectList Bookings(params (string Name, StageEntry Acquisition)[] experiments) => new(
        [new LabSummary { Lab = "Busy-Lab", Projects = [new ProjectSummary
        {
            Project = "Busy-Project", Lab = "Busy-Lab", Status = "active", Stages = [new StageEntry { Stage = "samples_received", Status = "done" }],
            Experiments = [.. experiments.Select(e => new ExperimentSummary
            {
                Experiment = e.Name, Project = "Busy-Project", Lab = "Busy-Lab", Status = "active", Instrument = "Stellar",
                Stages = [e.Acquisition], CurrentStage = e.Acquisition.IsDone ? null : e.Acquisition.Stage,
            })],
        }] }], [], [], 0, ["Stellar"]);

    [Fact]
    public void The_instrument_schedule_shows_the_timelines_weeks_and_counts_what_it_shows()
    {
        var list = Bookings(
            ("2026-09-Early", new StageEntry { Stage = "data_acquisition", Kind = "data_acquisition", Status = "done", Started = D(-24), Finished = D(-19) }),
            ("2027-03-Later", new StageEntry { Stage = "data_acquisition", Kind = "data_acquisition", PlannedStart = D(150), PlannedFinish = D(152) }),
            ("2026-10-Finish-Only", new StageEntry { Stage = "data_acquisition", Kind = "data_acquisition", Status = "done", Finished = D(-3), PlannedStart = D(20) }));

        var html = Page(DashboardView.Instruments, list: list);

        html.ShouldContain("2026-09-Early", Case.Sensitive, "it is on the timeline's weeks, so it is on the schedule too");
        html.ShouldNotContain("2027-03-Later", Case.Sensitive);
        html.ShouldContain(">Stellar (2)<", Case.Sensitive);
        html.ShouldContain($"2026-10-Finish-Only (Busy-Lab): {Today.AddDays(-3).ToString("MMM d", CultureInfo.GetCultureInfo("en-US"))}, done", Case.Sensitive);
    }

    [Fact]
    public void Many_overlapping_bookings_are_listed_up_to_a_limit()
    {
        var list = Bookings([.. Enumerable.Range(1, 15).Select(i => ($"2026-10-Clash-{i:D2}",
            new StageEntry { Stage = "data_acquisition", Kind = "data_acquisition", PlannedStart = D(2), PlannedFinish = D(4) }))]);

        var html = Page(DashboardView.Instruments, list: list);

        html.ShouldContain("Overlapping bookings (105)", Case.Sensitive);
        html.ShouldContain($"and {105 - DashboardHtml.ConflictRows} more", Case.Sensitive);
    }

    [Fact]
    public void Late_is_said_in_words_as_well_as_in_red()
    {
        Page(DashboardView.Timeline).ShouldContain(", late\"", Case.Sensitive);
        var calendar = DashboardHtml.Page(DashboardSample.List(), new DashboardRequest(DashboardView.Calendar, Today, DashboardFilter.Everything));
        calendar.ShouldContain("Metadata organized due (late)", Case.Sensitive);
    }

    [Fact]
    public void A_bar_is_a_labeled_link_the_keyboard_reaches_and_shows_its_label_when_focused()
    {
        var timeline = Page(DashboardView.Timeline);
        var instruments = Page(DashboardView.Instruments);

        // A link (the keyboard reaches it, and it shows the item), with its description as its name.
        timeline.ShouldContain("href=\"https://labops.invalid/open/2026-09-BioTRACK-DIA\" "
                               + "title=\"2026-09-BioTRACK-DIA, Signal processing: Sep 29 to Oct 9, late\" "
                               + "aria-label=\"2026-09-BioTRACK-DIA, Signal processing: Sep 29 to Oct 9, late\"", Case.Sensitive);
        instruments.ShouldContain("aria-label=\"2026-10-Otter-DIA (ClearwaterZoo-Cole): Oct 3 to Oct 11, under way, overlaps another booking\"",
            Case.Sensitive);
        foreach (var page in (string[])[timeline, instruments])
        {
            page.ShouldNotContain("<div class=\"bar", Case.Sensitive);
            page.ShouldContain("class=\"today\"", Case.Sensitive);
            Regex.Matches(page, "<div class=\"today\"[^>]*aria-hidden=\"true\"").Count.ShouldBe(Regex.Matches(page, "class=\"today\"").Count);
            page.ShouldContain("a[aria-label]:focus::after{content:attr(aria-label)", Case.Sensitive);
        }
    }

    [Fact]
    public void Soon_is_seven_days_counting_today()
    {
        var steps = new[]
        {
            new StageEntry { Stage = "a", Kind = "other", Label = "Six days out", PlannedFinish = D(6) },
            new StageEntry { Stage = "b", Kind = "other", Label = "Seven days out", PlannedFinish = D(7) },
        };
        var list = new ProjectList([new LabSummary { Lab = "Soon-Lab", Projects = [new ProjectSummary
        {
            Project = "Soon-Project", Lab = "Soon-Lab", Status = "active", Stages = steps, CurrentStage = "a",
        }] }], [], []);

        var html = Page(DashboardView.Attention, list: list);

        html.ShouldContain("Six days out", Case.Sensitive);
        html.ShouldNotContain("Seven days out", Case.Sensitive);
    }

    [Fact]
    public void Pages_have_a_language_and_a_title_and_people_by_name()
    {
        var html = Page(DashboardView.Board);

        html.ShouldStartWith("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Board</title>", Case.Sensitive);
        html.ShouldContain("Kai Chen, due Oct 11", Case.Sensitive);
        Page(DashboardView.Attention).ShouldContain("<h2 class=\"section\">Late (2)</h2>", Case.Sensitive);
    }
}
