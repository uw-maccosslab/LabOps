using System.ComponentModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Infrastructure;
using LabOps.Core.Panorama;

namespace LabOps.App.Views;

/// <summary>The project's wiki page as it would be published, with Publish and Write the text with Claude.</summary>
public partial class WikiWindow : Window
{
    private readonly WikiViewModel _vm;
    private readonly string _webViewFolder;
    private bool _previewReady;

    private WikiWindow(Window? owner, WikiViewModel vm, string webViewFolder, Func<Task<PanoramaClient?>> signIn)
    {
        InitializeComponent();
        Owner = owner;
        _vm = vm;
        _webViewFolder = webViewFolder;
        DataContext = vm;
        vm.PropertyChanged += OnViewModelChanged;
        Loaded += async (_, _) =>
        {
            await StartPreviewAsync().ConfigureAwait(true);
            await _vm.LoadAsync(signIn).ConfigureAwait(true);
        };
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelChanged;
    }

    /// <summary>Shows the window until it is closed; the view model says whether Claude should write the text.</summary>
    public static void Show(Window? owner, WikiViewModel vm, string webViewFolder, Func<Task<PanoramaClient?>> signIn) =>
        new WikiWindow(owner, vm, webViewFolder, signIn).ShowDialog();

    private async Task StartPreviewAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _webViewFolder).ConfigureAwait(true);
            await Preview.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            Preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Preview.CoreWebView2.NavigationStarting += OnNavigating;
            Preview.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                Shell.Open(e.Uri);
            };
            _previewReady = true;
            if (_vm.PreviewHtml is { } html)
            {
                Preview.NavigateToString(html);
            }
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException)
        {
            Serilog.Log.Warning(ex, "The WebView2 runtime is unavailable; the wiki preview is disabled.");
            Preview.Visibility = Visibility.Collapsed;
            NoPreview.Visibility = Visibility.Visible;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WikiViewModel.PreviewHtml) && _previewReady && _vm.PreviewHtml is { } html)
        {
            Preview.NavigateToString(html);
        }
    }

    /// <summary>Links in the page open in the browser, as they would on Panorama.</summary>
    private void OnNavigating(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:", StringComparison.Ordinal) || e.Uri.StartsWith("about:", StringComparison.Ordinal))
        {
            return;
        }

        e.Cancel = true;
        if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Shell.Open(e.Uri);
        }
    }

    private void OnOpenOnPanorama(object sender, RoutedEventArgs e) => Shell.Open(_vm.PageUrl);

    private void OnWriteText(object sender, RoutedEventArgs e)
    {
        _vm.WriteTextRequested = true;
        Close();
    }

    private async void OnPublish(object sender, RoutedEventArgs e)
    {
        if (_vm.ReplacesHandWritten && MessageBox.Show(this,
                $"The page on Panorama ({_vm.Where}) was written or edited by hand there. Replace it with this one? Panorama keeps "
                + "the earlier version in the page's history.", AppInfo.ProductName, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        await _vm.PublishAsync().ConfigureAwait(true);
    }
}
