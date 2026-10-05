namespace LabOps.Core.Claude;

/// <summary>
/// Recognizes Claude Code's own words for a sign-in that no longer works, so the chat can offer to
/// sign in again instead of only showing the error.
/// </summary>
/// <remarks>
/// <c>claude auth status</c> still says "logged in" when the saved session has expired and cannot
/// be refreshed, so Setup cannot see this; only a failed request shows it, as
/// "Failed to authenticate: OAuth session expired and could not be refreshed", an API error naming
/// <c>authentication_error</c>, or "Invalid API key · Please run /login". The phrases are Claude
/// Code's, specific enough that a reply about a lab's work does not contain them.
/// </remarks>
public static class ClaudeSignIn
{
    private static readonly string[] Phrases =
    [
        "failed to authenticate",
        "oauth session expired",
        "oauth token has expired",
        "oauth token expired",
        "authentication_error",
        "invalid api key",
        "please run /login",
        "not logged in",
    ];

    /// <summary>True when <paramref name="text"/> says Claude Code's sign-in failed.</summary>
    public static bool IsProblem(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Phrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
}
