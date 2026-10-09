namespace LabOps.Core.Projects.Dashboard;

/// <summary>The overview's views.</summary>
public enum DashboardView
{
    /// <summary>Late steps, steps due or starting this week, and records with errors.</summary>
    Attention,

    /// <summary>Every open project and experiment at its current step, in columns by phase.</summary>
    Board,

    /// <summary>Each item's steps on a calendar line: what happened, solid; what is planned, dashed.</summary>
    Timeline,

    /// <summary>A month, with each day's steps started, finished, planned to start and due.</summary>
    Calendar,

    /// <summary>Each instrument's data acquisitions, and any that overlap.</summary>
    Instruments,
}

/// <summary>Whose work and which lab a view shows; null shows everyone's and every lab.</summary>
public sealed record DashboardFilter(string? Person = null, string? Lab = null)
{
    public static DashboardFilter Everything { get; } = new();
}

/// <summary>
/// A project (its samples' steps) or an experiment (its measurement's steps): one line on the
/// board, the timeline and the calendar.
/// </summary>
public sealed record DashboardItem(
    string Name, bool IsExperiment, string Lab, string Project, string? Title, string? Instrument, string? LabContact,
    IReadOnlyList<StageEntry> Steps, StageEntry? Current, IReadOnlyList<ProjectIssue> Issues, WikiLocation? Wiki, string? Status = null)
{
    /// <summary>Paused (on_hold, or an experiment of a paused project): its plan may pass without it being late.</summary>
    public bool IsOnHold => Status == "on_hold";

    /// <summary>The step is late, and the work is not paused.</summary>
    public bool IsLate(StageEntry step, DateOnly today) => !IsOnHold && step.IsLate(today);

    /// <summary>
    /// Whether a step is the person's: assigned to them, recorded by them, or nobody's on an item
    /// they are the lab contact for. Everyone's, without a person.
    /// </summary>
    public bool IsTheirs(StageEntry step, DashboardFilter filter) =>
        filter.Person is not { } person
        || string.Equals(step.Assigned, person, StringComparison.OrdinalIgnoreCase)
        || string.Equals(step.By, person, StringComparison.OrdinalIgnoreCase)
        || (step.Assigned is null && string.Equals(LabContact, person, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the item is the person's (they are its lab contact, or a step not yet done or
    /// skipped is assigned to them) and in the lab.
    /// </summary>
    public bool Matches(DashboardFilter filter) =>
        (filter.Lab is null || string.Equals(Lab, filter.Lab, StringComparison.OrdinalIgnoreCase))
        && (filter.Person is null
            || string.Equals(LabContact, filter.Person, StringComparison.OrdinalIgnoreCase)
            || Steps.Any(s => !(s.IsDone || s.IsSkipped) && string.Equals(s.Assigned, filter.Person, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>A stretch of a step on the calendar: when it ran (or is running), or when it is planned.</summary>
public sealed record StepSpan(DateOnly From, DateOnly To, bool Planned, StageEntry Step);

/// <summary>Something that happens to a step on a day: it started, finished, is planned to start or is due.</summary>
public sealed record DayEvent(DateOnly Day, DashboardItem Item, StageEntry Step, string What, bool Planned);

/// <summary>An experiment's data acquisition on its instrument, as recorded or as planned.</summary>
public sealed record Booking(string Instrument, DashboardItem Item, StageEntry Step, DateOnly From, DateOnly To, bool Planned);

/// <summary>
/// What the overview's views are drawn from: the open projects and experiments, flattened, and
/// what each view needs computed from them. Everything is a function of the list and today, so a
/// view is the same however often it is drawn.
/// </summary>
public static class DashboardModel
{
    /// <summary>The board's columns: each gathers the steps of those kinds.</summary>
    public static readonly IReadOnlyList<(string Id, string Label, string[] Kinds)> Phases =
    [
        ("samples", "Samples", ["samples_received", "metadata_organized", "plate_layout"]),
        ("prep", "Preparation", ["sample_prep", "assay_development"]),
        ("acquisition", "Acquisition", ["data_acquisition", "data_deposited"]),
        ("analysis", "Analysis", ["signal_processing", "data_analysis"]),
        ("results", "Results", ["results_returned"]),
    ];

    /// <summary>Every project and experiment that is not closed, lab by lab, each project before its experiments.</summary>
    public static List<DashboardItem> Items(ProjectList list)
    {
        var items = new List<DashboardItem>();
        foreach (var lab in list.Labs)
        {
            foreach (var p in lab.Projects.Where(p => !p.IsClosed))
            {
                items.Add(new DashboardItem(p.Project, false, p.Lab, p.Project, p.Title, null, p.LabContact ?? lab.LabContact,
                    p.Stages, p.Stages.FirstOrDefault(s => s.Stage == p.CurrentStage), p.Issues, p.Wiki, p.Status));
                items.AddRange(p.Experiments.Where(e => !e.IsClosed).Select(e => new DashboardItem(
                    e.Experiment, true, e.Lab, e.Project, e.Title, e.Instrument, e.LabContact ?? p.LabContact ?? lab.LabContact,
                    e.Stages, e.Current, e.Issues, p.Wiki, p.Status == "on_hold" ? "on_hold" : e.Status)));
            }
        }

        return items;
    }

    /// <summary>
    /// The board column of an item's current step. A step of another kind (a second shipment, say)
    /// goes with the next step it leads to, or else the one before it.
    /// </summary>
    public static int Phase(DashboardItem item)
    {
        if (item.Current is not { } current)
        {
            return -1;
        }

        var at = item.Steps.ToList().IndexOf(current);
        foreach (var step in item.Steps.Skip(at).Concat(item.Steps.Take(at).Reverse()))
        {
            var phase = PhaseOf(step.Kind);
            if (phase >= 0)
            {
                return phase;
            }
        }

        return item.IsExperiment ? 3 : 0;
    }

    private static int PhaseOf(string? kind)
    {
        for (var i = 0; i < Phases.Count; i++)
        {
            if (Phases[i].Kinds.Contains(kind))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A step's spans: when it ran (started to finished; to today while it is under way; a single
    /// day when only its finish was recorded), and when it is planned.
    /// </summary>
    public static IEnumerable<StepSpan> Spans(StageEntry step, DateOnly today)
    {
        var started = StageEntry.Date(step.Started);
        var finished = StageEntry.Date(step.Finished);
        if (started is { } s)
        {
            var end = finished ?? (step.IsDone || step.IsSkipped ? s : today);
            yield return new StepSpan(s, end < s ? s : end, false, step);
        }
        else if (finished is { } f)
        {
            yield return new StepSpan(f, f, false, step);
        }

        var plannedStart = StageEntry.Date(step.PlannedStart);
        var plannedFinish = StageEntry.Date(step.PlannedFinish);
        if ((plannedStart ?? plannedFinish) is { } from)
        {
            var to = plannedFinish ?? from;
            yield return new StepSpan(from, to < from ? from : to, true, step);
        }
    }

    /// <summary>Each day's events in [first, last]: steps started and finished, and planned to start or due.</summary>
    public static List<DayEvent> Events(IEnumerable<DashboardItem> items, DateOnly first, DateOnly last)
    {
        var events = new List<DayEvent>();
        foreach (var item in items)
        {
            foreach (var step in item.Steps)
            {
                void Add(string? date, string what, bool planned)
                {
                    if (StageEntry.Date(date) is { } day && day >= first && day <= last)
                    {
                        events.Add(new DayEvent(day, item, step, what, planned));
                    }
                }

                // A step started and finished the same day is one event.
                if (step.Started != step.Finished || step.Finished is null)
                {
                    Add(step.Started, "started", false);
                }

                Add(step.Finished, step.IsSkipped ? "skipped" : "finished", false);
                if (!(step.IsDone || step.IsSkipped))
                {
                    if (StageEntry.Date(step.Started) is null)
                    {
                        Add(step.PlannedStart, "planned to start", true);
                    }

                    Add(step.PlannedFinish, "due", true);
                }
            }
        }

        // Within a day: what happened before what is planned, then by item, as listed.
        return [.. events.OrderBy(e => e.Day).ThenBy(e => e.Planned)];
    }

    /// <summary>
    /// The instruments' bookings: each experiment's data acquisition, from its record (started,
    /// to finished or, while under way, to its planned finish or today) or else from its plan.
    /// The lab's own list comes first, in its order, then any other instrument an experiment names.
    /// </summary>
    public static List<Booking> Bookings(IEnumerable<DashboardItem> items, DateOnly today)
    {
        var bookings = new List<Booking>();
        foreach (var item in items.Where(i => i.IsExperiment && !string.IsNullOrWhiteSpace(i.Instrument)))
        {
            foreach (var step in item.Steps.Where(s => s.Kind == "data_acquisition" && !s.IsSkipped))
            {
                var started = StageEntry.Date(step.Started);
                var plannedStart = StageEntry.Date(step.PlannedStart);
                var plannedFinish = StageEntry.Date(step.PlannedFinish);
                if (started is { } s)
                {
                    var end = StageEntry.Date(step.Finished)
                              ?? (step.IsDone ? s : plannedFinish is { } pf && pf >= today ? pf : today);
                    bookings.Add(new Booking(item.Instrument!.Trim(), item, step, s, end < s ? s : end, false));
                }
                else if (StageEntry.Date(step.Finished) is { } f)
                {
                    // Only its finish was recorded: it happened, that day, whatever was planned.
                    bookings.Add(new Booking(item.Instrument!.Trim(), item, step, f, f, false));
                }
                else if ((plannedStart ?? plannedFinish) is { } from)
                {
                    var to = plannedFinish ?? from;
                    bookings.Add(new Booking(item.Instrument!.Trim(), item, step, from, to < from ? from : to, true));
                }
            }
        }

        return bookings;
    }

    /// <summary>Pairs of bookings on one instrument whose days overlap.</summary>
    public static List<(Booking First, Booking Second)> Conflicts(IReadOnlyList<Booking> bookings)
    {
        var conflicts = new List<(Booking, Booking)>();
        foreach (var group in bookings.GroupBy(b => b.Instrument, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(b => b.From).ThenBy(b => b.To).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count && ordered[j].From <= ordered[i].To; j++)
                {
                    if (ordered[j].Item != ordered[i].Item)
                    {
                        conflicts.Add((ordered[i], ordered[j]));
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>The instruments to show: the lab's list in its order, then others the bookings name.</summary>
    public static List<string> InstrumentNames(IReadOnlyList<string> listed, IEnumerable<Booking> bookings)
    {
        var names = listed.ToList();
        foreach (var name in bookings.Select(b => b.Instrument).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
