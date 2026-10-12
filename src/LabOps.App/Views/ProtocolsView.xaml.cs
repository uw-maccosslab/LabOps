using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Infrastructure;
using LabOps.Core.Protocols;

namespace LabOps.App.Views;

/// <summary>
/// The Protocols area; its DataContext is the <see cref="ProtocolsViewModel"/>. The page of the
/// version shown is a file the engine rendered, with its figures inside it.
/// </summary>
public partial class ProtocolsView : UserControl
{
    private readonly HiddenLoads _loads;
    private bool _started;
    private bool _ready;
    private ProtocolsViewModel? _vm;

    public ProtocolsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        // A page loaded while the preview was hidden (the first protocol's, before any was selected)
        // is never drawn, so the preview showed a black box until the protocol's page rendered.
        _loads = WebViewGuard.RedrawWhenShown(Preview, ShowPage);
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
            await WebViewGuard.SecureAsync(Preview, App.Services.GetRequiredService<AppPaths>(), OpenLinkedFile);
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

        _loads.Loading();
        if (_vm?.PreviewFile is { } file && File.Exists(file))
        {
            // The query makes a page rendered again under the same name load afresh.
            Preview.CoreWebView2.Navigate($"{new Uri(file).AbsoluteUri}?v={DateTime.UtcNow.Ticks}");
        }
        else
        {
            Preview.NavigateToString(WebViewGuard.BlankPage);
        }
    }

    /// <summary>
    /// The page itself loads here. A method it links (a KingFisher program) opens in its folder, and
    /// its web links (a supplier's catalog page) in the browser; no other file is followed.
    /// </summary>
    private void OnNavigating(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (WebViewGuard.IsPage(e.Uri, _vm?.PreviewFile))
        {
            return;
        }

        e.Cancel = true;
        if (!OpenLinkedFile(e.Uri))
        {
            WebViewGuard.OpenOutside(e.Uri);
        }
    }

    /// <summary>Shows a file the page links (a method) selected in its folder; false when the link names no such file.</summary>
    private bool OpenLinkedFile(string uri)
    {
        if (ProtocolFiles.Linked(uri, _vm?.ClonePath) is not { } file)
        {
            return false;
        }

        Shell.Reveal(file);
        return true;
    }
}
