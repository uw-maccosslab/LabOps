using System.Windows;
using LabOps.Core.Infrastructure;
using LabOps.Core.Projects;

namespace LabOps.App.Views;

/// <summary>A step to add to a timeline.</summary>
/// <param name="Kind">One of <see cref="StageNames.Kinds"/>.</param>
/// <param name="Label">How it reads, or null for the kind's own name.</param>
/// <param name="After">The step's id to put it after, or null.</param>
/// <param name="Before">The step's id to put it before, or null. With neither, it goes at the end.</param>
public sealed record AddStepAnswer(string Kind, string? Label, string? After, string? Before);

/// <summary>Asks what step to add to a project's or an experiment's timeline, and where.</summary>
public partial class AddStepWindow : Window
{
    private readonly string? _first;
    private AddStepAnswer? _answer;

    private AddStepWindow(Window? owner, string heading, IReadOnlyList<StageEntry> steps)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Kind.ItemsSource = StageNames.Kinds.Select(k => new Choice(k, StageNames.Label(k))).ToList();
        Kind.SelectedIndex = 0;

        // After the last step unless chosen otherwise: after any step, or first of all.
        var positions = steps.Select(s => new Choice(s.Stage, $"After {s.DisplayLabel}")).ToList();
        if (steps.Count > 0)
        {
            positions.Insert(0, new Choice(null, "First"));
        }

        Position.ItemsSource = positions;
        Position.SelectedIndex = positions.Count - 1;
        _first = steps.Count > 0 ? steps[0].Stage : null;
        Loaded += (_, _) => Kind.Focus();
    }

    /// <summary>The step to add, or null if cancelled.</summary>
    public static AddStepAnswer? Ask(Window? owner, string heading, IReadOnlyList<StageEntry> steps)
    {
        var window = new AddStepWindow(owner, heading, steps);
        return window.ShowDialog() == true ? window._answer : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var kind = (Kind.SelectedItem as Choice)?.Value ?? "other";
        var label = string.IsNullOrWhiteSpace(Label.Text) ? null : Label.Text.Trim();
        if (kind == "other" && label is null)
        {
            MessageBox.Show(this, "Give the step a label that says what it is.", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            Label.Focus();
            return;
        }

        var after = (Position.SelectedItem as Choice)?.Value;
        _answer = new AddStepAnswer(kind, label, after, after is null ? _first : null);
        DialogResult = true;
    }

    private sealed record Choice(string? Value, string Label)
    {
        // What screen readers and UI Automation read for the list item.
        public override string ToString() => Label;
    }
}
