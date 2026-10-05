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

    [Fact]
    public async Task Try_again_is_offered_once_the_sign_in_window_has_closed()
    {
        var signedIn = 0;
        var retried = 0;
        var item = new SignInItem(() => { signedIn++; return Task.CompletedTask; }, () => { retried++; return Task.CompletedTask; });

        item.TryAgainCommand.CanExecute(null).ShouldBeFalse();
        await item.SignInCommand.ExecuteAsync(null);
        signedIn.ShouldBe(1);
        item.SignedIn.ShouldBeTrue();
        item.Text.ShouldContain("Try again");

        item.TryAgainCommand.CanExecute(null).ShouldBeTrue();
        await item.TryAgainCommand.ExecuteAsync(null);
        retried.ShouldBe(1);
    }
}
