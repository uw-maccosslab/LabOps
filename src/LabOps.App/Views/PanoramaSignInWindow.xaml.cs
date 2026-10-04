using System.Windows;
using LabOps.App.Services;
using LabOps.Core.Panorama;

namespace LabOps.App.Views;

/// <summary>
/// Asks for a Panorama API key, or a user name and password, when neither PanoramaBridge nor
/// LabOps has a sign-in that works. It is checked against Panorama before it is kept.
/// </summary>
public partial class PanoramaSignInWindow : Window
{
    private readonly Func<PanoramaCredential, Task<string?>> _check;
    private readonly Uri _server;
    private PanoramaCredential? _credential;
    private bool _checking;

    private PanoramaSignInWindow(Window? owner, string message, Uri server, Func<PanoramaCredential, Task<string?>> check)
    {
        InitializeComponent();
        Owner = owner;
        Message.Text = message;
        _server = server;
        _check = check;
        Loaded += (_, _) => ApiKey.Focus();
    }

    /// <summary>A sign-in Panorama accepted, or null if cancelled.</summary>
    /// <param name="check">Tries a sign-in; returns why it failed, or null when Panorama accepted it.</param>
    public static PanoramaCredential? Ask(Window? owner, string message, Uri server, Func<PanoramaCredential, Task<string?>> check)
    {
        var window = new PanoramaSignInWindow(owner, message, server, check);
        return window.ShowDialog() == true ? window._credential : null;
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ApiKeyFields is null || LoginFields is null)
        {
            return;  // during InitializeComponent
        }

        ApiKeyFields.IsEnabled = UseApiKey.IsChecked == true;
        LoginFields.IsEnabled = UseLogin.IsChecked == true;
        (UseApiKey.IsChecked == true ? (UIElement)ApiKey : UserName).Focus();
    }

    private void OnCreateApiKey(object sender, RoutedEventArgs e) =>
        Shell.Open($"{_server.GetLeftPart(UriPartial.Authority)}/login-createApiKey.view");

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        // A click already queued behind one being checked would close the window a second time.
        if (_checking || _credential is not null)
        {
            return;
        }

        PanoramaCredential credential;
        if (UseApiKey.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(ApiKey.Password))
            {
                Show("Paste the API key.");
                return;
            }

            credential = PanoramaCredential.ApiKey(ApiKey.Password);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(UserName.Text) || Password.Password.Length == 0)
            {
                Show("Give the user name and the password.");
                return;
            }

            credential = PanoramaCredential.Login(UserName.Text, Password.Password);
        }

        SignIn.IsEnabled = false;
        _checking = true;
        Show(null);
        try
        {
            if (await _check(credential).ConfigureAwait(true) is { } problem)
            {
                Show(problem);
                return;
            }
        }
        finally
        {
            SignIn.IsEnabled = true;
            _checking = false;
        }

        _credential = credential;
        DialogResult = true;
    }

    private void Show(string? problem)
    {
        Problem.Text = problem ?? "";
        Problem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
