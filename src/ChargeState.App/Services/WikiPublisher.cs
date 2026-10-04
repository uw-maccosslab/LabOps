using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ChargeState.Core.Engines;
using ChargeState.Core.Infrastructure;
using ChargeState.Core.Panorama;
using ChargeState.Core.Processes;
using ChargeState.Core.Projects;

namespace ChargeState.App.Services;

/// <summary>
/// Builds a project's wiki page (project.py wiki, with the Skyline documents read from Panorama)
/// and publishes it to the project's folder on Panorama.
/// </summary>
/// <remarks>
/// After each change saved in the app, <see cref="UpdateInBackground"/> republishes the page, but
/// only a page ChargeState published before (its footer is there) and only while the written parts
/// are the ones published then: new text from Claude, and the first publish, go through the Wiki
/// page window, where a person reviews them.
/// </remarks>
public sealed class WikiPublisher(PanoramaSignIn signIn, ProjectEngine engine, WorkTracker work, AppPaths paths, ILogger<WikiPublisher> log)
{
    private readonly Dictionary<string, ProjectSummary?> _queued = new(StringComparer.Ordinal);

    /// <summary>Stands in for Panorama in tests.</summary>
    internal HttpMessageHandler? Handler { get; init; }

    /// <summary>Where WebView2 keeps its data; the same folder as the chat's, as one process must use one.</summary>
    public string WebViewFolder => Path.Combine(paths.Root, "webview2");

    /// <summary>Panorama's address for a wiki page.</summary>
    public string PageUrl(WikiLocation wiki) =>
        $"{signIn.Server.GetLeftPart(UriPartial.Authority)}{PanoramaPaths.Encode(PanoramaPaths.AsFolder(wiki.Folder))}wiki-page.view?name="
        + Uri.EscapeDataString(wiki.PageName);

    /// <summary>
    /// The folder to suggest for a project's page: the one holding its experiments' Panorama folders
    /// (/MacCoss/Collaborations/MNRF/BioTRACK for .../BioTRACK/2026-09-BioTRACK-Quant and -Control).
    /// File areas (@files, often the lab's shared one) count only when there is nothing else, and
    /// the suggestion never climbs above three levels, where the lab's shared folders are.
    /// </summary>
    public static string? SuggestFolder(ProjectSummary project)
    {
        var all = project.Experiments.SelectMany(e => e.Panorama).Select(f => f.Folder).OfType<string>().ToList();
        var own = all.Where(f => !f.Contains("/@", StringComparison.Ordinal)).ToList();
        var folders = (own.Count > 0 ? own : all.Select(f => f.Split("/@", 2)[0]))
            .Select(f => f.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)).Where(s => s.Length > 0).ToList();
        if (folders.Count == 0)
        {
            return null;
        }

