using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LabOps.Core.Infrastructure;

/// <summary>
/// A path as Windows resolves it: through junctions, symbolic links and subst drives to the file or
/// folder itself, and from a mapped drive to its network path (\\server\share\...). Python's
/// Path.resolve() gives the same, so a path an engine wrote can be compared with one here.
/// </summary>
public static class FinalPath
{
    private const uint ReadAttributes = 0x80;

    // FILE_FLAG_BACKUP_SEMANTICS, without which a folder cannot be opened.
    private const uint BackupSemantics = 0x02000000;

    /// <summary>The final path of an existing file or folder; null when it cannot be opened, or off Windows.</summary>
    public static string? Of(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var handle = CreateFileW(path, ReadAttributes, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[512];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length > buffer.Length)
        {
            // Too small: the length returned is the one needed.
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        }

        if (length == 0 || length > buffer.Length)
        {
            return null;
        }

        var final = new string(buffer, 0, (int)length);
        return final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + final[8..]
            : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..]
            : final;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes, FileMode creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] filePath, uint length, uint flags);
}
