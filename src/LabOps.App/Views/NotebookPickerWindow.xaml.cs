using System.Windows;
using System.Windows.Input;
using LabOps.App.ViewModels;
using LabOps.Core.Panorama;

namespace LabOps.App.Views;

/// <summary>Lists the lab's ELN notebooks on Panorama and returns the one chosen.</summary>
public partial class NotebookPickerWindow : Window
{
    private readonly NotebookPickerViewModel _vm;
    private PanoramaNotebook? _chosen;

    private NotebookPickerWindow(Window? owner, NotebookPickerViewModel vm)
    {
        InitializeComponent();
        Owner = owner;
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            Search.Focus();
            await _vm.InitializeAsync().ConfigureAwait(true);
        };
    }

    /// <summary>The notebook chosen, or null if cancelled.</summary>
    public static PanoramaNotebook? Ask(Window? owner, NotebookPickerViewModel vm)
    {
        var window = new NotebookPickerWindow(owner, vm);
        window.ShowDialog();
        return window._chosen;
    }

    private void OnChoose(object sender, RoutedEventArgs e) => Choose();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => Choose();

    private void Choose()
    {
        if (_chosen is not null || _vm.Selected is not { } notebook)
        {
            return;
        }

        _chosen = notebook;
        DialogResult = true;
    }
}
