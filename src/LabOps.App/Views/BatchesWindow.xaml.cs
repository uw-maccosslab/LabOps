using System.Windows;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Views;

/// <summary>How many batches a step's work is done in: sample prep of three plates, say.</summary>
public partial class BatchesWindow : Window
{
    /// <summary>The most offered, as the engine allows (ProjectsEngine.MaxBatches).</summary>
    private const int Most = 48;

    private BatchesWindow(Window? owner, string heading, int? current, int suggested)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Message.Text = current is null
            ? "Each batch becomes a step of its own under this one, which is started, finished, assigned and planned on "
              + "its own. What is recorded so far stays: the start with the first batch, the finish with the last."
            : "More batches are added after the last one. Fewer batches remove the last ones, as long as nothing is "
              + "recorded, planned or linked for them.";
        Ok.Content = current is null ? "Split" : "Change";
        Count.ItemsSource = Enumerable.Range(2, Most - 1).ToList();
        Count.SelectedItem = Math.Clamp(current ?? suggested, 2, Most);
        Loaded += (_, _) => Count.Focus();
    }

    /// <summary>The number of batches chosen, or null if cancelled.</summary>
    /// <param name="current">How many batches the step has now; null for a step done all at once.</param>
    /// <param name="suggested">The count to offer first for a step not yet in batches (the layout's plates).</param>
    public static int? Ask(Window? owner, string heading, int? current, int suggested)
    {
        var window = new BatchesWindow(owner, heading, current, suggested);
        return window.ShowDialog() == true ? window.Count.SelectedItem as int? : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
