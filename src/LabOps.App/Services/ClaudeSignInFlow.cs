using System.Windows;
using Microsoft.Extensions.Logging;
using LabOps.Core.Claude;
using LabOps.Core.Infrastructure;
using LabOps.Core.Setup;

namespace LabOps.App.Services;

/// <summary>
/// Signs in to Claude the way Setup and the chat both do: in the browser, with no console window
/// (<see cref="ClaudeLogin"/>), and only if that does not finish, the console window Setup used to
/// open, where Claude Code can ask for a code to paste.
/// </summary>
public sealed class ClaudeSignInFlow(ClaudeLogin login, SetupService setup, ILogger<ClaudeSignInFlow> log)
{
    /// <param name="onLink">Called with the sign-in page's address, to show in case the browser did not open.</param>
    /// <param name="onConsole">Called with what to do in the console window, just before it opens.</param>
    /// <param name="cancellationToken">Stops waiting for the browser.</param>
    /// <returns>True when signed in: in the browser, or in the console window, which the person closes once it says so.</returns>
    public async Task<bool> SignInAsync(Action<string>? onLink, Action<string>? onConsole, CancellationToken cancellationToken)
    {
        var result = await login.SignInAsync(onLink, cancellationToken).ConfigureAwait(true);
        if (result.Succeeded || result.Cancelled)
        {
            return result.Succeeded;
        }

        log.LogWarning("claude auth login did not finish: {Output}", result.Output);
        if (MessageBox.Show(
                "The sign-in did not finish. Sign in with a console window instead? It shows what Claude asks for, and you close "
                + "it when it says you are signed in.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes
            || setup.ConsoleFix(SetupStep.ClaudeSignIn) is not { } command)
        {
            return false;
        }

        onConsole?.Invoke(command.Explanation);
        await Shell.RunInConsoleAsync(command).ConfigureAwait(true);
        return true;
    }
}
