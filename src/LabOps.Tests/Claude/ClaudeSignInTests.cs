using LabOps.App.ViewModels;
using LabOps.Core.Claude;

namespace LabOps.Tests.Claude;

/// <summary>An expired Claude sign-in is recognized in the chat, which offers to sign in again.</summary>
public sealed class ClaudeSignInTests
{
    [Theory]
    [InlineData("Failed to authenticate: OAuth session expired and could not be refreshed")]
    [InlineData("""API Error: 401 {"type":"error","error":{"type":"authentication_error","message":"OAuth token has expired."}}""")]
    [InlineData("Invalid API key · Please run /login")]
    [InlineData("Not logged in · Please run /login")]
    public void Claude_code_s_words_for_a_failed_sign_in_are_recognized(string text) =>
        ClaudeSignIn.IsProblem(text).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Saved and shared.")]
    [InlineData("Panorama did not accept the sign-in; give a new API key.")]
    [InlineData("Claude stopped unexpectedly (exit code 1).")]
    public void Other_messages_are_not(string? text) => ClaudeSignIn.IsProblem(text).ShouldBeFalse();

    [Theory]
    [InlineData("Failed to authenticate: OAuth session expired and could not be refreshed")]
    [InlineData("""API Error: 401 {"type":"error","error":{"type":"authentication_error","message":"OAuth token has expired."}}""")]
    [InlineData("  Invalid API key · Please run /login")]
    [InlineData("Not logged in · Please run /login")]
    public void Claude_code_s_own_error_shown_as_a_reply_offers_a_sign_in(string text) =>
        ClaudeSignIn.IsProblemReply(text).ShouldBeTrue();

    [Theory]
    [InlineData("gh auth status says: You are not logged into any GitHub hosts. Run gh auth login.")]
    [InlineData("Panorama failed to authenticate with the API key in the settings, so the folder was not read.")]
    [InlineData("The script printed \"Invalid API key\" for the Panorama key; give a new one.")]
    [InlineData("API Error: 529 overloaded")]
    public void A_reply_that_only_mentions_a_sign_in_does_not(string text) =>
        ClaudeSignIn.IsProblemReply(text).ShouldBeFalse();

    [Theory]
    [InlineData("Opening browser to sign in…\nIf the browser didn't open, visit: https://claude.ai/oauth\nPaste code here if prompted > Login successful.\n", true)]
    [InlineData("Opening browser to sign in…\nPaste code here if prompted > ", false)]
    [InlineData("", false)]
    public void Only_claude_code_saying_so_counts_as_signed_in(string output, bool signedIn) =>
        ClaudeLogin.SaysSignedIn(output).ShouldBe(signedIn);

    [Fact]
    public async Task Try_again_is_offered_once_signed_in()
    {
        var attempts = 0;
        var retried = 0;
        var works = false;
        var item = new SignInItem((i, _) =>
        {
            attempts++;
            i.Link = "https://claude.ai/oauth/authorize?code=true";
            i.HasLink.ShouldBeTrue();  // shown while waiting, in case the browser did not open
            return Task.FromResult(works);
        }, () => { retried++; return Task.CompletedTask; });

        item.TryAgainCommand.CanExecute(null).ShouldBeFalse();
        await item.SignInCommand.ExecuteAsync(null);
        item.SignedIn.ShouldBeFalse();
        item.Text.ShouldContain("did not finish");
        item.TryAgainCommand.CanExecute(null).ShouldBeFalse();
        item.HasLink.ShouldBeFalse();

        works = true;
        await item.SignInCommand.ExecuteAsync(null);
        attempts.ShouldBe(2);
        item.SignedIn.ShouldBeTrue();
        item.Text.ShouldBe("Signed in. Choose Try again.");
        item.TryAgainCommand.CanExecute(null).ShouldBeTrue();
        await item.TryAgainCommand.ExecuteAsync(null);
        retried.ShouldBe(1);
    }

    [Theory]
    [InlineData("Opening browser to sign in...", null)]
    [InlineData("If the browser didn't open, visit: https://claude.ai/oauth/authorize?code=true&client_id=abc.",
        "https://claude.ai/oauth/authorize?code=true&client_id=abc")]
    [InlineData("Visit https://example.org/phish to continue", null)]
    public void The_sign_in_page_is_found_in_what_claude_prints(string line, string? link) =>
        ClaudeLogin.LinkIn(line).ShouldBe(link);
}
