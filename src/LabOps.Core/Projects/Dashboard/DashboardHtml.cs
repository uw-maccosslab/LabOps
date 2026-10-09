using System.Globalization;
using System.Net;
using System.Text;

namespace LabOps.Core.Projects.Dashboard;

/// <summary>One view to draw: which, as of when, for whom, and (for the calendar) which month.</summary>
public sealed record DashboardRequest(DashboardView View, DateOnly Today, DashboardFilter Filter, DateOnly? Month = null);

/// <summary>
/// The overview as HTML. The app shows a whole page (a stylesheet and class-based markup) in a
/// WebView2 with scripts off; Panorama gets a bounded summary with every style inline, since a wiki
/// page keeps only what is on its elements. Both come from the same markup, written once: a class
/// is either named or replaced by its declarations. There is no script and no SVG, and every value
/// from a record is encoded.
/// </summary>
public static class DashboardHtml
{
    /// <summary>The links the app intercepts: #open:NAME selects that project or experiment in the list.</summary>
    public const string OpenPrefix = "#open:";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>How long a view looks ahead for steps due or starting soon.</summary>
    public const int SoonDays = 7;

    /// <summary>The most rows a section of the Panorama summary lists before saying how many more there are.</summary>
    public const int PanoramaRows = 25;

    public static string ViewTitle(DashboardView view) => view switch
    {
        DashboardView.Attention => "Needs attention",
        DashboardView.Board => "Board",
        DashboardView.Timeline => "Timeline",
        DashboardView.Calendar => "Calendar",
        _ => "Instruments",
    };

    /// <summary>The app's page for one view.</summary>
    public static string Page(ProjectList list, DashboardRequest request)
    {
        var h = new Html(inline: false);
        h.Raw("<!doctype html><html><head><meta charset=\"utf-8\"><style>").Raw(Styles.Sheet).Raw("</style></head><body>");
        h.Open("div", "dash");
        var items = DashboardModel.Items(list).Where(i => i.Matches(request.Filter)).ToList();
        h.Element("h1", "title", request.View == DashboardView.Calendar
            ? $"{ViewTitle(request.View)}: {MonthOf(request).ToString("MMMM yyyy", English)}"
            : ViewTitle(request.View));
        h.Element("p", "sub", Subtitle(list, request));
        switch (request.View)
        {
            case DashboardView.Attention:
                Attention(h, list, items, request, links: Links.App, rows: int.MaxValue);
                break;
            case DashboardView.Board:
                Board(h, items, request);
                break;
            case DashboardView.Timeline:
                Timeline(h, items, request.Today);
                break;
            case DashboardView.Calendar:
                Calendar(h, items, request);
                break;
            default:
                Instruments(h, list, items, request.Today, Links.App, rows: int.MaxValue);
                break;
        }

        h.Close("div").Raw("</body></html>");
        return h.ToString();
    }

    /// <summary>
    /// The lab dashboard for Panorama: what needs attention, how much work is at each phase in
    /// each lab, and the instruments' next weeks. Every list is capped, so the page stays a
    /// readable size however many projects there are.
    /// </summary>
    public static string PanoramaSummary(ProjectList list, DateOnly today)
    {
        var h = new Html(inline: true);
        var items = DashboardModel.Items(list);
        var request = new DashboardRequest(DashboardView.Attention, today, DashboardFilter.Everything);
        h.Open("div", "dash");
        h.Element("p", "sub", $"Open lab projects and experiments as of {today.ToString("dddd, MMMM d, yyyy", English)}.");
        h.Element("h2", "section", "Needs attention");
        Attention(h, list, items, request, Links.Panorama, PanoramaRows);
        h.Element("h2", "section", "Work by phase");
        PhaseCounts(h, items);
        h.Element("h2", "section", "Instruments");
        Instruments(h, list, items, today, Links.Panorama, PanoramaRows, weeksAhead: 4);
        h.Close("div");
        return h.ToString();
    }

    private enum Links
    {
        App,
        Panorama,
    }

    private static string Subtitle(ProjectList list, DashboardRequest request)
    {
        var whose = request.Filter.Person is { } login
            ? $"{list.People.FirstOrDefault(p => string.Equals(p.Login, login, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? login}'s work"
            : "Everyone's work";
        var where = request.Filter.Lab is { } lab ? $" with {lab}" : " in every lab";
        return $"{whose}{where}, as of {request.Today.ToString("dddd, MMMM d, yyyy", English)}.";
    }

