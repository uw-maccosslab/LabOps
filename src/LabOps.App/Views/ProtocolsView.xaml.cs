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
            // The page is the engine's, with its figures inside it: it needs no scripts and nothing
            // from the web, so a protocol's text can neither run code here nor send anything out.
            await WebViewGuard.SecureAsync(Preview, App.Services.GetRequiredService<AppPaths>());
            Preview.CoreWebView2.NavigationStarting += OnNavigating;
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
        if (WebViewGuard.IsThePage(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        WebViewGuard.OpenOutside(e.Uri);
    }
}
