using System.Net.Http.Headers;
using System.Text;

namespace ChargeState.Core.Panorama;

/// <summary>
/// How the app signs in to Panorama: an API key, or a Panorama user name and password. Both go
/// as HTTP Basic, the same as PanoramaBridge (an API key is the pair "apikey" and the key).
/// </summary>
public sealed class PanoramaCredential
{
    /// <summary>The user name Panorama expects with an API key.</summary>
    public const string ApiKeyUserName = "apikey";

    // Older LabKey keys were handed out with this prefix; people paste what they were given.
    private const string LegacyPrefix = "apikey|";

    private PanoramaCredential(string userName, string secret, string source)
    {
        UserName = userName;
        Secret = secret;
        Source = source;
    }

    public string UserName { get; }

    /// <summary>Never logged and never shown; it lives only in Windows Credential Manager.</summary>
    public string Secret { get; }

    /// <summary>Which app saved it: "PanoramaBridge", "ChargeState", or "typed" before it is saved.</summary>
    public string Source { get; }

    public bool IsApiKey => string.Equals(UserName, ApiKeyUserName, StringComparison.OrdinalIgnoreCase);

    public static PanoramaCredential ApiKey(string key, string source = "typed")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var trimmed = key.Trim();
        if (trimmed.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[LegacyPrefix.Length..];
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(trimmed, nameof(key));
        return new PanoramaCredential(ApiKeyUserName, trimmed, source);
    }

    public static PanoramaCredential Login(string userName, string password, string source = "typed")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrEmpty(password);
        return new PanoramaCredential(userName.Trim(), password, source);
    }

    /// <summary>From what a credential store holds: user name "apikey" means an API key.</summary>
    public static PanoramaCredential? FromStored(StoredCredential? stored, string source) =>
        stored is not { } s || string.IsNullOrWhiteSpace(s.UserName) || string.IsNullOrEmpty(s.Secret)
            ? null
            : string.Equals(s.UserName, ApiKeyUserName, StringComparison.OrdinalIgnoreCase)
                ? ApiKey(s.Secret, source)
                : Login(s.UserName, s.Secret, source);

    /// <summary>Sent with every request rather than after a challenge, so a bad key fails at once.</summary>
    public AuthenticationHeaderValue ToAuthenticationHeader() =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Secret}")));

    public StoredCredential ToStored() => new(UserName, Secret);

    /// <summary>Describes it without the secret. Safe to log and to show.</summary>
    public override string ToString() => IsApiKey ? "an API key" : $"user {UserName}";
}
