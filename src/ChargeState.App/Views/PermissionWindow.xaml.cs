using System.Text.Json;
using System.Windows;
using ChargeState.Core.Claude;

namespace ChargeState.App.Views;

public partial class PermissionWindow : Window
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private PermissionWindow(PermissionRequest request)
    {
        InitializeComponent();
        Description.Text = request.Description;
        Details.Text = $"{request.ToolName}\n{JsonSerializer.Serialize(request.Input, Indented)}";
    }

    /// <summary>Asks the user; Don't allow is the default button, so Enter is the safe choice.</summary>
    public static (bool Allow, bool Remember) Ask(PermissionRequest request)
    {
        var window = new PermissionWindow(request) { Owner = Application.Current.MainWindow };
        var allowed = window.ShowDialog() == true;
        return (allowed, allowed && window.Remember.IsChecked == true);
    }

    private void OnAllow(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDeny(object sender, RoutedEventArgs e) => DialogResult = false;
}
