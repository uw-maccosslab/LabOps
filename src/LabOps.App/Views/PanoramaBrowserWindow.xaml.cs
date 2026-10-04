using System.Windows;
using LabOps.App.ViewModels;

namespace LabOps.App.Views;

/// <summary>Browses Panorama's folders, as PanoramaBridge's remote browser does, and returns the one chosen.</summary>
public partial class PanoramaBrowserWindow : Window
{
    private readonly PanoramaBrowserViewModel _vm;
    private bool _chosen;

    private PanoramaBrowserWindow(Window? owner, PanoramaBrowserViewModel vm)
    {
        InitializeComponent();
        Owner = owner;
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) => await _vm.InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>The folder chosen (/MacCoss/maccoss/@files/2026-BioTRACK), or null; and whether the sign-in failed.</summary>
    public static (string? Folder, bool SignInFailed) Ask(Window? owner, PanoramaBrowserViewModel vm)
    {
        var window = new PanoramaBrowserWindow(owner, vm);
        window.ShowDialog();
        return (window._chosen ? vm.Chosen : null, vm.SignInFailed);
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.Selected = e.NewValue as PanoramaFolderNode;

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        _chosen = _vm.CanChoose;
        DialogResult = _chosen;
    }
}
