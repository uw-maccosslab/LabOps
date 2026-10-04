using System.Diagnostics;
using System.Windows;
using LabOps.Core.Infrastructure;
using LabOps.Core.Setup;

namespace LabOps.App.Services;

/// <summary>Opening files, folders and links, and running setup commands in a visible console.</summary>
public static class Shell
{
    public static void Open(string pathOrUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            MessageBox.Show($"Could not open {pathOrUrl}.\n\n{ex.Message}", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Shows a file selected in Explorer.</summary>
    public static void Reveal(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path } })?.Dispose();
        }
        else
        {
            Open(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
        }
    }

    /// <summary>
    /// Runs a setup command in its own console window and waits for the user to close it.
    /// </summary>
    /// <remarks>
    /// A console program started from a windowed app without CreateNoWindow gets a console of
    /// its own, which is what a sign-in that prints a one-time code needs.
    /// </remarks>
    public static async Task RunInConsoleAsync(ConsoleCommand command)
    {
        var info = new ProcessStartInfo(command.FileName) { UseShellExecute = false, CreateNoWindow = false };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info);
        if (process is not null)
        {
            await process.WaitForExitAsync().ConfigureAwait(true);
        }
    }
}
