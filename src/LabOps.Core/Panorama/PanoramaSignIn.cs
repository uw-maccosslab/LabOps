namespace LabOps.Core.Panorama;

/// <summary>
/// The Panorama sign-ins to try: PanoramaBridge's if it saved one on this computer (an API key
/// or a user name and password), then the one saved by LabOps. Saving and forgetting touch
/// only LabOps's own entry; PanoramaBridge's belongs to PanoramaBridge.
/// </summary>
/// <remarks>
/// LabOps saves its own only when there was no usable PanoramaBridge sign-in, so trying
/// PanoramaBridge's first follows a key PanoramaBridge renews, and LabOps's still works
/// when PanoramaBridge's has expired.
/// </remarks>
public sealed class PanoramaSignIn(ICredentialStore store, Uri server)
{
    public Uri Server { get; } = server;

    /// <summary>"https://panoramaweb.org": scheme and host, which is how both apps name entries.</summary>
    public string Host => $"{Server.Scheme}://{Server.Host}";

    /// <summary>PanoramaBridge's entry for this server (WindowsCredentialStore.TargetFor there).</summary>
    public string PanoramaBridgeTarget => $"PanoramaBridge:{Host}";

    public string OwnTarget => $"LabOps:{Host}";

    /// <summary>The entry saved by the app under its earlier name, ChargeState; read, never written.</summary>
    public string LegacyTarget => $"ChargeState:{Host}";

    /// <summary>The saved sign-ins, in the order to try them.</summary>
    public IReadOnlyList<PanoramaCredential> Candidates() =>
    [
        .. new[]
        {
            PanoramaCredential.FromStored(Read(PanoramaBridgeTarget), "PanoramaBridge"),
            PanoramaCredential.FromStored(Read(OwnTarget), "LabOps"),
            PanoramaCredential.FromStored(Read(LegacyTarget), "ChargeState"),
        }.OfType<PanoramaCredential>(),
    ];

    /// <summary>Saves a sign-in typed into LabOps, for next time.</summary>
    public PanoramaCredential Save(PanoramaCredential credential)
    {
        store.Write(OwnTarget, credential.ToStored(), "Panorama sign-in saved by LabOps");
        return PanoramaCredential.FromStored(credential.ToStored(), "LabOps")!;
    }

    /// <summary>Forgets LabOps's own sign-in (never PanoramaBridge's).</summary>
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
