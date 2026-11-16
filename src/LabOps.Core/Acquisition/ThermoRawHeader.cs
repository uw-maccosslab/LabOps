using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenMcdf;

namespace LabOps.Core.Acquisition;

/// <summary>A raw file LabOps cannot read; the message is written for the person using the app.</summary>
public sealed class RawFormatException(string message) : Exception(message);

/// <summary>A controller (the mass spectrometer, an LC pump, the autosampler) and its run header's address.</summary>
/// <param name="Type">0 for the mass spectrometer; other numbers for the other devices.</param>
public sealed record RawController(int Type, long RunHeader)
{
    public bool IsMassSpectrometer => Type == 0;
}

/// <summary>Where the parts of a raw file are, from its first bytes.</summary>
/// <param name="Acquired">When the acquisition started, by the instrument computer's clock (Thermo records no time zone).</param>
/// <param name="MethodOffset">Where the embedded instrument method starts, or -1 when the file has none.</param>
/// <param name="DataAddress">Where the spectra start; the embedded method ends before it.</param>
public sealed record RawLayout(int Version, DateTime Acquired, int MethodOffset, long DataAddress, IReadOnlyList<RawController> Controllers, long FileLength)
{
    public RawController? MassSpectrometer => Controllers.FirstOrDefault(c => c.IsMassSpectrometer);
}

/// <summary>One device's part of the embedded method, as its method editor summarized it.</summary>
public sealed record RawMethodText(string Device, string Text);

/// <summary>The mass spectrometer as its run header names it.</summary>
public sealed record RawInstrument(string? Name, string? Model, string? Serial, string? Software);

/// <summary>
/// Reads what a Thermo .raw file says about how it was acquired from two small parts of it: its
/// start (the file header, the controller table and the embedded instrument method, under about a
/// megabyte) and the mass spectrometer's instrument block just after its run header. LabOps reads
/// those parts over HTTP from Panorama (PanoramaRawReader) instead of downloading files of several
/// gigabytes, and Thermo's RawFileReader cannot open a file from them: it reads the first 10 MiB
/// and every index when it opens one.
/// </summary>
/// <remarks>
/// Thermo does not document the format. The layout here was worked out on format version 66
/// files from a Q Exactive, Q Exactive HF, Exploris 480, Stellar, Orbitrap Astral and Astral
/// Zoom (2020 to 2026), checked against RawMethodCheck (ProteoWizard and RawFileReader): the
/// method text is byte for byte what RawFileReader returns, and the acquisition time and
/// instrument are what it reports. Other versions are refused rather than guessed at, since
/// versions before 64 have 32-bit addresses laid out differently.
/// </remarks>
public static class ThermoRawHeader
{
    /// <summary>The format version this reader knows.</summary>
    public const int SupportedVersion = 66;

    /// <summary>How much of the start to read first: the header and controller table are well inside it.</summary>
    public const int StartLength = 64 * 1024;

    /// <summary>The run header's fixed part; the instrument block follows it.</summary>
    public const int InstrumentOffset = 7588;

    /// <summary>How much of the instrument block to read: its strings come first and are short.</summary>
    public const int InstrumentLength = 4096;

    /// <summary>
    /// The most of the start LabOps reads for the method. Methods seen are 30 KB to 1.1 MB; a
    /// larger claim means the header was misread, and reading on would fetch spectra.
    /// </summary>
    public const long MaxMethodEnd = 32L * 1024 * 1024;

