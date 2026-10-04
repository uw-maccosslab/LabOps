using System.IO;
using System.Windows;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Services;

/// <summary>
/// The app was called ChargeState until 26.7.0, and was installed under another package, which
/// does not update to LabOps. LabOps takes over its settings on first start (AppPaths.AdoptLegacy)
/// and offers, once, to remove it.
/// </summary>
public static class LegacyApp
{
    /// <summary>Where Velopack installed ChargeState.</summary>
    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.LegacyPackageId);

    public static bool IsInstalled => File.Exists(Path.Combine(InstallFolder, "Update.exe"));

    /// <summary>Asks once whether to remove ChargeState, and opens Windows' Installed apps if so.</summary>
    public static void OfferRemoval(Window owner, AppSettings settings, SettingsStore store)
    {
        if (!IsInstalled || settings.LegacyRemovalOffered)
        {
            return;
        }

        settings.LegacyRemovalOffered = true;
        store.Save(settings);
        if (MessageBox.Show(owner,
                "ChargeState, this app's earlier name, is still installed. LabOps has taken over its settings, its clones and "
                + "its Panorama sign-in, and ChargeState gets no more updates.\n\n"
                + "Close ChargeState if it is open, so the two do not both sync the same folders. Remove it now? Windows' "
                + "Installed apps opens: find ChargeState there and choose Uninstall.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Shell.Open("ms-settings:appsfeatures");
        }
    }
}
