using System.Windows;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App.Views;

/// <summary>What the user entered for a stage update.</summary>
/// <param name="Date">When it happened; null for a skip.</param>
/// <param name="Note">The note, or null to leave any existing note alone.</param>
public sealed record StageAnswer(DateOnly? Date, string? Note);

/// <summary>Confirms a stage update, with its date (today unless changed) and an optional note.</summary>
public partial class StageWindow : Window
{
    private StageWindow(Window? owner, string heading, string message, string okText, bool askDate)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Message.Text = message;
        OkButton.Content = okText;
        When.SelectedDate = DateTime.Today;
        DateRow.Visibility = askDate ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Note.Focus();
    }

    /// <summary>Returns the date and note, or null if cancelled.</summary>
    public static StageAnswer? Ask(Window? owner, string heading, string message, string okText, bool askDate)
    {
        var window = new StageWindow(owner, heading, message, okText, askDate);
        if (window.ShowDialog() != true)
        {
            return null;
        }

        var date = askDate && window.When.SelectedDate is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var note = string.IsNullOrWhiteSpace(window.Note.Text) ? null : window.Note.Text.Trim();
        return new StageAnswer(date, note);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (DateRow.Visibility == Visibility.Visible && When.SelectedDate is { } d && d.Date > DateTime.Today)
        {
            MessageBox.Show(this, "The date is in the future. Choose the day it happened.", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
