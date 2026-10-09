using LabOps.App.ViewModels;
using LabOps.App.Views;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;
using LabOps.Tests.Projects;

namespace LabOps.Tests.App;

public sealed class OverviewTests
{
    private static OverviewViewModel Overview() => new(() => DashboardSample.Today);

    [Fact]
    public void The_overview_starts_with_the_signed_in_persons_own_work_when_they_have_some()
    {
        var mine = Overview();
        mine.Update(DashboardSample.List(), "kchen");
        mine.Person!.Value.ShouldBe("kchen");
        mine.Html.ShouldContain("Kai Chen&#39;s work", Case.Sensitive);

        var nobodys = Overview();
        nobodys.Update(DashboardSample.List(), "someone-else");
        nobodys.Person!.Value.ShouldBeNull();
        nobodys.People[0].Label.ShouldBe("Everyone");
        nobodys.Labs.Select(l => l.Label).ShouldBe(["Every lab", "ClearwaterZoo-Cole", "Lakeside-Park", "Riverbend-Ortiz", "UW-MacCoss"]);
    }

    [Fact]
    public void What_was_chosen_is_kept_when_the_projects_are_loaded_again()
    {
        var overview = Overview();
        overview.Update(DashboardSample.List(), "kchen");
        overview.Person = overview.People[0];
        overview.Lab = overview.Labs.Single(l => l.Value == "UW-MacCoss");
        overview.View = DashboardView.Board;

        overview.Update(DashboardSample.List(), "kchen");

        (overview.Person.Value, overview.Lab!.Value, overview.View).ShouldBe((null, "UW-MacCoss", DashboardView.Board));
        overview.Html.ShouldContain("Everyone&#39;s work with UW-MacCoss", Case.Sensitive);
        overview.Html.ShouldNotContain("RVB-Heron-Liver", Case.Sensitive);
    }

    [Fact]
    public void Refilling_the_lists_is_not_taken_as_a_choice_and_draws_the_page_once()
    {
        var overview = Overview();
        // What a ComboBox bound to Person and Lab does when its items are cleared.
        overview.People.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                overview.Person = null;
            }
        };
        overview.Labs.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                overview.Lab = null;
            }
        };
        var drawn = 0;
        overview.PropertyChanged += (_, e) => drawn += e.PropertyName == nameof(OverviewViewModel.Html) ? 1 : 0;

        overview.Update(DashboardSample.List(), null);   // not signed in yet: everyone's work
        overview.Update(DashboardSample.List(), "kchen"); // signed in since

        overview.Person!.Value.ShouldBe("kchen", "nobody chose Everyone; the lists were only refilled");
        drawn.ShouldBe(2);
    }

    [Fact]
    public void The_calendar_moves_by_month()
    {
        var overview = Overview();
        overview.Update(DashboardSample.List(), null);
        overview.View = DashboardView.Calendar;

        overview.NextMonthCommand.Execute(null);
        overview.MonthText.ShouldBe("November 2026");
        overview.Html.ShouldContain("Calendar: November 2026", Case.Sensitive);
        overview.PreviousMonthCommand.Execute(null);
        overview.PreviousMonthCommand.Execute(null);
        overview.ThisMonthCommand.Execute(null);
        overview.Month.ShouldBe(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void A_click_on_a_project_in_the_page_asks_to_show_it_and_nothing_else_is_followed()
    {
        var overview = Overview();
        string? asked = null;
        overview.OpenRequested += name => asked = name;

        overview.Follow(DashboardHtml.OpenPrefix + Uri.EscapeDataString("2026-10-Otter DIA")).ShouldBeTrue();
        asked.ShouldBe("2026-10-Otter DIA");
        overview.Follow("https://example.org/somewhere").ShouldBeFalse();
    }

    [Theory]
    [InlineData("pending", "2026-10-12", "2026-10-20", "Planned Oct 12 to Oct 20", false)]
    [InlineData("pending", "2026-10-05", null, "Planned to start Oct 5: late", true)]
    [InlineData("in_progress", null, "2026-10-08", "Due Oct 8: late", true)]
    [InlineData("done", "2026-10-01", "2026-10-05", "Planned Oct 1 to Oct 5", false)]
    [InlineData("pending", null, null, null, false)]
    public void A_step_row_says_its_plan_and_whether_it_is_late(string status, string? start, string? finish, string? text, bool late)
    {
        var section = new TimelineSection(new ProjectSummary { Project = "Proj" }, "projects/Lab/Proj", "Samples", "", [], []);
        var row = new StageRowViewModel(section, new StageEntry { Stage = "x", Status = status, PlannedStart = start, PlannedFinish = finish }, false)
        {
            Today = () => DashboardSample.Today,
        };

        (row.PlannedText, row.IsLate).ShouldBe((text, late));
        row.CanPlan.ShouldBe(status is "pending" or "in_progress");
    }

    [Fact]
    public void A_plan_cannot_finish_before_it_starts()
    {
        PlanWindow.WhyNot(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 11)).ShouldBe("The finish is before the start.");
        PlanWindow.WhyNot(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)).ShouldBeNull();
        PlanWindow.WhyNot(null, new DateOnly(2026, 10, 1)).ShouldBeNull();
    }
}
