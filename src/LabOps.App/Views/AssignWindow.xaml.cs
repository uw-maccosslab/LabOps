using System.Windows;
using LabOps.Core.Infrastructure;
using LabOps.Core.Projects;

namespace LabOps.App.Views;

/// <summary>Who a step goes to.</summary>
/// <param name="Login">GitHub login, or null for nobody.</param>
/// <param name="AlsoLater">Also give them the later steps of the timeline that nobody has.</param>
public sealed record AssignAnswer(string? Login, bool AlsoLater);

/// <summary>Assigns a step to someone in config/people.yaml, or to nobody.</summary>
public partial class AssignWindow : Window
{
    private AssignAnswer? _answer;

    private AssignWindow(Window? owner, string heading, IReadOnlyList<Person> people, string? current, int laterUnassigned)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Message.Text = "Choose who does this step. Someone not in the list needs to be added to config/people.yaml first; "
            + "Ask Claude to update the project can do that.";

        var choices = people.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new Choice(p.Login, p.Name is null ? p.Login : $"{p.Name} ({p.Login})")).ToList();
        if (current is not null && choices.All(c => !string.Equals(c.Login, current, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new Choice(current, $"{current} (not in config/people.yaml)"));
        }

        choices.Add(new Choice(null, "Nobody"));
        Who.ItemsSource = choices;
        Who.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Login, current, StringComparison.OrdinalIgnoreCase))
            ?? choices[0];

        Later.Content = laterUnassigned == 1
            ? "Also the 1 later step nobody has"
            : $"Also the {laterUnassigned} later steps nobody has";
        Later.Visibility = laterUnassigned > 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Who.Focus();
    }

    /// <summary>Who to assign, or null if cancelled.</summary>
    /// <param name="current">The step's assignee now, if any.</param>
    /// <param name="laterUnassigned">How many later steps nobody has, offered as well.</param>
    public static AssignAnswer? Ask(Window? owner, string heading, IReadOnlyList<Person> people, string? current, int laterUnassigned)
    {
        var window = new AssignWindow(owner, heading, people, current, laterUnassigned);
        return window.ShowDialog() == true ? window._answer : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _answer = new AssignAnswer((Who.SelectedItem as Choice)?.Login, Later.IsChecked == true);
        DialogResult = true;
    }

    private sealed record Choice(string? Login, string Label)
    {
        // What screen readers and UI Automation read for the list item.
        public override string ToString() => Label;
    }
}
