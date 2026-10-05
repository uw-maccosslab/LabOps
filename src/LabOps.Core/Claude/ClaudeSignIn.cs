namespace LabOps.Core.Claude;

/// <summary>
/// Recognizes Claude Code's own words for a sign-in that no longer works, so the chat can offer to
/// sign in again instead of only showing the error.
/// </summary>
/// <remarks>
/// <c>claude auth status</c> still says "logged in" when the saved session has expired and cannot
/// be refreshed, so Setup cannot see this; only a failed request shows it, as
/// "Failed to authenticate: OAuth session expired and could not be refreshed", an API error naming
/// <c>authentication_error</c>, or "Invalid API key · Please run /login". Claude's replies can
/// contain the same words (relaying <c>gh auth status</c>, or a Panorama sign-in that failed), so
/// a reply counts only when it is Claude Code's own error message (<see cref="IsProblemReply"/>).
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

    // How Claude Code's own error messages begin, when it reports a failed request as a reply.
    private static readonly string[] ErrorStarts =
    [
        "api error",
        "failed to authenticate",
        "invalid api key",
        "oauth token",
        "oauth session",
        "not logged in",
        "please run /login",
    ];

    /// <summary>True when <paramref name="text"/>, an error Claude Code reported, says its sign-in failed.</summary>
    public static bool IsProblem(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Phrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when <paramref name="text"/>, shown as Claude's reply, is Claude Code's own message that
    /// its sign-in failed, rather than a reply that mentions a sign-in.
    /// </summary>
    public static bool IsProblemReply(string? text) =>
        IsProblem(text) && ErrorStarts.Any(s => text!.TrimStart().StartsWith(s, StringComparison.OrdinalIgnoreCase));
}
