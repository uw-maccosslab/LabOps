using System.ComponentModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ChargeState.App.Services;
using ChargeState.App.ViewModels;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppPaths _paths;
    private bool _previewReady;

    public MainWindow(MainViewModel vm, AppPaths paths)
    {
        _vm = vm;
        _paths = paths;
        InitializeComponent();
        DataContext = vm;
        Loaded += OnLoaded;
        Closing += OnClosing;
        vm.PropertyChanged += OnViewModelChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // The browser profile goes in the app's data folder, not beside the executable.
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(_paths.Root, "webview2"));
            await Preview.EnsureCoreWebView2Async(environment);
            Preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Preview.CoreWebView2.NavigationStarting += OnPreviewNavigating;
            _previewReady = true;
            Preview.NavigateToString(_vm.PreviewHtml);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException)
        {
            Serilog.Log.Warning(ex, "The WebView2 runtime is unavailable; the preview is disabled.");
        }

        await _vm.InitializeAsync(this);
        SearchBox.Focus();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewHtml) && _previewReady)
        {
            Preview.NavigateToString(_vm.PreviewHtml);
        }
    }

    /// <summary>Links in a quote (the services portal, an email address) open outside the app.</summary>
    private void OnPreviewNavigating(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:", StringComparison.Ordinal) || e.Uri.StartsWith("about:", StringComparison.Ordinal))
        {
            return;
        }

        e.Cancel = true;
        if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            Shell.Open(e.Uri);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.Chat.IsBusy || _vm.IsWorking)
        {
            var answer = MessageBox.Show(
                "Work is still in progress. Close anyway? Anything Claude has not finished will not be saved.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            e.Cancel = answer != MessageBoxResult.Yes;
        }
    }
}
