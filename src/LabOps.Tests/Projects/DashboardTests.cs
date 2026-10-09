using System.Diagnostics;
using System.Globalization;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;

namespace LabOps.Tests.Projects;

public sealed class DashboardTests
{
    private static readonly DateOnly Today = DashboardSample.Today;

    private static string Page(DashboardView view, DashboardFilter? filter = null, ProjectList? list = null) =>
        DashboardHtml.Page(list ?? DashboardSample.List(), new DashboardRequest(view, Today, filter ?? DashboardFilter.Everything));

    private static DashboardItem Item(string name) => DashboardModel.Items(DashboardSample.List()).Single(i => i.Name == name);

    [Fact]
    public void Open_work_is_every_project_and_experiment_not_closed_each_project_first()
    {
        var list = DashboardSample.List();
        var closed = list.Labs[0].Projects[0] with { Project = "CWZ-Old", Status = "closed" };
        list = list with { Labs = [list.Labs[0] with { Projects = [.. list.Labs[0].Projects, closed] }, .. list.Labs.Skip(1)] };

        var names = DashboardModel.Items(list).Select(i => i.Name).ToList();

        names.ShouldNotContain("CWZ-Old");
        names.Take(2).ShouldBe(["CWZ-Otter-EV", "2026-10-Otter-DIA"]);
        Item("2026-10-Otter-DIA").LabContact.ShouldBe("jdoe", "an experiment without its own lab contact has its project's");
    }

    [Theory]
    [InlineData("RVB-Heron-Liver", 0)]
    [InlineData("2026-12-BioTRACK-PRM", 0)]
    [InlineData("2026-10-Otter-DIA", 2)]
    [InlineData("2026-09-BioTRACK-DIA", 3)]
    [InlineData("CWZ-Otter-EV", -1)]
    public void An_item_sits_in_the_column_of_its_current_step(string name, int phase) =>
        DashboardModel.Phase(Item(name)).ShouldBe(phase);

    [Fact]
    public void A_step_of_another_kind_goes_with_the_step_it_leads_to()
    {
        var item = Item("RVB-Heron-Liver") with
        {
            Steps = [new StageEntry { Stage = "second_shipment", Kind = "other", Label = "Second shipment" }, new StageEntry { Stage = "sample_prep", Kind = "sample_prep" }],
        };
        DashboardModel.Phase(item with { Current = item.Steps[0] }).ShouldBe(1);
    }

