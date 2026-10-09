using System.Windows;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Views;

/// <summary>When a step should start and finish; both null removes the plan.</summary>
public sealed record PlanAnswer(DateOnly? Start, DateOnly? Finish);

/// <summary>Plans a step: its planned start and finish.</summary>
public partial class PlanWindow : Window
{
    private PlanAnswer? _answer;

    private PlanWindow(Window? owner, string heading, DateOnly? start, DateOnly? finish)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Start.SelectedDate = start?.ToDateTime(TimeOnly.MinValue);
        Finish.SelectedDate = finish?.ToDateTime(TimeOnly.MinValue);
        ClearButton.Visibility = start is null && finish is null ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => Start.Focus();
    }

    /// <summary>The plan to record, or null if canceled.</summary>
    public static PlanAnswer? Ask(Window? owner, string heading, DateOnly? start, DateOnly? finish)
    {
        var window = new PlanWindow(owner, heading, start, finish);
        return window.ShowDialog() == true ? window._answer : null;
    }

    /// <summary>Why the dates cannot be a plan, or null when they can.</summary>
    public static string? WhyNot(DateOnly? start, DateOnly? finish) =>
        start is { } s && finish is { } f && f < s ? "The finish is before the start." : null;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var start = Start.SelectedDate is { } s ? DateOnly.FromDateTime(s) : (DateOnly?)null;
        var finish = Finish.SelectedDate is { } f ? DateOnly.FromDateTime(f) : (DateOnly?)null;
        if (WhyNot(start, finish) is { } problem)
        {
            Problem.Text = problem;
            Problem.Visibility = Visibility.Visible;
            return;
        }

        _answer = new PlanAnswer(start, finish);
        DialogResult = true;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _answer = new PlanAnswer(null, null);
        DialogResult = true;
    }
}
