namespace ChargeState.Core.Panorama;

/// <summary>
/// The Panorama sign-ins to try: PanoramaBridge's if it saved one on this computer (an API key
/// or a user name and password), then the one saved by ChargeState. Saving and forgetting touch
/// only ChargeState's own entry; PanoramaBridge's belongs to PanoramaBridge.
/// </summary>
/// <remarks>
/// ChargeState saves its own only when there was no usable PanoramaBridge sign-in, so trying
/// PanoramaBridge's first follows a key PanoramaBridge renews, and ChargeState's still works
/// when PanoramaBridge's has expired.
/// </remarks>
public sealed class PanoramaSignIn(ICredentialStore store, Uri server)
{
    public Uri Server { get; } = server;

    /// <summary>"https://panoramaweb.org": scheme and host, which is how both apps name entries.</summary>
    public string Host => $"{Server.Scheme}://{Server.Host}";

    /// <summary>PanoramaBridge's entry for this server (WindowsCredentialStore.TargetFor there).</summary>
    public string PanoramaBridgeTarget => $"PanoramaBridge:{Host}";

    public string OwnTarget => $"ChargeState:{Host}";

    /// <summary>The saved sign-ins, in the order to try them.</summary>
    public IReadOnlyList<PanoramaCredential> Candidates() =>
    [
        .. new[]
        {
            PanoramaCredential.FromStored(Read(PanoramaBridgeTarget), "PanoramaBridge"),
            PanoramaCredential.FromStored(Read(OwnTarget), "ChargeState"),
        }.OfType<PanoramaCredential>(),
    ];

    /// <summary>Saves a sign-in typed into ChargeState, for next time.</summary>
    public PanoramaCredential Save(PanoramaCredential credential)
    {
        store.Write(OwnTarget, credential.ToStored(), "Panorama sign-in saved by ChargeState");
        return PanoramaCredential.FromStored(credential.ToStored(), "ChargeState")!;
    }

    /// <summary>Forgets ChargeState's own sign-in (never PanoramaBridge's).</summary>
    public void Forget() => store.Delete(OwnTarget);

    // An entry that cannot be read is treated as missing: the person is asked to sign in instead.
    private StoredCredential? Read(string target)
    {
        try
        {
            return store.Read(target);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
