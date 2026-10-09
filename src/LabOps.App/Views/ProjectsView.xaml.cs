using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Views;

/// <summary>
/// The Projects area; its DataContext is the <see cref="ProjectsViewModel"/>. The overview is a page
/// the app draws, shown with no scripts; a click on a project in it is caught here and shows that
/// project in the list.
/// </summary>
public partial class ProjectsView : UserControl
{
    private ProjectsViewModel? _vm;
    private bool _started;
    private bool _ready;
    private string? _page;

    public ProjectsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Puts the cursor in the search box.</summary>
    public void FocusSearch() => SearchBox.Focus();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
            _vm.Overview.PropertyChanged -= OnOverviewChanged;
        }

        _vm = DataContext as ProjectsViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            _vm.Overview.PropertyChanged += OnOverviewChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectsViewModel.ShowOverview) && _vm?.ShowOverview == true)
        {
            // The browser starts the first time the overview is shown, not with the app.
            _ = StartAsync();
            ShowPage();
        }
    }

    private void OnOverviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverviewViewModel.Html))
        {
            ShowPage();
        }
    }

    private async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            var paths = App.Services.GetRequiredService<AppPaths>();
            await WebViewGuard.SecureAsync(OverviewPage, paths, uri => _vm?.Overview.Follow(uri) == true);
            OverviewPage.CoreWebView2.NavigationStarting += OnNavigating;
            _page = Path.Combine(paths.Root, "overview", "overview.html");
            _ready = true;
            ShowPage();
        }
        catch (Exception ex)
        {
            // Whatever stopped it (no runtime, a profile folder it may not use), the overview says
            // it cannot be shown rather than staying blank; the list still works.
            Serilog.Log.Warning(ex, "The WebView2 browser could not start; the projects overview is disabled.");
            OverviewPage.Visibility = Visibility.Collapsed;
            NoOverview.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Shows the overview's page, written to a file in the app's folder first: a page passed as a
    /// string is limited to 2 MB, which a large lab's board could pass.
    /// </summary>
    private void ShowPage()
    {
        if (!_ready || _page is null || _vm is not { ShowOverview: true } vm || string.IsNullOrEmpty(vm.Overview.Html))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_page)!);
            File.WriteAllText(_page, vm.Overview.Html, new UTF8Encoding(false));
            // The query makes the page load afresh each time it is drawn again.
            OverviewPage.CoreWebView2.Navigate($"{new Uri(_page).AbsoluteUri}?v={DateTime.UtcNow.Ticks}");
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(ex, "The projects overview could not be written to {Page}.", _page);
        }
    }

    /// <summary>The page itself loads; a project in it opens in the list; anything else is not followed.</summary>
    private void OnNavigating(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (WebViewGuard.IsThePage(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        if (_vm?.Overview.Follow(e.Uri) != true)
        {
            WebViewGuard.OpenOutside(e.Uri);
        }
    }
}