    /// <summary>The file header's fixed size; the sequence row and file information follow it.</summary>
    private const int FileHeaderLength = 1356;

    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>The header, the acquisition time and the controller table, from the file's first bytes.</summary>
    /// <param name="start">At least the first <see cref="StartLength"/> bytes, or the whole file when it is shorter.</param>
    public static RawLayout ReadLayout(ReadOnlySpan<byte> start, long fileLength)
    {
        if (start.Length < FileHeaderLength
            || BinaryPrimitives.ReadUInt16LittleEndian(start) != 0xA101
            || Encoding.Unicode.GetString(start.Slice(2, 16)) != "Finnigan")
        {
            throw new RawFormatException("is not a Thermo raw file");
        }

        var version = (int)BinaryPrimitives.ReadUInt32LittleEndian(start[36..]);
        if (version != SupportedVersion)
        {
            throw new RawFormatException($"is in Thermo raw format version {version}, which LabOps cannot read yet (it reads version {SupportedVersion})");
        }

        // The header's first audit time is when the acquisition started, as a FILETIME holding the
        // instrument computer's local time.
        var acquired = DateTime.SpecifyKind(DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(start[40..])), DateTimeKind.Unspecified);
        var methodOffset = start.IndexOf(CompoundFileSignature);
        var tableEnd = methodOffset > 0 ? methodOffset : start.Length;

        // The controller table: 16-byte entries (type, index, run header address), numbered from
        // 0, right after the address where the spectra start. Its position depends on the variable
        // length text before it, so it is found by its shape. The spectra's address lies after the
        // embedded method and before every run header, which rules out chance matches.
        (long Data, List<RawController> Controllers)? best = null;
        for (var at = FileHeaderLength + 8; at + 16 <= tableEnd; at++)
        {
            var controllers = new List<RawController>();
            for (var entry = at; entry + 16 <= start.Length; entry += 16)
            {
                var type = BinaryPrimitives.ReadUInt32LittleEndian(start[entry..]);
                var index = BinaryPrimitives.ReadUInt32LittleEndian(start[(entry + 4)..]);
                var address = BinaryPrimitives.ReadInt64LittleEndian(start[(entry + 8)..]);
                if (index != controllers.Count || type > 10 || address <= 0 || address >= fileLength)
                {
                    break;
                }

                controllers.Add(new RawController((int)type, address));
            }

            if (controllers.Count == 0)
            {
                continue;
            }

            var data = BinaryPrimitives.ReadInt64LittleEndian(start[(at - 8)..]);
            if (data > Math.Max(methodOffset, 0) && data < controllers.Min(c => c.RunHeader)
                && (best is null || controllers.Count >= best.Value.Controllers.Count))
            {
                best = (data, controllers);
            }
        }

        if (best is not { } found || !found.Controllers.Any(c => c.IsMassSpectrometer))
        {
            throw new RawFormatException("has a header LabOps could not read (no mass spectrometer in its controller table)");
        }

