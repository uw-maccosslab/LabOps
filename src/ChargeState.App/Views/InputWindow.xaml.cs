using System.Windows;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App.Views;

/// <summary>A small dialog for one line of text, or a yes/no confirmation.</summary>
public partial class InputWindow : Window
{
    private InputWindow(Window? owner, string heading, string message, bool withInput, string okText)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Message.Text = message;
        OkButton.Content = okText;
        if (withInput)
        {
            Answer.Visibility = Visibility.Visible;
            Loaded += (_, _) => Answer.Focus();
        }
    }

    /// <summary>Returns the text entered (possibly empty), or null if cancelled.</summary>
    public static string? Ask(Window? owner, string heading, string message)
    {
        var window = new InputWindow(owner, heading, message, withInput: true, "OK");
        return window.ShowDialog() == true ? window.Answer.Text.Trim() : null;
    }

    internal static bool Confirm(Window? owner, string heading, string message, string okText) =>
        new InputWindow(owner, heading, message, withInput: false, okText).ShowDialog() == true;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}

/// <summary>A confirmation with a named action button, for steps that cannot be undone.</summary>
public static class ConfirmWindow
{
    public static bool Ask(Window? owner, string heading, string message, string okText) =>
        InputWindow.Confirm(owner, heading, message, okText);
}
