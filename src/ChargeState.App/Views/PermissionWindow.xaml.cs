using System.Text.Json;
using System.Windows;
using ChargeState.Core.Claude;

namespace ChargeState.App.Views;

public partial class PermissionWindow : Window
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private PermissionWindow(PermissionRequest request, string? remembers)
    {
        InitializeComponent();
        Description.Text = request.Description;
        Details.Text = $"{request.ToolName}\n{JsonSerializer.Serialize(request.Input, Indented)}";
        if (remembers is null)
        {
            // A command hidden inside another cannot be judged by its programs, so it is always asked about.
            Remember.Visibility = Visibility.Collapsed;
        }
        else
        {
            RememberText.Text = $"Allow steps that use {remembers} until ChargeState closes";
        }
    }

    /// <summary>Asks the user; Don't allow is the default button, so Enter is the safe choice.</summary>
    /// <param name="request">What Claude wants to do.</param>
    /// <param name="remembers">What the checkbox would allow from now on ("uv and sed"); null to hide it.</param>
    public static (bool Allow, bool Remember) Ask(PermissionRequest request, string? remembers)
    {
        var window = new PermissionWindow(request, remembers) { Owner = Application.Current.MainWindow };
        var allowed = window.ShowDialog() == true;
        return (allowed, allowed && window.Remember.IsChecked == true);
    }

    private void OnAllow(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDeny(object sender, RoutedEventArgs e) => DialogResult = false;
}