        var common = folders[0].AsEnumerable();
        foreach (var f in folders.Skip(1))
        {
            common = common.Zip(f).TakeWhile(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)).Select(p => p.First);
        }

        var shared = common.ToList();
        // One experiment's own folder is not the project's: its page goes one level up.
        if (folders.Any(f => f.Length == shared.Count) && shared.Count > 3)
        {
            shared.RemoveAt(shared.Count - 1);
        }

        // Two levels (/MacCoss/maccoss) is a shared folder, never one project's.
        return shared.Count < 3 ? null : "/" + string.Join('/', shared);
    }

    /// <summary>A client with a saved sign-in Panorama accepts, without asking anyone; null when none works.</summary>
    public async Task<PanoramaClient?> SignInQuietlyAsync()
    {
        foreach (var candidate in signIn.Candidates())
        {
            var client = new PanoramaClient(signIn.Server, candidate, Handler);
            try
            {
                await client.ListAsync("/_webdav/").ConfigureAwait(true);
                return client;
            }
            catch (PanoramaException ex)
            {
                client.Dispose();
                if (!ex.IsSignInProblem)
                {
                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the page. With a client, each results and process control folder's Skyline documents
    /// are read from Panorama first (a folder that cannot be read is listed without them).
    /// </summary>
    public async Task<WikiPageContent> BuildAsync(ProjectSummary project, PanoramaClient? client)
    {
        string? documentsFile = null;
        try
        {
            if (client is not null)
            {
                var documents = new Dictionary<string, object>();
                var folders = project.Experiments.Where(e => e.Status != "closed").SelectMany(e => e.Panorama)
                    .Where(f => f.Kind is "results" or "qc" && !string.IsNullOrWhiteSpace(f.Folder))
                    .Select(f => f.Folder!).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in folders)
                {
                    try
                    {
                        var list = await client.ListSkylineDocumentsAsync(folder).ConfigureAwait(true);
                        documents[folder] = list.Select(d => new
                        {
                            name = d.Name, replicates = d.Replicates, peptides = d.Peptides, proteins = d.Proteins,
                            uploaded = d.Uploaded?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        }).ToList();
                    }
                    catch (PanoramaException ex)
                    {
                        log.LogInformation("No Skyline documents read from {Folder}: {Problem}", folder, ex.Message);
                    }
                }

                documentsFile = Path.Combine(Path.GetTempPath(), $"chargestate-wiki-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(documentsFile, JsonSerializer.Serialize(documents)).ConfigureAwait(true);
            }

            return await engine.WikiAsync(project.Project, documentsFile).ConfigureAwait(true);
        }
        finally
        {
            if (documentsFile is not null)
            {
                try
                {
                    File.Delete(documentsFile);
                }
                catch (IOException)
                {
                    // A temporary file; Windows cleans the folder eventually.
                }
            }
        }
    }

    /// <summary>
    /// Republishes the project's page in the background after a change, when it is ChargeState's
    /// and its written parts are unchanged. Changes in quick succession are published once, at the end.
    /// </summary>
    public void UpdateInBackground(ProjectSummary project)
    {
        if (project.Wiki is null)
        {
            return;
        }

        if (_queued.ContainsKey(project.Project))
        {
            _queued[project.Project] = project;  // running: publish this version when it finishes
            return;
        }

        _queued[project.Project] = null;
        _ = RunAsync(project);
    }

    private async Task RunAsync(ProjectSummary project)
    {
        try
        {
            for (var next = project; next is not null; next = _queued[project.Project])
            {
                _queued[project.Project] = null;
                work.Notice = await UpdateAsync(next).ConfigureAwait(true);
            }
        }
        finally
        {
            _queued.Remove(project.Project);
        }
    }

    /// <summary>One background update; returns what to tell the person, or null for nothing.</summary>
    internal async Task<string?> UpdateAsync(ProjectSummary project)
    {
        var wiki = project.Wiki!;
        try
        {
            using var client = await SignInQuietlyAsync().ConfigureAwait(true);
            if (client is null)
            {
                return $"The wiki page for {project.Project} was not updated: no Panorama sign-in works on this computer (open Wiki page to sign in).";
            }

            var existing = await client.GetWikiPageAsync(wiki.Folder, wiki.PageName).ConfigureAwait(true);
            if (existing is not { IsChargeState: true })
            {
                log.LogInformation("Wiki page {Page} in {Folder} is not ChargeState's yet; not updated on its own.", wiki.PageName, wiki.Folder);
                return null;
            }

            if (existing.EditedOnPanorama)
            {
                return $"The wiki page for {project.Project} was edited on Panorama since the app published it, so it was left alone: "
                    + "open Wiki page to look and publish.";
            }

            var content = await BuildAsync(project, client).ConfigureAwait(true);
            if (existing.WrittenHash != content.WrittenHash)
            {
                return $"The text of the wiki page for {project.Project} has changed: open Wiki page to review and publish it.";
            }

            if (string.Equals(existing.Body.Trim(), content.Html.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            await client.SaveWikiPageAsync(wiki.Folder, wiki.PageName, content.Title, content.Html, existing).ConfigureAwait(true);
            log.LogInformation("Updated wiki page {Page} in {Folder}.", wiki.PageName, wiki.Folder);
            return $"Wiki page for {project.Project} updated {DateTime.Now.ToString("t", CultureInfo.CurrentCulture)}";
        }
        catch (Exception ex) when (ex is PanoramaException or EngineException or ToolMissingException or IOException)
        {
            log.LogWarning(ex, "Could not update the wiki page for {Project}.", project.Project);
            return $"The wiki page for {project.Project} was not updated: {ex.Message}";
        }
    }
}
