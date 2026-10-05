using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Views;

/// <summary>
/// The Protocols area; its DataContext is the <see cref="ProtocolsViewModel"/>. The page of the
/// version shown is a file the engine rendered, with its figures inside it.
/// </summary>
public partial class ProtocolsView : UserControl
{
    private bool _started;
    private bool _ready;
    private ProtocolsViewModel? _vm;

    public ProtocolsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Puts the cursor in the search box.</summary>
    public void FocusSearch() => SearchBox.Focus();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }

        _vm = DataContext as ProtocolsViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            var paths = App.Services.GetRequiredService<AppPaths>();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(paths.Root, "webview2"));
            await Preview.EnsureCoreWebView2Async(environment);
            Preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // The page is the engine's, with its figures inside it: it needs no scripts and nothing
            // from the web, so a protocol's text can neither run code here nor send anything out.
            Preview.CoreWebView2.Settings.IsScriptEnabled = false;
            Preview.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            Preview.CoreWebView2.WebResourceRequested += (_, args) =>
            {
                if (IsBlocked(args.Request.Uri, args.ResourceContext))
                {
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                }
            };
            Preview.CoreWebView2.NavigationStarting += OnNavigating;
            Preview.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenOutside(args.Uri);
            };
            _ready = true;
            ShowPage();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException)
        {
            Serilog.Log.Warning(ex, "The WebView2 runtime is unavailable; the protocol preview is disabled.");
            Preview.Visibility = Visibility.Collapsed;
            NoPreview.Visibility = Visibility.Visible;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProtocolsViewModel.PreviewFile))
        {
            ShowPage();
        }
    }

    private void ShowPage()
    {
        if (!_ready)
        {
            return;
        }

        if (_vm?.PreviewFile is { } file && File.Exists(file))
        {
            // The query makes a page rendered again under the same name load afresh.
            Preview.CoreWebView2.Navigate($"{new Uri(file).AbsoluteUri}?v={DateTime.UtcNow.Ticks}");
        }
        else
        {
            Preview.NavigateToString("<!doctype html><html><body style=\"font-family:Segoe UI;color:#5b6670\"></body></html>");
        }
    }

    /// <summary>The page itself loads here; its links (a supplier's catalog page) open in the browser.</summary>
    private void OnNavigating(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("data:", StringComparison.Ordinal)
            || e.Uri.StartsWith("about:", StringComparison.Ordinal))
        {
            return;
        }

        e.Cancel = true;
        OpenOutside(e.Uri);
    }

    /// <summary>
    /// What the page may not load: anything from the web, and any file other than the page itself
    /// (the engine puts its figures inside the page). Following a link is a navigation, handled
    /// by <see cref="OnNavigating"/>.
    /// </summary>
    internal static bool IsBlocked(string uri, CoreWebView2WebResourceContext context) =>
        context != CoreWebView2WebResourceContext.Document
        && !uri.StartsWith("data:", StringComparison.Ordinal) && !uri.StartsWith("about:", StringComparison.Ordinal);

    private static void OpenOutside(string uri)
    {
        if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            Shell.Open(uri);
        }
    }
}