    [Fact]
    public void A_step_spans_what_happened_and_what_is_planned()
    {
        var step = new StageEntry { Stage = "x", Status = "in_progress", Started = "2026-10-01", PlannedStart = "2026-09-30", PlannedFinish = "2026-10-12" };

        DashboardModel.Spans(step, Today).ShouldBe(
        [
            new StepSpan(new DateOnly(2026, 10, 1), Today, false, step),
            new StepSpan(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 12), true, step),
        ]);
        var finishedOnly = new StageEntry { Stage = "y", Status = "done", Finished = "2026-10-02" };
        DashboardModel.Spans(finishedOnly, Today).Single().ShouldBe(new StepSpan(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), false, finishedOnly));
    }

    [Fact]
    public void A_step_started_and_finished_the_same_day_is_one_calendar_event()
    {
        var events = DashboardModel.Events([Item("RVB-Heron-Liver")], new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27));

        events.ShouldHaveSingleItem().What.ShouldBe("finished");
    }

    [Fact]
    public void Bookings_come_from_data_acquisition_and_overlaps_on_one_instrument_are_conflicts()
    {
        var items = DashboardModel.Items(DashboardSample.List());
        var bookings = DashboardModel.Bookings(items, Today);

        var otter = bookings.Single(b => b.Item.Name == "2026-10-Otter-DIA");
        (otter.From, otter.To, otter.Planned).ShouldBe((new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 11), false),
            "under way: from its start to its planned finish, which is still ahead");
        bookings.Single(b => b.Item.Name == "2026-10-Heron-DIA").Planned.ShouldBeTrue();
        var conflict = DashboardModel.Conflicts(bookings).ShouldHaveSingleItem();
        (conflict.First.Item.Name, conflict.Second.Item.Name).ShouldBe(("2026-10-Otter-DIA", "2026-10-Heron-DIA"));
        DashboardModel.InstrumentNames(["Stellar", "Orbitrap Astral"], bookings).ShouldBe(["Stellar", "Orbitrap Astral", "Q-Orbitrap"]);
    }

    [Fact]
    public void A_person_sees_what_is_theirs()
    {
        var mine = DashboardModel.Items(DashboardSample.List()).Where(i => i.Matches(new DashboardFilter("KCHEN"))).Select(i => i.Name);

        mine.ShouldBe(["2026-10-Otter-DIA", "LKS-Trout-CSF", "2026-10-Heron-DIA", "2026-12-BioTRACK-PRM", "2026-10-QC-Stellar"], ignoreOrder: true);
        Page(DashboardView.Board, new DashboardFilter("kchen")).ShouldContain("Kai Chen&#39;s work in every lab", Case.Sensitive);
    }

    [Theory]
    [InlineData(DashboardView.Attention, "3 days late")]
    [InlineData(DashboardView.Board, "Acquisition (4)")]
    [InlineData(DashboardView.Timeline, "Sep 14")]
    [InlineData(DashboardView.Calendar, "Calendar: October 2026")]
    [InlineData(DashboardView.Instruments, "Overlapping bookings (1)")]
    public void Each_view_draws_without_scripts_and_with_every_value_encoded(DashboardView view, string shows)
    {
        var html = Page(view);

        html.ShouldContain(shows, Case.Sensitive);
        html.ShouldNotContain("<script", Case.Insensitive);
        html.ShouldNotContain("<svg", Case.Insensitive);
        html.ShouldNotContain("javascript:", Case.Insensitive);
        html.ShouldNotContain("<liver>", Case.Sensitive);
        if (view is DashboardView.Board or DashboardView.Attention or DashboardView.Timeline)
        {
            html.ShouldContain("Heron &lt;liver&gt; &amp; kidney tissue, two batches", Case.Sensitive);
        }

        html.ShouldContain($"href=\"{DashboardHtml.OpenPrefix}2026-10-Otter-DIA\"", Case.Sensitive);
    }

    [Fact]
    public void A_late_step_is_red_on_the_board_and_the_timeline()
    {
        Page(DashboardView.Board).ShouldContain("card card-late", Case.Sensitive);
        Page(DashboardView.Timeline).ShouldContain("class=\"bar-late\"", Case.Sensitive);
    }

    [Fact]
    public void A_busy_day_shows_three_chips_and_counts_the_rest()
    {
        var steps = Enumerable.Range(1, 5).Select(i => new StageEntry { Stage = $"s{i}", Kind = "other", Label = $"Step {i}", PlannedFinish = "2026-10-20" }).ToArray();
        var list = new ProjectList([new LabSummary { Lab = "Busy-Lab", Projects = [new ProjectSummary
        {
            Project = "Busy-Project", Lab = "Busy-Lab", Status = "active", Stages = steps, CurrentStage = "s1",
        }] }], [], []);

        var html = Page(DashboardView.Calendar, list: list);

        html.Split("class=\"chip chip-planned\"").Length.ShouldBe(DashboardHtml.ChipsPerDay + 1);
        html.ShouldContain("+2 more", Case.Sensitive);
    }

    [Fact]
    public void The_panorama_summary_is_styled_inline_links_to_wiki_pages_and_hides_error_messages()
    {
        var html = DashboardHtml.PanoramaSummary(DashboardSample.List(), Today);

        html.ShouldNotContain("class=", Case.Sensitive);
        html.ShouldNotContain(DashboardHtml.OpenPrefix, Case.Sensitive);
        html.ShouldNotContain("<style", Case.Insensitive);
        html.ShouldContain("style=\"", Case.Sensitive);
        html.ShouldContain("href=\"/MacCoss/Collaborations/MNRF/BioTRACK/project-begin.view\"", Case.Sensitive);
        html.ShouldNotContain("looks like a name", Case.Sensitive);
        html.ShouldContain("1 record(s) have errors; open LabOps to see them.", Case.Sensitive);
    }

    [Fact]
    public void Every_class_the_views_use_has_a_style()
    {
        foreach (var view in Enum.GetValues<DashboardView>())
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Page(view), "class=\"([^\"]*)\""))
            {
                foreach (var cls in m.Groups[1].Value.Split(' '))
                {
                    Styles.Has(cls).ShouldBeTrue($"{view} uses class {cls}");
                }
            }
        }
    }

    /// <summary>Hundreds of projects a year for years: 3,000 closed and 150 open, each with two experiments.</summary>
    private static ProjectList Large(int open, int closed)
    {
        var labs = new List<LabSummary>();
        for (var l = 0; l < 30; l++)
        {
            var projects = new List<ProjectSummary>();
            for (var p = l; p < open + closed; p += 30)
            {
                var name = $"P{p:D4}";
                var start = -(p % 60);
                StageEntry S(string kind, int offset, int length, string? who = null) => new()
                {
                    Stage = kind, Kind = kind, Assigned = who ?? $"user{p % 12}",
                    Status = start + offset + length < 0 ? "done" : start + offset < 0 ? "in_progress" : "pending",
                    Started = start + offset < 0 ? D(start + offset) : null,
                    Finished = start + offset + length < 0 ? D(start + offset + length) : null,
                    PlannedStart = D(start + offset), PlannedFinish = D(start + offset + length - 2),
                };
                ExperimentSummary E(string suffix, string instrument) => new()
                {
                    Experiment = $"2026-10-{name}-{suffix}", Project = name, Lab = $"Lab-{l:D2}", Status = p < open ? "active" : "closed",
                    Instrument = instrument, Stages = [S("data_acquisition", 30, 5), S("signal_processing", 36, 7), S("data_analysis", 44, 20)],
                };
                var steps = new[] { S("samples_received", 0, 1), S("metadata_organized", 2, 5), S("plate_layout", 8, 2), S("sample_prep", 11, 14) };
                projects.Add(new ProjectSummary
                {
                    Project = name, Lab = $"Lab-{l:D2}", Status = p < open ? "active" : "closed", Stages = steps,
                    CurrentStage = steps.FirstOrDefault(s => !s.IsDone)?.Stage,
                    Experiments = [E("DIA", "Orbitrap Astral"), E("PRM", "Stellar")],
                });
            }

            labs.Add(new LabSummary { Lab = $"Lab-{l:D2}", Projects = projects });
        }

        return new ProjectList(labs, [], [], 0, ["Orbitrap Astral", "Stellar"]);

        static string D(int days) => Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Years_of_projects_draw_quickly_and_the_panorama_summary_stays_bounded()
    {
        var list = Large(open: 150, closed: 3000);
        Page(DashboardView.Board, list: list); // warm up

        var clock = Stopwatch.StartNew();
        foreach (var view in Enum.GetValues<DashboardView>())
        {
            Page(view, list: list);
        }

        var summary = DashboardHtml.PanoramaSummary(list, Today);
        clock.Stop();

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        summary.Length.ShouldBeLessThan(300_000);
        summary.ShouldContain("more; open LabOps to see them all.", Case.Sensitive);
    }
}
