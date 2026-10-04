using System.Globalization;
using System.Net;
using ChargeState.App.Services;
using ChargeState.Core.Engines;
using ChargeState.Core.Panorama;
using ChargeState.Core.Processes;
using ChargeState.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChargeState.App.ViewModels;

/// <summary>
/// The Wiki page window: the project's page as it would be published, what is on Panorama now,
/// and Publish. Writing the text is Claude's (the update-wiki skill), from this window.
/// </summary>
public sealed partial class WikiViewModel(ProjectSummary project, WikiPublisher publisher) : ObservableObject, IDisposable
{
    private PanoramaClient? _client;
    private WikiPageContent? _content;
    private WikiPageInfo? _existing;

    public ProjectSummary Project { get; } = project;

    public WikiLocation Wiki => Project.Wiki!;

    public string Heading => $"Wiki page: {Project.Project}";

    public string Where => $"{Wiki.Folder}, page {Wiki.PageName}";

    public string PageUrl => publisher.PageUrl(Wiki);

    /// <summary>The page in a full document for the preview, its links resolved against the page's folder.</summary>
    [ObservableProperty] public partial string? PreviewHtml { get; private set; }

    [ObservableProperty] public partial string Status { get; private set; } = "Building the page...";

    [ObservableProperty] public partial string? Problem { get; private set; }

    [ObservableProperty] public partial bool CanPublish { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; } = true;

    public bool IsIdle => !IsBusy;

    /// <summary>Whether the page on Panorama now was written or edited by hand there, which Publish replaces.</summary>
    public bool ReplacesHandWritten => _existing is { IsChargeState: false } or { EditedOnPanorama: true };

    /// <summary>Set when the person chose Write the text with Claude.</summary>
    public bool WriteTextRequested { get; set; }

    /// <summary>Signs in (asking only if no saved sign-in works), builds the page and reads what is on Panorama.</summary>
    public async Task LoadAsync(Func<Task<PanoramaClient?>> signIn)
    {
        try
        {
            _client = await signIn().ConfigureAwait(true);
            _content = await publisher.BuildAsync(Project, _client).ConfigureAwait(true);
            if (_client is not null)
            {
                _existing = await _client.GetWikiPageAsync(Wiki.Folder, Wiki.PageName).ConfigureAwait(true);
            }

            PreviewHtml = Document(_content.Html, publisher.PageUrl(Wiki));
            Status = Describe(_client is not null, _existing, _content);
            CanPublish = _client is not null;
        }
        catch (Exception ex) when (ex is PanoramaException or EngineException or ToolMissingException or System.IO.IOException)
        {
            Problem = ex.Message;
            Status = "The page could not be built.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Publishes the page as previewed.</summary>
    public async Task PublishAsync()
    {
        if (_client is null || _content is null)
        {
            return;
        }

        IsBusy = true;
        CanPublish = false;
        Problem = null;
        try
        {
            _existing = await _client.SaveWikiPageAsync(Wiki.Folder, Wiki.PageName, _content.Title, _content.Html, _existing)
                .ConfigureAwait(true);
            Status = $"Published {DateTime.Now.ToString("t", CultureInfo.CurrentCulture)}. From now on the app updates the page "
                + "after every change, until its text changes again.";
        }
        catch (PanoramaException ex)
        {
            Problem = ex.Message;
        }
        finally
        {
            IsBusy = false;
            CanPublish = true;
        }

        OnPropertyChanged(nameof(ReplacesHandWritten));
    }

    /// <summary>What is on Panorama, and what Publish would do.</summary>
    internal static string Describe(bool signedIn, WikiPageInfo? existing, WikiPageContent content)
    {
        var state = !signedIn
            ? "Not signed in to Panorama, so the preview has no Skyline documents and the page cannot be published."
            : existing is null
                ? "Not on Panorama yet. Publish creates the page; after that, the app updates it after every change."
                : !existing.IsChargeState
                    ? "On Panorama now is a page written by hand. Publish replaces it with this one (Panorama keeps the earlier "
                      + "version in the page's history); after that, the app updates it after every change."
                    : existing.EditedOnPanorama
                        ? "The page on Panorama was edited there since the app published it, so the app has left it alone. Publish "
                          + "replaces those edits with this page (Panorama keeps them in the page's history)."
                    : existing.WrittenHash != content.WrittenHash
                        ? "The text has changed since the page was last published. Publish shares it; until then the app does not "
                          + "update the page on its own."
                        : "On Panorama, and updated by the app after every change.";
        return content.Written
            ? state
            : state + " The summary, plan and description of the samples are not written yet: choose Write the text with Claude.";
    }

    /// <summary>A full document around the page, as Panorama would show it: its fonts and icons, links relative to the folder.</summary>
    internal static string Document(string html, string pageUrl) =>
        "<!doctype html><html><head><meta charset=\"utf-8\">"
        + $"<base href=\"{WebUtility.HtmlEncode(pageUrl)}\">"
        + "<link rel=\"stylesheet\" href=\"https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css\">"
        + "<style>html{background:#fff}body{margin:20px 24px;font-family:Roboto,'Segoe UI',Helvetica,Arial,sans-serif;"
        + "color:#333}a{color:#126495}</style></head><body>" + html + "</body></html>";

    public void Dispose() => _client?.Dispose();
}
