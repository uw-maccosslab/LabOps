using System.Globalization;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using LabOps.Core.Panorama;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;

namespace LabOps.App.Services;

/// <summary>
/// Publishes the lab dashboard (<see cref="DashboardPage"/>) to the Panorama folder that
/// LabOps-Projects' config/app.yaml names, a folder only lab members can read.
/// </summary>
/// <remarks>
/// A person publishes it the first time, from the Overview, after confirming. From then on, after
/// each reload of the projects, the app brings it up to date in the background, but only when
/// this copy has just synced with GitHub (so an out-of-date copy never overwrites newer work),
/// only a page LabOps made, and only while nobody has edited it on Panorama. Every lab member's
/// app does this; one without permission to edit the folder fails quietly.
/// </remarks>
public sealed class DashboardPublisher(PanoramaSignIn signIn, WorkTracker work, ILogger<DashboardPublisher> log)
{
    private (ProjectList List, WikiLocation Where)? _next;
    private bool _running;
    private bool _toldEdited;

    /// <summary>Stands in for Panorama in tests.</summary>
    internal HttpMessageHandler? Handler { get; init; }

    /// <summary>Today in Seattle, the date the page is drawn for.</summary>
    internal Func<DateOnly> Today { get; init; } = LabOps.Engines.Projects.ProjectRepository.LabToday;

    /// <summary>Panorama's address for the dashboard page.</summary>
    public string PageUrl(WikiLocation where) =>
        $"{signIn.Server.GetLeftPart(UriPartial.Authority)}{PanoramaPaths.Encode(PanoramaPaths.AsFolder(where.Folder))}wiki-page.view?name="
        + Uri.EscapeDataString(where.PageName);

    /// <summary>A person publishes the dashboard (the first time, or to take back an edited page). Returns what to tell them.</summary>
    public async Task<string> PublishAsync(ProjectList list, WikiLocation where)
    {
        try
        {
            using var client = await SignInQuietlyAsync().ConfigureAwait(true);
            if (client is null)
            {
                return "No Panorama sign-in works on this computer. Sign in to Panorama in LabOps (a project's Wiki page asks), "
                       + "then publish again.";
            }

            var existing = await client.GetWikiPageAsync(where.Folder, where.PageName).ConfigureAwait(true);
            var page = DashboardPage.Build(list, Today());
            switch (DashboardPage.Decide(existing?.Body, page, byPerson: true))
            {
                case DashboardDecision.NotOurs:
                    return $"The page {where.PageName} in {where.Folder} on Panorama was not made by LabOps, so it was left alone. "
                           + "Remove it on Panorama, or name another page for the dashboard in LabOps-Projects' config/app.yaml.";
                case DashboardDecision.Unchanged:
                    return "The lab dashboard on Panorama is already up to date.";
            }

            await client.SaveWikiPageAsync(where.Folder, where.PageName, DashboardPage.Title, page, existing).ConfigureAwait(true);
            _toldEdited = false;
            log.LogInformation("Published the lab dashboard to {Folder} ({Page}).", where.Folder, where.PageName);
            return $"Lab dashboard published to {where.Folder} on Panorama. LabOps keeps it up to date from now on.";
        }
        catch (Exception ex) when (ex is PanoramaException or IOException or HttpRequestException)
        {
            log.LogWarning(ex, "Could not publish the lab dashboard to {Folder}.", where.Folder);
            return $"The lab dashboard was not published: {ex.Message}";
        }
    }

    /// <summary>
    /// Brings the dashboard up to date after the projects were reloaded, when there is one to keep
    /// and this copy has just synced. Reloads in quick succession publish once, with the latest.
    /// </summary>
    /// <param name="synced">This copy synced with GitHub moments ago, so it has everyone's work.</param>
    public void UpdateInBackground(ProjectList list, WikiLocation? where, bool synced)
    {
        if (where is null || !synced)
        {
            return;
        }

        _next = (list, where);
        if (!_running)
        {
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            while (_next is { } next)
            {
                _next = null;
                if (await UpdateAsync(next.List, next.Where).ConfigureAwait(true) is { } notice)
                {
                    work.Notice = notice;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>One background update; returns what to tell the person, or null for nothing.</summary>
    internal async Task<string?> UpdateAsync(ProjectList list, WikiLocation where)
    {
        try
        {
            using var client = await SignInQuietlyAsync().ConfigureAwait(true);
            if (client is null)
            {
                return null;
            }

            var existing = await client.GetWikiPageAsync(where.Folder, where.PageName).ConfigureAwait(true);
            var page = DashboardPage.Build(list, Today());
            switch (DashboardPage.Decide(existing?.Body, page, byPerson: false))
            {
                case DashboardDecision.Publish:
                    await client.SaveWikiPageAsync(where.Folder, where.PageName, DashboardPage.Title, page, existing).ConfigureAwait(true);
                    log.LogInformation("Updated the lab dashboard in {Folder} at {Time}.", where.Folder,
                        DateTime.Now.ToString("t", CultureInfo.InvariantCulture));
                    return null;
                case DashboardDecision.Edited when !_toldEdited:
                    // Said once a session: it stays true until someone publishes again.
                    _toldEdited = true;
                    return "The lab dashboard was edited on Panorama, so LabOps stopped updating it. "
                           + "Publish it from the Overview to take it back.";
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is PanoramaException or IOException or HttpRequestException)
        {
            // Most often a lab member without permission to edit the folder: nothing to tell them.
            log.LogInformation("The lab dashboard was not updated: {Problem}", ex.Message);
            return null;
        }
    }

    /// <summary>A client with a saved sign-in Panorama accepts, without asking anyone; null when none works.</summary>
    private async Task<PanoramaClient?> SignInQuietlyAsync()
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
}
