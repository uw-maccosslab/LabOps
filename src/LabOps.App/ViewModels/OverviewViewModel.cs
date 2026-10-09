using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;

namespace LabOps.App.ViewModels;

/// <summary>A choice in one of the overview's lists; a null value means everyone, or every lab.</summary>
public sealed record OverviewChoice(string? Value, string Label)
{
    // What screen readers and UI Automation read for the list item.
    public override string ToString() => Label;
}

/// <summary>One of the overview's views, as its tab reads.</summary>
public sealed record OverviewTab(DashboardView View, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Projects area's overview: which view, whose work, which lab and (for the calendar) which
/// month, and the page that shows it. The page is drawn from the list the area last loaded, so
/// changing a choice draws it again without reading anything.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private ProjectList? _list;
    private bool _personChosen;

    public OverviewViewModel(Func<DateOnly>? today = null)
    {
        Today = today ?? LabOps.Engines.Projects.ProjectRepository.LabToday;
        Month = FirstOfMonth(Today());
        View = DashboardView.Attention;
    }

    /// <summary>Someone clicked a project or experiment in the page: show it in the list.</summary>
    public event Action<string>? OpenRequested;

    private Func<DateOnly> Today { get; }

    public IReadOnlyList<OverviewTab> Tabs { get; } =
        [.. Enum.GetValues<DashboardView>().Select(v => new OverviewTab(v, DashboardHtml.ViewTitle(v)))];

    public ObservableCollection<OverviewChoice> People { get; } = [];

    public ObservableCollection<OverviewChoice> Labs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendar))]
    public partial DashboardView View { get; set; }

    [ObservableProperty] public partial OverviewChoice? Person { get; set; }

    [ObservableProperty] public partial OverviewChoice? Lab { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthText))]
    public partial DateOnly Month { get; set; }

    /// <summary>The page for the choices, or empty before the projects are loaded.</summary>
    [ObservableProperty] public partial string Html { get; set; } = "";

    public bool IsCalendar => View == DashboardView.Calendar;

    public string MonthText => Month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>
    /// Takes the list the area loaded. The first time, it shows the signed-in person's own work
    /// when they have any, and everyone's otherwise; after that, what was chosen is kept.
    /// </summary>
    public void Update(ProjectList list, string? me)
    {
        _list = list;
        var person = Person?.Value;
        var lab = Lab?.Value;
        // Refilling the lists makes the bound boxes set Person and Lab to null on their own; those
        // are not choices, and the page is drawn once, at the end.
        _updating = true;
        People.Clear();
        People.Add(new OverviewChoice(null, "Everyone"));
        foreach (var p in list.People.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            People.Add(new OverviewChoice(p.Login, p.DisplayName));
        }

        Labs.Clear();
        Labs.Add(new OverviewChoice(null, "Every lab"));
        foreach (var l in list.Labs.Where(l => l.Projects.Any(p => !p.IsClosed)).OrderBy(l => l.Lab, StringComparer.OrdinalIgnoreCase))
        {
            Labs.Add(new OverviewChoice(l.Lab, l.Lab));
        }

        if (!_personChosen && me is not null && DashboardModel.Items(list).Any(i => i.Matches(new DashboardFilter(me))))
        {
            person = People.FirstOrDefault(c => string.Equals(c.Value, me, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        Person = People.FirstOrDefault(c => string.Equals(c.Value, person, StringComparison.OrdinalIgnoreCase)) ?? People[0];
        Lab = Labs.FirstOrDefault(c => string.Equals(c.Value, lab, StringComparison.OrdinalIgnoreCase)) ?? Labs[0];
        _updating = false;
        Render();
    }

    // Set while Update refills the lists and restores the choices, which are not the person's.
    private bool _updating;

    partial void OnViewChanged(DashboardView value) => Render();

    partial void OnPersonChanged(OverviewChoice? value)
    {
        if (!_updating)
        {
            _personChosen = true;
            Render();
        }
    }

    partial void OnLabChanged(OverviewChoice? value)
    {
        if (!_updating)
        {
            Render();
        }
    }

    partial void OnMonthChanged(DateOnly value) => Render();

    [RelayCommand]
    private void PreviousMonth() => Month = Month.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => Month = Month.AddMonths(1);

    [RelayCommand]
    private void ThisMonth() => Month = FirstOfMonth(Today());

    /// <summary>A link the page followed: a project or experiment to show, or nothing the overview knows.</summary>
    /// <returns>Whether the address was one of the page's own links.</returns>
    public bool Follow(string uri)
    {
        if (!uri.StartsWith(DashboardHtml.OpenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var name = Uri.UnescapeDataString(uri[DashboardHtml.OpenPrefix.Length..]);
        if (name.Length > 0)
        {
            OpenRequested?.Invoke(name);
        }

        return true;
    }

    private void Render()
    {
        if (_list is null)
        {
            return;
        }

        Html = DashboardHtml.Page(_list, new DashboardRequest(View, Today(), new DashboardFilter(Person?.Value, Lab?.Value), Month));
    }

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);
}
