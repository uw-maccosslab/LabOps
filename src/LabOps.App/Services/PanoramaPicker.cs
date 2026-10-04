using System.Windows;
using Microsoft.Extensions.Logging;
using LabOps.App.ViewModels;
using LabOps.App.Views;
using LabOps.Core.Infrastructure;
using LabOps.Core.Panorama;

namespace LabOps.App.Services;

/// <summary>
/// Chooses a folder or an ELN notebook on Panorama, and signs in for the Wiki page window. Signs in
/// with PanoramaBridge's saved sign-in, else LabOps's, and asks for an API key or a user name
/// and password only when neither works.
/// </summary>
public sealed class PanoramaPicker(PanoramaSignIn signIn, ILogger<PanoramaPicker> log)
{
    /// <summary>The folder chosen (/MacCoss/maccoss/@files/2026-BioTRACK), or null.</summary>
    public async Task<string?> ChooseFolderAsync(Window owner)
    {
        using var client = await SignInAsync(owner).ConfigureAwait(true);
        return client is null ? null : PanoramaBrowserWindow.Ask(owner, new PanoramaBrowserViewModel(client)).Folder;
    }

    /// <summary>The notebook chosen, or null.</summary>
    public async Task<PanoramaNotebook?> ChooseNotebookAsync(Window owner)
    {
        using var client = await SignInAsync(owner).ConfigureAwait(true);
        return client is null ? null : NotebookPickerWindow.Ask(owner, new NotebookPickerViewModel(client));
    }

    /// <summary>A client with a sign-in Panorama accepts, or null if the person gave up. The caller disposes it.</summary>
    public async Task<PanoramaClient?> SignInAsync(Window owner)
    {
        string? rejected = null;
        foreach (var candidate in signIn.Candidates())
        {
            var problem = await TryAsync(candidate).ConfigureAwait(true);
            if (problem is null)
            {
                return new PanoramaClient(signIn.Server, candidate);
            }

            if (!problem.IsSignInProblem)
            {
                MessageBox.Show(owner, problem.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            log.LogInformation("Panorama did not accept the sign-in saved by {Source}.", candidate.Source);
            rejected ??= $"Panorama did not accept the sign-in saved by {candidate.Source} ({candidate}); the API key may have expired. ";
        }

        var message = (rejected ?? "Neither PanoramaBridge nor LabOps has a Panorama sign-in saved on this computer. ")
            + "Sign in once to use Panorama from LabOps; it keeps the sign-in for next time.";
        if (PanoramaSignInWindow.Ask(owner, message, signIn.Server,
                async c => (await TryAsync(c).ConfigureAwait(true))?.Message) is not { } typed)
        {
            return null;
        }

        try
        {
            return new PanoramaClient(signIn.Server, signIn.Save(typed));
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Could not save the Panorama sign-in.");
            return new PanoramaClient(signIn.Server, typed);
        }
    }

    /// <summary>Lists Panorama's projects with a sign-in: null when it works, else why not.</summary>
    private async Task<PanoramaException?> TryAsync(PanoramaCredential credential)
    {
        using var client = new PanoramaClient(signIn.Server, credential);
        try
        {
            await client.ListAsync("/_webdav/").ConfigureAwait(true);
            return null;
        }
        catch (PanoramaException ex)
        {
            return ex;
        }
    }
}