        return new RawLayout(version, acquired, methodOffset, found.Data, found.Controllers, fileLength);
    }

    /// <summary>How many bytes from the start hold the method: everything before the spectra.</summary>
    public static long MethodEnd(RawLayout layout) =>
        layout.DataAddress <= MaxMethodEnd
            ? layout.DataAddress
            : throw new RawFormatException($"claims a method of {layout.DataAddress:N0} bytes, which is not plausible");

    /// <summary>
    /// The embedded method's text, one entry per device (the LC, the mass spectrometer), in the
    /// order the method stores them; empty when the file has no embedded method.
    /// </summary>
    /// <param name="start">The file from its first byte to at least <see cref="MethodEnd"/>.</param>
    public static IReadOnlyList<RawMethodText> ReadMethods(byte[] start, RawLayout layout)
    {
        if (layout.MethodOffset < 0)
        {
            return [];
        }

        var end = (int)Math.Min(MethodEnd(layout), start.Length);
        return ReadCompoundMethods(start, layout.MethodOffset, end - layout.MethodOffset);
    }

    /// <summary>
    /// The embedded method as a file of its own: the compound file between the header and the
    /// spectra, which is the instrument method (.meth) the file was acquired with. Null when the
    /// file has none.
    /// </summary>
    /// <param name="start">The file from its first byte to at least <see cref="MethodEnd"/>.</param>
    public static byte[]? ExtractMethod(byte[] start, RawLayout layout) =>
        layout.MethodOffset < 0 ? null : start[layout.MethodOffset..(int)Math.Min(MethodEnd(layout), start.Length)];

    /// <summary>
    /// Each device's method text in an instrument method file (.meth), as <see cref="ReadMethods"/>
    /// reads them from a raw file, so the two fingerprint alike.
    /// </summary>
    public static IReadOnlyList<RawMethodText> ReadMethodFileText(byte[] method) => ReadCompoundMethods(method, 0, method.Length);

    private static List<RawMethodText> ReadCompoundMethods(byte[] bytes, int offset, int count)
    {
        try
        {
            using var stream = new MemoryStream(bytes, offset, count, writable: false);
            using var root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
            var methods = new List<RawMethodText>();
            foreach (var entry in root.EnumerateEntries().Where(e => e.Type == EntryType.Storage))
            {
                var device = root.OpenStorage(entry.Name);
                if (!device.TryOpenStream("Text", out var text))
                {
                    continue;
                }

                using (text)
                {
                    using var copy = new MemoryStream();
                    text.CopyTo(copy);
                    methods.Add(new RawMethodText(entry.Name, Encoding.Unicode.GetString(copy.ToArray()).TrimEnd('\0')));
                }
            }

            return methods;
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            throw new RawFormatException($"has an embedded method LabOps could not read ({ex.Message})");
        }
    }

    /// <summary>
    /// The instrument method's file name (Actis_DIA_24m.meth), from the sequence information in
    /// the header; null when it names none. Only this field is read: the sequence information also
    /// holds the sample's name and ID, which LabOps does not keep.
    /// </summary>
    public static string? ReadMethodFile(ReadOnlySpan<byte> start, RawLayout layout)
    {
        var end = layout.MethodOffset > 0 ? layout.MethodOffset : start.Length;
        var suffix = Encoding.Unicode.GetBytes(".meth");
        var region = start[..end];
        for (var at = region.IndexOf(suffix); at >= 0;)
        {
            // A string is a 32-bit count of UTF-16 characters, then the characters; find the count
            // that makes this one end right after ".meth".
            var stringEnd = at + suffix.Length;
            for (var length = 5; length <= 260; length++)
            {
                var countAt = stringEnd - (2 * length) - 4;
                if (countAt < FileHeaderLength)
                {
                    break;
                }

                if (BinaryPrimitives.ReadUInt32LittleEndian(region[countAt..]) == length)
                {
                    var path = Encoding.Unicode.GetString(region.Slice(countAt + 4, 2 * length));
                    return path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
                }
            }

            var next = region[(at + 2)..].IndexOf(suffix);
            at = next < 0 ? -1 : at + 2 + next;
        }

        return null;
    }

    /// <summary>The mass spectrometer's name, model, serial number and software, from its instrument block.</summary>
    /// <param name="block">The bytes from the run header's address plus <see cref="InstrumentOffset"/>.</param>
    public static RawInstrument ReadInstrument(ReadOnlySpan<byte> block)
    {
        var strings = new List<string?>();
        var at = 0;
        while (strings.Count < 4 && at + 4 <= block.Length)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(block[at..]);
            if (length > 256 || at + 4 + (2 * length) > block.Length)
            {
                throw new RawFormatException("has an instrument description LabOps could not read");
            }

            var text = Encoding.Unicode.GetString(block.Slice(at + 4, (int)(2 * length))).Trim();
            strings.Add(text.Length > 0 ? text : null);
            at += 4 + (2 * (int)length);
        }

        while (strings.Count < 4)
        {
            strings.Add(null);
        }

        return new RawInstrument(strings[0], strings[1], strings[2], strings[3]);
    }

    /// <summary>
    /// A short fingerprint of the method's text, the same for two files acquired with the same
    /// method. The "Creator: ... Last Modified: ..." line is left out, as RawMethodCheck leaves it
    /// out when it groups files, since saving a method again changes it and nothing else.
    /// </summary>
    public static string Fingerprint(IEnumerable<RawMethodText> methods)
    {
        var text = new StringBuilder();
        foreach (var method in methods.OrderBy(m => m.Device, StringComparer.Ordinal))
        {
            text.Append(method.Device).Append('\n');
            foreach (var line in method.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (!line.TrimStart().StartsWith("Creator:", StringComparison.OrdinalIgnoreCase))
                {
                    text.Append(line.TrimEnd()).Append('\n');
                }
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..12];
    }
}
