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
    private bool _shutDown;

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
        // The quote preview lives in the Quotes area, which may be hidden (the Projects area opens
        // first), so startup does not wait for it.
        var preview = InitializePreviewAsync();
        await _vm.InitializeAsync(this);
        FocusSearch();
        await preview;
    }

    private async Task InitializePreviewAsync()
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
    }

    private void FocusSearch()
    {
        if (_vm.IsQuotesArea)
        {
            SearchBox.Focus();
        }
        else
        {
            ProjectsArea.FocusSearch();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewHtml) && _previewReady)
        {
            Preview.NavigateToString(_vm.PreviewHtml);
        }
        else if (e.PropertyName == nameof(MainViewModel.Area) && IsLoaded)
        {
            Dispatcher.InvokeAsync(FocusSearch);
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

    /// <summary>
    /// Ends Claude and stops the tool server before the window goes, then closes it for real.
    /// </summary>
    /// <remarks>
    /// The first close is cancelled so the shutdown can be awaited while the UI thread keeps
    /// running. Waiting for it synchronously in App.OnExit deadlocked: ending a Claude session
    /// resumes on the UI thread, which was blocked waiting for it, so the process outlived its
    /// window and kept the single-instance lock.
    /// <para>
    /// The second close is queued behind this handler. With nothing to shut down (no Claude
    /// conversation) the shutdown finishes at once, so a Close() here would still be inside this
    /// Closing event, which WPF refuses: the app showed an error and stayed open, disabled.
    /// </para>
    /// </remarks>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutDown)
        {
            return;
        }

        if (_vm.Chat.IsBusy || _vm.IsWorking)
        {
            var answer = MessageBox.Show(
                "Work is still in progress. Close anyway? Anything Claude has not finished will not be saved.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        e.Cancel = true;
        _shutDown = true;
        IsEnabled = false;
        Title = $"{AppInfo.ProductName}: closing...";
        try
        {
            await _vm.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            // Closing must not fail; the exit watchdog ends anything left behind.
            Serilog.Log.Warning(ex, "Shutdown did not finish cleanly.");
        }

        _ = Dispatcher.BeginInvoke(Close);
    }
}