    // ---------------------------------------------------------------- needs attention

    private static void Attention(Html h, ProjectList list, List<DashboardItem> items, DashboardRequest request, Links links, int rows)
    {
        var today = request.Today;
        var soon = today.AddDays(SoonDays);
        var late = new List<(DashboardItem Item, StageEntry Step, DateOnly Since, string What)>();
        var due = new List<(DashboardItem Item, StageEntry Step, DateOnly Day, string What)>();
        foreach (var item in items)
        {
            foreach (var step in item.Steps.Where(s => !(s.IsDone || s.IsSkipped)))
            {
                var plannedStart = StageEntry.Date(step.PlannedStart);
                var plannedFinish = StageEntry.Date(step.PlannedFinish);
                if (step.IsLate(today))
                {
                    var finishPassed = plannedFinish is { } f && f < today;
                    late.Add((item, step, finishPassed ? plannedFinish!.Value : plannedStart!.Value,
                        finishPassed ? "was due" : "was to start"));
                }
                else if (plannedFinish is { } f && f >= today && f <= soon)
                {
                    due.Add((item, step, f, "due"));
                }
                else if (step.IsPending && plannedStart is { } s && s >= today && s <= soon)
                {
                    due.Add((item, step, s, "starts"));
                }
            }
        }

        h.Element("h3", "section", Counted("Late", late.Count));
        if (late.Count == 0)
        {
            h.Element("p", "empty", "Nothing is late.");
        }
        else
        {
            StepTable(h, late.OrderBy(l => l.Since).ThenBy(l => l.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(l => (l.Item, l.Step, $"{l.What} {Short(l.Since)}", Days(today.DayNumber - l.Since.DayNumber), true)).ToList(), links, rows);
        }

        h.Element("h3", "section", Counted($"Due or starting in the next {SoonDays} days", due.Count));
        if (due.Count == 0)
        {
            h.Element("p", "empty", "Nothing is planned to finish or start this week.");
        }
        else
        {
            StepTable(h, due.OrderBy(d => d.Day).ThenBy(d => d.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => (d.Item, d.Step, $"{d.What} {Short(d.Day)}",
                    d.Day == today ? "today" : d.Day.DayNumber - today.DayNumber == 1 ? "tomorrow" : $"in {d.Day.DayNumber - today.DayNumber} days", false))
                .ToList(), links, rows);
        }

        var broken = items.Where(i => i.Issues.Any(x => x.IsError)).ToList();
        var whole = request.Filter == DashboardFilter.Everything ? list.Problems.Where(p => p.IsError).ToList() : [];
        h.Element("h3", "section", Counted("Records with errors", broken.Count + whole.Count));
        if (broken.Count + whole.Count == 0)
        {
            h.Element("p", "empty", "Every record checks clean.");
            return;
        }

        if (links == Links.Panorama)
        {
            // The messages are for whoever fixes the records, in LabOps; the page only says how many.
            h.Element("p", "empty", $"{broken.Count + whole.Count} record(s) have errors; open LabOps to see them.");
            return;
        }

        h.Open("table", "list");
        foreach (var item in broken)
        {
            h.Open("tr").Open("td", "cell");
            ItemLink(h, item, links);
            h.Close("td").Element("td", "cell lab", item.Lab).Element("td", "cell late", item.Issues.First(x => x.IsError).Message).Close("tr");
        }

        foreach (var problem in whole)
        {
            h.Open("tr").Element("td", "cell", "Repository").Element("td", "cell lab", "").Element("td", "cell late", problem.Message).Close("tr");
        }

        h.Close("table");
    }

    private static void StepTable(Html h, List<(DashboardItem Item, StageEntry Step, string When, string HowLong, bool Late)> rows, Links links, int max)
    {
        h.Open("table", "list");
        h.Open("tr").Element("th", "head", "Project or experiment").Element("th", "head", "Lab").Element("th", "head", "Step")
            .Element("th", "head", "Assigned").Element("th", "head", "When").Element("th", "head", "").Close("tr");
        foreach (var (item, step, when, howLong, late) in rows.Take(max))
        {
            h.Open("tr").Open("td", "cell");
            ItemLink(h, item, links);
            h.Close("td")
                .Element("td", "cell lab", item.Lab)
                .Element("td", "cell", step.DisplayLabel)
                .Element("td", "cell", step.Assigned ?? "nobody")
                .Element("td", late ? "cell late" : "cell", when)
                .Element("td", late ? "cell late" : "cell lab", howLong)
                .Close("tr");
        }

        h.Close("table");
        More(h, rows.Count - max);
    }

    // ---------------------------------------------------------------- board

    private static void Board(Html h, List<DashboardItem> items, DashboardRequest request)
    {
        var detailed = request.Filter != DashboardFilter.Everything;
        var placed = items.Select(i => (Item: i, Phase: DashboardModel.Phase(i))).Where(x => x.Phase >= 0).ToList();
        if (placed.Count == 0)
        {
            h.Element("p", "empty", "No open project or experiment has a step to do.");
            return;
        }

        h.Open("div", "board");
        for (var phase = 0; phase < DashboardModel.Phases.Count; phase++)
        {
            var here = placed.Where(x => x.Phase == phase).Select(x => x.Item)
                .OrderByDescending(i => i.Current!.IsLate(request.Today))
                .ThenBy(i => i.Lab, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            h.Open("div", "col");
            h.Element("div", "col-head", Counted(DashboardModel.Phases[phase].Label, here.Count));
            foreach (var item in here)
            {
                var step = item.Current!;
                var late = step.IsLate(request.Today);
                h.Open("div", late ? "card card-late" : "card");
                ItemLink(h, item, Links.App);
                h.Element("div", "card-line", item.IsExperiment ? $"{item.Lab} / {item.Project}" : item.Lab);
                if (detailed && !string.IsNullOrWhiteSpace(item.Title))
                {
                    h.Element("div", "card-line", item.Title!);
                }

                h.Open("div", "card-line").Text(step.DisplayLabel + " ");
                Badge(h, step, request.Today);
                h.Close("div");
                var who = step.Assigned is { } a ? a : "nobody assigned";
                var plan = StageEntry.Date(step.PlannedFinish) is { } f ? $", due {Short(f)}" : "";
                h.Element("div", late ? "card-line late" : "card-line", who + plan);
                h.Close("div");
            }

            h.Close("div");
        }

        h.Close("div");
    }

    private static void Badge(Html h, StageEntry step, DateOnly today)
    {
        if (step.IsLate(today))
        {
            h.Element("span", "badge badge-late", "late");
        }
        else if (step.IsInProgress)
        {
            h.Element("span", "badge badge-progress", "under way");
        }
        else
        {
            h.Element("span", "badge", "not started");
        }
    }

    private static void PhaseCounts(Html h, List<DashboardItem> items)
    {
        var placed = items.Select(i => (Item: i, Phase: DashboardModel.Phase(i))).Where(x => x.Phase >= 0).ToList();
        if (placed.Count == 0)
        {
            h.Element("p", "empty", "No open work.");
            return;
        }

        h.Open("table", "list").Open("tr").Element("th", "head", "Lab");
        foreach (var phase in DashboardModel.Phases)
        {
            h.Element("th", "head num", phase.Label);
        }

        h.Close("tr");
        foreach (var lab in placed.GroupBy(x => x.Item.Lab, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            h.Open("tr").Element("td", "cell", lab.Key);
            for (var phase = 0; phase < DashboardModel.Phases.Count; phase++)
            {
                var n = lab.Count(x => x.Phase == phase);
                h.Element("td", "cell num", n == 0 ? "" : n.ToString(CultureInfo.InvariantCulture));
            }

            h.Close("tr");
        }

        h.Close("table");
    }

    // ---------------------------------------------------------------- timeline

    /// <summary>The timeline's window: from the Monday three weeks back, twelve weeks.</summary>
    public static (DateOnly First, DateOnly Last) TimelineWindow(DateOnly today)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var first = monday.AddDays(-21);
        return (first, first.AddDays(12 * 7 - 1));
    }

    private static void Timeline(Html h, List<DashboardItem> items, DateOnly today)
    {
        var (first, last) = TimelineWindow(today);
        var rows = items
            .Select(i => (Item: i, Spans: i.Steps.SelectMany(s => DashboardModel.Spans(s, today)).Where(s => s.To >= first && s.From <= last).ToList()))
            .Where(x => x.Spans.Count > 0).ToList();
        if (rows.Count == 0)
        {
            h.Element("p", "empty", "Nothing was recorded or planned in these weeks.");
            return;
        }

        h.Open("div", "tl");
        Weeks(h, first, last);
        foreach (var lab in rows.GroupBy(r => r.Item.Lab, StringComparer.OrdinalIgnoreCase))
        {
            h.Element("div", "tl-lab", lab.Key);
            foreach (var (item, spans) in lab)
            {
                h.Open("div", "tl-row").Open("div", item.IsExperiment ? "tl-name tl-sub" : "tl-name");
                ItemLink(h, item, Links.App);
                h.Close("div").Open("div", "tl-track");
                foreach (var span in spans)
                {
                    var late = span.Step.IsLate(today);
                    var cls = span.Planned ? (late ? "bar-planned-late" : "bar-planned")
                        : span.Step.IsDone || span.Step.IsSkipped ? "bar-done" : late ? "bar-late" : "bar";
                    var tip = $"{span.Step.DisplayLabel}: {Range(span.From, span.To)}{(span.Planned ? " (planned)" : "")}";
                    h.Open("div", cls, Position(span.From, span.To, first, last), ("title", tip)).Close("div");
                }

                TodayLine(h, today, first, last);
                h.Close("div").Close("div");
            }
        }

        h.Close("div");
        h.Element("p", "legend", "Solid: what happened (gray once done). Dashed: what is planned. Red: late. The line is today.");
    }

    private static void Weeks(Html h, DateOnly first, DateOnly last)
    {
        h.Open("div", "tl-row tl-weeks").Element("div", "tl-name", "").Open("div", "tl-track");
        for (var week = first; week <= last; week = week.AddDays(7))
        {
            h.Element("div", "week", Short(week), Position(week, week, first, last, widthDays: 7));
        }

        h.Close("div").Close("div");
    }

    private static void TodayLine(Html h, DateOnly today, DateOnly first, DateOnly last)
    {
        if (today >= first && today <= last)
        {
            h.Open("div", "today", $"left:{Percent(today.DayNumber - first.DayNumber + 0.5, last.DayNumber - first.DayNumber + 1)}").Close("div");
        }
    }

    private static string Position(DateOnly from, DateOnly to, DateOnly first, DateOnly last, int? widthDays = null)
    {
        var days = last.DayNumber - first.DayNumber + 1;
        var start = Math.Max(from.DayNumber, first.DayNumber) - first.DayNumber;
        var end = Math.Min(to.DayNumber, last.DayNumber) - first.DayNumber + 1;
        var width = widthDays ?? Math.Max(end - start, 1);
        return $"left:{Percent(start, days)};width:{Percent(width, days)}";
    }

    private static string Percent(double part, int whole) => (100.0 * part / whole).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    // ---------------------------------------------------------------- calendar

    private static DateOnly MonthOf(DashboardRequest request) =>
        request.Month is { } m ? new DateOnly(m.Year, m.Month, 1) : new DateOnly(request.Today.Year, request.Today.Month, 1);

    /// <summary>The most chips a day shows; the rest are counted (and listed in the tooltip).</summary>
    public const int ChipsPerDay = 3;

    private static void Calendar(Html h, List<DashboardItem> items, DashboardRequest request)
    {
        var month = MonthOf(request);
        // Weeks start on Sunday, as the lab's calendars do.
        var first = month.AddDays(-(int)month.DayOfWeek);
        var lastOfMonth = month.AddMonths(1).AddDays(-1);
        var last = lastOfMonth.AddDays(6 - (int)lastOfMonth.DayOfWeek);
        var events = DashboardModel.Events(items, first, last).ToLookup(e => e.Day);
        h.Open("table", "cal").Open("tr");
        foreach (var day in (string[])["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"])
        {
            h.Element("th", "cal-head", day);
        }

        h.Close("tr");
        for (var week = first; week <= last; week = week.AddDays(7))
        {
            h.Open("tr");
            for (var day = week; day < week.AddDays(7); day = day.AddDays(1))
            {
                var cls = day == request.Today ? "cal-day cal-today" : day.Month != month.Month ? "cal-day cal-out" : "cal-day";
                h.Open("td", cls).Element("div", "day", day.Day.ToString(CultureInfo.InvariantCulture));
                var today = events[day].ToList();
                foreach (var e in today.Take(ChipsPerDay))
                {
                    var late = e.Planned && e.Step.IsLate(request.Today);
                    var chip = late ? "chip chip-late" : e.Planned ? "chip chip-planned" : e.What is "finished" or "skipped" ? "chip chip-done" : "chip";
                    var text = $"{e.Item.Name}: {e.Step.DisplayLabel} {e.What}";
                    h.Open("a", chip, null, ("href", OpenPrefix + Uri.EscapeDataString(e.Item.Name)), ("title", text)).Text(text).Close("a");
                }

                if (today.Count > ChipsPerDay)
                {
                    h.Element("div", "more", $"+{today.Count - ChipsPerDay} more", null,
                        ("title", string.Join("\n", today.Skip(ChipsPerDay).Select(e => $"{e.Item.Name}: {e.Step.DisplayLabel} {e.What}"))));
                }

                h.Close("td");
            }

            h.Close("tr");
        }

        h.Close("table");
        h.Element("p", "legend", "Blue: started. Green: finished. Dashed: planned to start, or due. Red: late.");
    }

    // ---------------------------------------------------------------- instruments

    private static void Instruments(Html h, ProjectList list, List<DashboardItem> items, DateOnly today, Links links, int rows, int? weeksAhead = null)
    {
        var horizon = weeksAhead is { } w ? today.AddDays(7 * w) : DateOnly.MaxValue;
        var bookings = DashboardModel.Bookings(items, today)
            .Where(b => b.To >= today.AddDays(-14) && b.From <= horizon)
            .OrderBy(b => b.From).ThenBy(b => b.Item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var conflicts = DashboardModel.Conflicts(bookings);
        if (conflicts.Count > 0)
        {
            h.Element("h3", "section late", Counted("Overlapping bookings", conflicts.Count));
            h.Open("table", "list");
            foreach (var (a, b) in conflicts.Take(rows))
            {
                h.Open("tr").Element("td", "cell", a.Instrument).Open("td", "cell");
                ItemLink(h, a.Item, links);
                h.Text($" ({Range(a.From, a.To)})").Close("td").Open("td", "cell");
                ItemLink(h, b.Item, links);
                h.Text($" ({Range(b.From, b.To)})").Close("td").Close("tr");
            }

            h.Close("table");
            More(h, conflicts.Count - rows);
        }

        var names = DashboardModel.InstrumentNames(list.Instruments, bookings);
        if (names.Count == 0)
        {
            h.Element("p", "empty", "No experiment names an instrument, and config/instruments.yaml lists none.");
            return;
        }

        // In the app, a schedule: one line per booking on the timeline's weeks, clashes in red.
        var (first, last) = TimelineWindow(today);
        var clashing = conflicts.SelectMany(c => (Booking[])[c.First, c.Second]).ToHashSet();
        if (links == Links.App)
        {
            h.Open("div", "tl");
            Weeks(h, first, last);
        }

        foreach (var name in names)
        {
            var mine = bookings.Where(b => string.Equals(b.Instrument, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (links == Links.App)
            {
                h.Element("div", "tl-lab", Counted(name, mine.Count));
                var shown = mine.Where(b => b.To >= first && b.From <= last).ToList();
                if (shown.Count == 0)
                {
                    h.Element("p", "empty", "Nothing booked in these weeks.");
                }

                foreach (var b in shown)
                {
                    h.Open("div", "tl-row").Open("div", "tl-name");
                    ItemLink(h, b.Item, links);
                    h.Element("span", "lab", " " + b.Item.Lab).Close("div").Open("div", "tl-track");
                    var cls = clashing.Contains(b) ? "bar-clash" : b.Planned ? "bar-planned" : b.Step.IsDone ? "bar-done" : "bar";
                    var tip = $"{b.Item.Name} ({b.Item.Lab}): {Range(b.From, b.To)}, "
                              + (b.Planned ? "planned" : b.Step.IsDone ? "done" : "under way") + (clashing.Contains(b) ? ", overlaps another booking" : "");
                    h.Open("div", cls, Position(b.From, b.To, first, last), ("title", tip)).Close("div");
                    TodayLine(h, today, first, last);
                    h.Close("div").Close("div");
                }

                continue;
            }

            h.Element("h3", "section", Counted(name, mine.Count));
            if (mine.Count == 0)
            {
                h.Element("p", "empty", "Nothing booked.");
                continue;
            }

            h.Open("table", "list");
            foreach (var b in mine.Take(rows))
            {
                h.Open("tr").Open("td", "cell");
                ItemLink(h, b.Item, links);
                h.Close("td").Element("td", "cell lab", b.Item.Lab).Element("td", "cell", Range(b.From, b.To))
                    .Element("td", b.Planned ? "cell lab" : "cell", b.Planned ? "planned" : b.Step.IsDone ? "done" : "under way")
                    .Close("tr");
            }

            h.Close("table");
            More(h, mine.Count - rows);
        }

        if (links == Links.App)
        {
            h.Close("div");
            h.Element("p", "legend", "Each line is an experiment's data acquisition. Solid: recorded (gray once done). Dashed: planned. "
                                     + "Red: overlaps another booking on the same instrument. The line is today.");
        }
    }

    // ---------------------------------------------------------------- pieces

    private static void ItemLink(Html h, DashboardItem item, Links links)
    {
        if (links == Links.App)
        {
            h.Open("a", "item", null, ("href", OpenPrefix + Uri.EscapeDataString(item.Name)), ("title", item.Title ?? item.Name))
                .Text(item.Name).Close("a");
        }
        else if (item.Wiki is { } wiki && wiki.Folder.StartsWith('/'))
        {
            // The project's own page on Panorama, which collaborators may read too.
            var page = wiki.PageName == "default" ? $"{wiki.Folder.TrimEnd('/')}/project-begin.view"
                : $"{wiki.Folder.TrimEnd('/')}/wiki-page.view?name={Uri.EscapeDataString(wiki.PageName)}";
            h.Open("a", "item", null, ("href", page)).Text(item.Name).Close("a");
        }
        else
        {
            h.Element("span", "item-plain", item.Name);
        }
    }

    private static void More(Html h, int more)
    {
        if (more > 0)
        {
            h.Element("p", "empty", $"and {more} more; open LabOps to see them all.");
        }
    }

    private static string Counted(string label, int count) => count > 0 ? $"{label} ({count})" : label;

    private static string Days(int days) => days == 1 ? "1 day late" : $"{days} days late";

    private static string Short(DateOnly date) => date.ToString("MMM d", English);

    private static string Range(DateOnly from, DateOnly to) => from == to ? Short(from) : $"{Short(from)} to {Short(to)}";

    /// <summary>
    /// Writes elements with classes (for the app's stylesheet) or with the classes' declarations
    /// inline (for Panorama). Text and attribute values are always encoded.
    /// </summary>
    private sealed class Html(bool inline)
    {
        private readonly StringBuilder _text = new();

        public Html Raw(string html)
        {
            _text.Append(html);
            return this;
        }

        public Html Text(string text)
        {
            _text.Append(WebUtility.HtmlEncode(text));
            return this;
        }

        public Html Open(string tag, string? classes = null, string? style = null, params (string Name, string Value)[] attributes)
        {
            _text.Append('<').Append(tag);
            var css = inline ? Styles.Inline(classes) + (style ?? "") : style;
            if (!inline && !string.IsNullOrEmpty(classes))
            {
                _text.Append(" class=\"").Append(classes).Append('"');
            }

            if (!string.IsNullOrEmpty(css))
            {
                _text.Append(" style=\"").Append(WebUtility.HtmlEncode(css)).Append('"');
            }

            foreach (var (name, value) in attributes)
            {
                _text.Append(' ').Append(name).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
            }

            _text.Append('>');
            return this;
        }

        public Html Close(string tag)
        {
            _text.Append("</").Append(tag).Append('>');
            return this;
        }

        public Html Element(string tag, string? classes, string text, string? style = null, params (string Name, string Value)[] attributes) =>
            Open(tag, classes, style, attributes).Text(text).Close(tag);

        public override string ToString() => _text.ToString();
    }
}
