using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using LabOps.Core.Acquisition;
using LabOps.Core.Panorama;
using LabOps.Core.Projects;
using OpenMcdf;

namespace LabOps.Tests.Acquisition;

/// <summary>
/// Reading a Thermo raw file's acquisition from two small parts of it (ThermoRawHeader), over
/// Panorama's range requests (PanoramaRawReader). The files here are made up, laid out as the
/// real ones were found to be; LAB_RAW_FILES=&lt;folder of .raw files&gt; also reads real ones.
/// </summary>
public sealed class RawFileTests
{
    private static readonly Uri Server = new("https://panoramaweb.org");

    private const string LcText = "SII for Xcalibur\r\nCreator: someone Last Modified: 2026-01-01\r\nGradient: 4.5 to 38 %B\r\n";
    private const string MsText = "Orbitrap Astral Zoom Method Summary\r\nCreator: someone Last Modified: 2026-01-01\r\nMethod Duration (min) = 24\r\n";

    /// <summary>
    /// A raw file of <paramref name="length"/> bytes: the file header (signature, version 66,
    /// acquisition time), a sequence row naming the method file, the controller table, the
    /// embedded method (a compound file), filler for the spectra, and an LC pump's and the mass
    /// spectrometer's run headers with the instrument block 7,588 bytes into the latter.
    /// </summary>
    internal static byte[] MakeRawFile(DateTime acquired, string lcText = LcText, string msText = MsText, int version = 66,
        string serial = "OA10308", long length = 400_000, int methodAt = 4096)
    {
        var file = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(file, 0xA101);
        Encoding.Unicode.GetBytes("Finnigan").CopyTo(file, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(36), (uint)version);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(40), acquired.Ticks - new DateTime(1601, 1, 1).Ticks);

        // The sequence row: the sample's name (which LabOps never reads), then the method's path.
        var at = WriteString(file, 1500, "Sample 12, patient 0042");
        WriteString(file, at + 20, @"C:\Methods\Zoom\Astral_DIA_24m.meth");

        var method = Method(lcText, msText);
        method.CopyTo(file, methodAt);
        long dataAt = methodAt + method.Length;
        long pumpHeader = length - 30_000;
        long msHeader = length - 20_000;

