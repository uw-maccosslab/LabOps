using System.Windows;
using LabOps.App.ViewModels;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Views;

public partial class NewProjectWindow : Window
{
    private readonly NewProjectViewModel _vm = new();

    private NewProjectWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => DescriptionBox.Focus();
    }

    /// <summary>Shows the form; returns what was filled in, or null if cancelled.</summary>
    public static NewProjectViewModel? Ask(Window? owner)
    {
        var window = new NewProjectWindow { Owner = owner };
        return window.ShowDialog() == true ? window._vm : null;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsComplete)
        {
            MessageBox.Show(this, "Describe the project, or paste an email about it.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
