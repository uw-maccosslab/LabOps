using System.IO;
using LabOps.Core.Infrastructure;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LabOps.App.Services;

/// <summary>
/// How the app shows pages it draws itself (a protocol, the projects overview) in a WebView2: no
/// scripts, no developer tools, nothing loaded but the page itself, and a link to the web opened
/// in the browser rather than inside the app. A page's text can then neither run code here nor
/// send anything out.
/// </summary>
internal static class WebViewGuard
{
    /// <summary>Starts the view's browser with the app's own profile and locks it down.</summary>
    /// <param name="ownLinks">
    /// Handles one of the page's own links (true when it was one), so that opening it in a new
    /// window (ctrl-click, middle-click, the context menu) does what a click does rather than
    /// sending it to the browser.
    /// </param>
    public static async Task SecureAsync(WebView2 view, AppPaths paths, Func<string, bool>? ownLinks = null)
    {
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(paths.Root, "webview2"));
        await view.EnsureCoreWebView2Async(environment);
        var core = view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsScriptEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (IsBlocked(args.Request.Uri, args.ResourceContext))
            {
                args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            }
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (ownLinks?.Invoke(args.Uri) != true)
            {
                OpenOutside(args.Uri);
            }
        };
    }

    /// <summary>
    /// What a page may not load: anything from the web, and any file other than the page itself
    /// (figures are inside the page). Following a link is a navigation, which each view handles.
    /// </summary>
    internal static bool IsBlocked(string uri, CoreWebView2WebResourceContext context) =>
        context != CoreWebView2WebResourceContext.Document
        && !uri.StartsWith("data:", StringComparison.Ordinal) && !uri.StartsWith("about:", StringComparison.Ordinal);

    /// <summary>Whether a navigation is to the page itself (a file the app wrote, or a string it showed).</summary>
    internal static bool IsThePage(string uri) =>
        uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("data:", StringComparison.Ordinal)
        || uri.StartsWith("about:", StringComparison.Ordinal);

    /// <summary>Opens a web or mail link in the person's browser or mail program; anything else is ignored.</summary>
    internal static void OpenOutside(string uri)
    {
        if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            Shell.Open(uri);
        }
    }
}
