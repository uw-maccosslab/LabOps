using System.Windows;
using ChargeState.App.ViewModels;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App.Views;

public partial class NewExperimentWindow : Window
{
    private readonly NewExperimentViewModel _vm = new();

    private NewExperimentWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => DescriptionBox.Focus();
    }

    /// <summary>Shows the form; returns what was filled in, or null if cancelled.</summary>
    public static NewExperimentViewModel? Ask(Window? owner)
    {
        var window = new NewExperimentWindow { Owner = owner };
        return window.ShowDialog() == true ? window._vm : null;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsComplete)
        {
            MessageBox.Show(this, "Describe the experiment, or paste an email about it.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