        const int table = 3000;
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(table - 8), dataAt);
        WriteController(file, table, type: 2, index: 0, pumpHeader);
        WriteController(file, table + 16, type: 0, index: 1, msHeader);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan((int)pumpHeader + 7416), pumpHeader - 5000);

        at = WriteString(file, (int)msHeader + ThermoRawHeader.InstrumentOffset, "");
        at = WriteString(file, at, "Orbitrap Astral Zoom");
        at = WriteString(file, at, serial);
        WriteString(file, at, "2.0-2.0.356.0/None");
        return file;

        static void WriteController(byte[] file, int at, uint type, uint index, long runHeader)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), type);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), index);
            BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(at + 8), runHeader);
        }
    }

    private static int WriteString(byte[] file, int at, string text)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), (uint)text.Length);
        Encoding.Unicode.GetBytes(text).CopyTo(file, at + 4);
        return at + 4 + (2 * text.Length);
    }

    /// <summary>The embedded method: a compound file with each device's summary in its Text stream.</summary>
    private static byte[] Method(string lcText, string msText)
    {
        using var memory = new MemoryStream();
        using (var root = RootStorage.Create(memory, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            foreach (var (device, text) in new[] { ("SiiXcalibur", lcText), ("TNG-Merkur", msText) })
            {
                using var stream = root.CreateStorage(device).CreateStream("Text");
                stream.Write(Encoding.Unicode.GetBytes(text));
            }

            root.Flush();
        }

        return memory.ToArray();
    }

    private static (RawLayout Layout, IReadOnlyList<RawMethodText> Methods, string? MethodFile, RawInstrument Instrument) Read(byte[] file)
    {
        var layout = ThermoRawHeader.ReadLayout(file.AsSpan(0, ThermoRawHeader.StartLength), file.Length);
        var block = file.AsSpan((int)(layout.MassSpectrometer!.RunHeader + ThermoRawHeader.InstrumentOffset));
        return (layout, ThermoRawHeader.ReadMethods(file, layout), ThermoRawHeader.ReadMethodFile(file, layout), ThermoRawHeader.ReadInstrument(block));
    }

    // -- the format ---------------------------------------------------------------------------

    [Fact]
    public void The_start_and_the_instrument_block_give_the_time_method_and_instrument()
    {
        var file = MakeRawFile(new DateTime(2026, 9, 22, 18, 34, 37));

        var (layout, methods, methodFile, instrument) = Read(file);

        layout.Version.ShouldBe(66);
        layout.Acquired.ShouldBe(new DateTime(2026, 9, 22, 18, 34, 37));
        layout.Controllers.Select(c => c.Type).ShouldBe([2, 0]);
        layout.MethodOffset.ShouldBe(4096);
        // In the compound file's directory order, not the order written.
        methods.Select(m => m.Device).ShouldBe(["SiiXcalibur", "TNG-Merkur"], ignoreOrder: true);
        methods.Single(m => m.Device == "TNG-Merkur").Text.ShouldBe(MsText);
        methodFile.ShouldBe("Astral_DIA_24m.meth");
        instrument.ShouldBe(new RawInstrument(null, "Orbitrap Astral Zoom", "OA10308", "2.0-2.0.356.0/None"));
    }

    [Fact]
    public void A_method_saved_again_has_the_same_fingerprint_and_a_changed_one_does_not()
    {
        RawMethodText[] method = [new("SiiXcalibur", LcText), new("TNG-Merkur", MsText)];
        RawMethodText[] savedAgain = [new("TNG-Merkur", MsText.Replace("someone", "someone else", StringComparison.Ordinal)), new("SiiXcalibur", LcText)];
        RawMethodText[] changed = [new("SiiXcalibur", LcText), new("TNG-Merkur", MsText.Replace("24", "60", StringComparison.Ordinal))];

        ThermoRawHeader.Fingerprint(method).ShouldBe(ThermoRawHeader.Fingerprint(savedAgain));
        ThermoRawHeader.Fingerprint(method).ShouldNotBe(ThermoRawHeader.Fingerprint(changed));
        ThermoRawHeader.Fingerprint(method).Length.ShouldBe(12);
    }

    [Fact]
    public void Other_files_and_other_format_versions_are_refused_with_a_reason()
    {
        Should.Throw<RawFormatException>(() => ThermoRawHeader.ReadLayout(new byte[70_000], 70_000)).Message.ShouldBe("is not a Thermo raw file");
        var old = MakeRawFile(DateTime.Now, version: 63);
        Should.Throw<RawFormatException>(() => ThermoRawHeader.ReadLayout(old.AsSpan(0, ThermoRawHeader.StartLength), old.Length))
            .Message.ShouldBe("is in Thermo raw format version 63, which LabOps cannot read yet (it reads version 66)");
    }

    /// <summary>LAB_RAW_FILES: real raw files read the same way, from their start and instrument block only.</summary>
    [Fact]
    public void Real_raw_files_read()
    {
        var folder = Environment.GetEnvironmentVariable("LAB_RAW_FILES");
        Assert.SkipWhen(string.IsNullOrEmpty(folder), "Set LAB_RAW_FILES to a folder of Thermo .raw files to read real ones.");
        var paths = Directory.GetFiles(folder!, "*.raw");
        paths.ShouldNotBeEmpty();
        foreach (var path in paths)
        {
            using var f = File.OpenRead(path);
            var start = new byte[Math.Min(ThermoRawHeader.StartLength, f.Length)];
            f.ReadExactly(start);
            var layout = ThermoRawHeader.ReadLayout(start, f.Length);
            var all = new byte[ThermoRawHeader.MethodEnd(layout)];
            f.Position = 0;
            f.ReadExactly(all);
            f.Position = layout.MassSpectrometer!.RunHeader + ThermoRawHeader.InstrumentOffset;
            var block = new byte[Math.Min(ThermoRawHeader.InstrumentLength, f.Length - f.Position)];
            f.ReadExactly(block);

            ThermoRawHeader.ReadInstrument(block).Serial.ShouldNotBeNull(path);
            var methods = ThermoRawHeader.ReadMethods(all, layout);
            methods.ShouldNotBeEmpty(path);
            // The MS method summary parses into its experiments, as RawMethodCheck parses it.
            ThermoMethod.Read(methods).Ms.ShouldNotBeNull(path).Experiments.ShouldNotBeEmpty(path);
            ThermoRawHeader.ReadMethodFile(all, layout).ShouldNotBeNull(path);
            layout.Acquired.Year.ShouldBeInRange(2010, DateTime.Now.Year, path);
        }
    }

    // -- reading on Panorama ------------------------------------------------------------------

    private const string Folder = "/MacCoss/Lab/Pilot/@files/RawFiles";

    /// <summary>A file area with Plate_1 (two raw files and a text file) and Plate_2 (one raw file that is not one).</summary>
    private static Dictionary<string, byte[]> Files() => new()
    {
        ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-001.raw"] = MakeRawFile(new DateTime(2026, 10, 1, 9, 0, 0)),
        ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-002.raw"] = MakeRawFile(new DateTime(2026, 10, 2, 9, 0, 0)),
        ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_2/S2-001.raw"] = new byte[100_000],
    };

    private static HttpResponseMessage Answer(HttpRequestMessage request, Dictionary<string, byte[]> files, bool ignoreRange = false)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        if (request.RequestUri.Query == "?method=json")
        {
            string[] names = path switch
            {
                "/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/" => ["Plate_1/", "Plate_2/"],
                "/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/" => ["S1-001.raw", "S1-002.raw", "notes.txt"],
                "/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_2/" => ["S2-001.raw"],
                _ => [],
            };
            var listing = new JsonObject
            {
                ["files"] = new JsonArray([.. names.Select(n => (JsonNode)new JsonObject { ["text"] = n.TrimEnd('/'), ["collection"] = n.EndsWith('/') })]),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listing.ToJsonString()) };
        }

        var file = files[path];
        if (ignoreRange)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
        }

        var range = request.Headers.Range!.Ranges.Single();
        var from = range.From!.Value;
        var to = Math.Min(range.To!.Value, file.Length - 1);
        var content = new ByteArrayContent(file[(int)from..(int)(to + 1)]);
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, file.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    [Fact]
    public async Task A_raw_data_folder_is_read_by_parts_of_each_file_and_its_subfolders()
    {
        var files = Files();
        var handler = new StubHandler(r => Answer(r, files));
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), handler);
        var reader = new PanoramaRawReader(client);

        var listed = await reader.ListAsync([Folder], TestContext.Current.CancellationToken);
        var progress = new List<int>();
        var read = await reader.ReadAsync(listed.Files, new SyncProgress(progress), TestContext.Current.CancellationToken);

        listed.Files.Select(f => f.Name).ShouldBe(["S1-001.raw", "S1-002.raw", "S2-001.raw"]);
        listed.Missing.ShouldBeEmpty();
        read.Files.Select(f => (f.Folder, f.Acquired, f.MethodFile, f.Instrument.Serial)).ShouldBe(
        [
            ($"{Folder}/Plate_1", new DateTime(2026, 10, 1, 9, 0, 0), "Astral_DIA_24m.meth", "OA10308"),
            ($"{Folder}/Plate_1", new DateTime(2026, 10, 2, 9, 0, 0), "Astral_DIA_24m.meth", "OA10308"),
        ]);
        read.Files.Select(f => f.Method).Distinct().ShouldHaveSingleItem();
        read.Problems.ShouldHaveSingleItem().Message.ShouldBe("S2-001.raw is not a Thermo raw file.");
        // In order, though six files are read at once.
        progress.ShouldBe([1, 2, 3]);
        // Every file request asks for a part, and none asks for a whole file.
        var reads = handler.Requests.Where(r => r.RequestUri!.Query.Length == 0).ToList();
        reads.ShouldAllBe(r => r.Headers.Range != null);
        reads.ShouldAllBe(r => r.Headers.Authorization!.Scheme == "Basic");
    }

    [Fact]
    public async Task The_engine_gets_folders_times_methods_and_instruments_but_no_file_names()
    {
        var files = Files();
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), new StubHandler(r => Answer(r, files)));
        var reader = new PanoramaRawReader(client);
        var read = await reader.ReadAsync((await reader.ListAsync([Folder], TestContext.Current.CancellationToken)).Files, null, TestContext.Current.CancellationToken);

        var json = read.ToEngineJson();

        json.ShouldNotContain("S1-00");
        var first = JsonNode.Parse(json)!["files"]!.AsArray()[0]!;
        first["folder"]!.GetValue<string>().ShouldBe($"{Folder}/Plate_1");
        first["acquired"]!.GetValue<string>().ShouldBe("2026-10-01T09:00:00");
        first["model"]!.GetValue<string>().ShouldBe("Orbitrap Astral Zoom");
        first["method_file"]!.GetValue<string>().ShouldBe("Astral_DIA_24m.meth");
    }

    [Fact]
    public async Task A_server_that_sends_whole_files_is_not_read_and_a_refused_sign_in_stops_the_reading()
    {
        var files = Files();
        using (var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), new StubHandler(r => Answer(r, files, ignoreRange: true))))
        {
            var reader = new PanoramaRawReader(client);
            var read = await reader.ReadAsync((await reader.ListAsync([Folder], TestContext.Current.CancellationToken)).Files, null, TestContext.Current.CancellationToken);
            read.Files.ShouldBeEmpty();
            read.Problems[0].Message.ShouldBe($"Panorama sent the whole of {Folder}/Plate_1/S1-001.raw instead of part of it, so LabOps did not read it.");
        }

        using var refused = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"),
            new StubHandler(r => r.RequestUri!.Query == "?method=json" ? Answer(r, files) : new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var refusedReader = new PanoramaRawReader(refused);
        var listed = await refusedReader.ListAsync([Folder], TestContext.Current.CancellationToken);
        (await Should.ThrowAsync<PanoramaException>(() => refusedReader.ReadAsync(listed.Files, null, TestContext.Current.CancellationToken)))
            .IsSignInProblem.ShouldBeTrue();
    }

    [Fact]
    public void What_the_files_said_reads_as_one_line_per_method()
    {
        AcquisitionRead[] reads =
        [
            new("/x/Plate_2", "DIA.meth", "Orbitrap Astral Zoom", "OA10308", 96, "2026-09-25", "2026-09-27", "aaa"),
            new("/x/Plate_1", "DIA.meth", "Orbitrap Astral Zoom", "OA10308", 100, "2026-09-22", "2026-09-24", "aaa"),
            new("/x/Library", "GPF.meth", "Orbitrap Astral Zoom", "OA10308", 1, "2026-09-21", "2026-09-21", "bbb"),
        ];

        AcquisitionRead.Describe(reads).ShouldBe(
        [
            "1 raw file, 2026-09-21: GPF.meth on Orbitrap Astral Zoom (OA10308)",
            "196 raw files, 2026-09-22 to 2026-09-27: DIA.meth on Orbitrap Astral Zoom (OA10308)",
        ]);
    }

    [Fact]
    public void An_instrument_known_only_by_its_serial_is_named_by_it()
    {
        AcquisitionRead[] reads = [new("/x", "DIA.meth", null, "OA10308", 2, "2026-09-21", "2026-09-22", "aaa")];

        AcquisitionRead.Describe(reads).ShouldBe(["2 raw files, 2026-09-21 to 2026-09-22: DIA.meth on serial OA10308"]);
    }

    [Fact]
    public async Task A_folders_method_is_taken_out_of_the_first_raw_file_acquired_with_it()
    {
        var files = new Dictionary<string, byte[]>
        {
            // Another method first, then a file that is not a raw file, then the one.
            ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-001.raw"] = MakeRawFile(new DateTime(2026, 10, 1, 9, 0, 0), msText: MsText.Replace("24", "60", StringComparison.Ordinal)),
            ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-002.raw"] = new byte[100_000],
            ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-003.raw"] = MakeRawFile(new DateTime(2026, 10, 2, 9, 0, 0)),
        };
        var handler = new StubHandler(r => r.RequestUri!.Query == "?method=json"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JsonObject
                {
                    ["files"] = new JsonArray([.. files.Keys.Select(k => (JsonNode)new JsonObject { ["text"] = k[(k.LastIndexOf('/') + 1)..], ["collection"] = false })]),
                }.ToJsonString()),
            }
            : Answer(r, files));
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), handler);
        var wanted = ThermoRawHeader.Fingerprint([new("SiiXcalibur", LcText), new("TNG-Merkur", MsText)]);

        var capture = (await new PanoramaRawReader(client).FindMethodAsync($"{Folder}/Plate_1", wanted, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        capture.MethodFile.ShouldBe("Astral_DIA_24m.meth");
        ThermoRawHeader.Fingerprint(ThermoRawHeader.ReadMethodFileText(capture.Method)).ShouldBe(wanted);
        (await new PanoramaRawReader(client).FindMethodAsync($"{Folder}/Plate_1", "000000000000", TestContext.Current.CancellationToken)).ShouldBeNull();
        // Every request asked for a part of a file, never a whole one.
        handler.Requests.Where(r => r.RequestUri!.Query.Length == 0).ShouldAllBe(r => r.Headers.Range != null);
    }

    [Fact]
    public async Task A_method_past_the_first_read_is_found_in_the_rest()
    {
        // The method usually starts 4 KB in; here a long header puts it past the first 64 KB.
        var files = new Dictionary<string, byte[]>
        {
            ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-001.raw"] = MakeRawFile(new DateTime(2026, 10, 1, 9, 0, 0)),
            ["/_webdav/MacCoss/Lab/Pilot/@files/RawFiles/Plate_1/S1-002.raw"] = MakeRawFile(new DateTime(2026, 10, 2, 9, 0, 0), methodAt: 70_000),
        };
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), new StubHandler(r => Answer(r, files)));
        var reader = new PanoramaRawReader(client);
        PanoramaEntry[] entries = [.. files.Keys.Select(path => new PanoramaEntry(path[(path.LastIndexOf('/') + 1)..], path, false))];

        var read = await reader.ReadAsync(entries, null, TestContext.Current.CancellationToken);

        read.Problems.ShouldBeEmpty();
        read.Files.Count.ShouldBe(2);
        // The same method, so the same fingerprint, not the empty method's.
        read.Files.Select(f => f.Method).Distinct().ShouldHaveSingleItem().ShouldNotBe(ThermoRawHeader.Fingerprint([]));
        read.Files.ShouldAllBe(f => f.MethodFile == "Astral_DIA_24m.meth");
    }

    [Fact]
    public async Task A_folder_not_on_Panorama_yet_is_missing_and_the_others_are_listed()
    {
        var files = Files();
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), new StubHandler(r =>
            r.RequestUri!.AbsolutePath.Contains("/Later/", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.NotFound) : Answer(r, files)));
        var reader = new PanoramaRawReader(client);

        var listed = await reader.ListAsync([Folder, "/MacCoss/Lab/Pilot/@files/Later"], TestContext.Current.CancellationToken);

        listed.Files.Count.ShouldBe(3);
        listed.Missing.ShouldBe(["/MacCoss/Lab/Pilot/@files/Later"]);
    }

    [Fact]
    public async Task A_file_that_fails_in_an_unexpected_way_is_a_problem_and_the_others_are_read()
    {
        var files = Files();
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("key"), new StubHandler(r =>
            r.RequestUri!.AbsolutePath.EndsWith("S1-002.raw", StringComparison.Ordinal) ? throw new InvalidOperationException("damaged") : Answer(r, files)));
        var reader = new PanoramaRawReader(client);

        var read = await reader.ReadAsync((await reader.ListAsync([Folder], TestContext.Current.CancellationToken)).Files, null, TestContext.Current.CancellationToken);

        read.Files.ShouldHaveSingleItem().Acquired.ShouldBe(new DateTime(2026, 10, 1, 9, 0, 0));
        read.Problems.Select(p => p.Message).ShouldBe(["S1-002.raw could not be read (damaged).", "S2-001.raw is not a Thermo raw file."]);
    }

    /// <summary>Reports at once, on the calling thread, unlike Progress&lt;T&gt;.</summary>
    private sealed class SyncProgress(List<int> seen) : IProgress<int>
    {
        public void Report(int value)
        {
            lock (seen)
            {
                seen.Add(value);
            }
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(respond(request));
        }
    }
}
