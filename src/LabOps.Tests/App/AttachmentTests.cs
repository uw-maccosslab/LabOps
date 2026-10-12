using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.App.Views;
using LabOps.Core.Infrastructure;
using LabOps.Core.Projects;
using LabOps.Core.Protocols;

namespace LabOps.Tests.App;

/// <summary>Files attached in the chat, and several files uploaded for a protocol.</summary>
public sealed class AttachmentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("labops-attach-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_sample_sheet_with_identifiers_never_reaches_claude()
    {
        var refused = new ScanResult(
            [new ScanSheet("Samples", 40, ["Sample_ID", "Last Name"])],
            [new ScanFinding("ERROR", "samples.csv", "Last Name", "is a name column", 40)], Errors: 1);
        IdentifierCheck.Note(refused).ShouldBeNull();
        IdentifierCheck.Errors(refused).ShouldStartWith("- ");

        var clean = new ScanResult(
            [new ScanSheet("Samples", 40, ["Sample_ID", "Group"])],
            [new ScanFinding("WARN", "samples.csv", "Group", "is free text", null)], Errors: 0);
        var note = IdentifierCheck.Note(clean).ShouldNotBeNull();
        note.ShouldStartWith("scanned by the app: Samples: 40 rows, 2 columns; nothing identifying");
        note.ShouldContain("warnings to check:");
    }

    [Theory]
    [InlineData("CLAUDE.md", "CLAUDE.md.txt")]
    [InlineData("claude.local.md", "claude.local.md.txt")]
    [InlineData(@"repo\.claude\settings.json", @"repo\_claude\settings.json")]
    [InlineData(@"KingFisher Methods\SAX_KF1_current.kfx", @"KingFisher Methods\SAX_KF1_current.kfx")]
    public void Nothing_attached_can_be_taken_as_instructions_for_claude(string given, string kept) =>
        ChatViewModel.SafeRelative(given).ShouldBe(kept);

    [Fact]
    public void A_dropped_folder_attaches_its_files_under_its_name_and_copies_never_overwrite()
    {
        var methods = Directory.CreateDirectory(Path.Combine(_root, "in", "KingFisher Methods", "Apex")).FullName;
        File.WriteAllText(Path.Combine(methods, "SAX_KF1.kfx"), "kf1");
        var lone = Path.Combine(_root, "in", "request.pdf");
        File.WriteAllText(lone, "pdf");

        var files = ChatViewModel.ExpandFolders([Path.Combine(_root, "in", "KingFisher Methods"), lone, Path.Combine(_root, "missing")]).ToList();

        files.Select(f => f.Relative).ShouldBe([Path.Combine("KingFisher Methods", "Apex", "SAX_KF1.kfx"), "request.pdf"]);
        var folder = Path.Combine(_root, "conversation");
        var first = ChatViewModel.CopyInto(folder, lone, "request.pdf");
        var second = ChatViewModel.CopyInto(folder, lone, "request.pdf");
        Path.GetFileName(first).ShouldBe("request.pdf");
        Path.GetFileName(second).ShouldBe("request (2).pdf");
        ChatViewModel.CopyInto(folder, files[0].Source, files[0].Relative)
            .ShouldBe(Path.Combine(folder, "KingFisher Methods", "Apex", "SAX_KF1.kfx"));
    }

    [Fact]
    public void Attachments_left_by_an_earlier_run_are_cleared_at_startup()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        Directory.Exists(paths.AttachmentsDirectory).ShouldBeTrue();
        var old = Directory.CreateDirectory(Path.Combine(paths.AttachmentsDirectory, "abc123")).FullName;
        File.WriteAllText(Path.Combine(old, "sheet.csv"), "x");

        paths.ClearAttachments();

        Directory.Exists(old).ShouldBeFalse();
        Directory.Exists(paths.AttachmentsDirectory).ShouldBeTrue();
    }

    [Fact]
    public void A_method_file_uploaded_alone_is_named_as_a_method_not_read_as_the_protocol()
    {
        var alone = new ProtocolImport("pnnl-proinsulin", "inbox/pnnl-proinsulin", "inbox/pnnl-proinsulin/IP-ProINS_UB.bdz",
            "inbox/pnnl-proinsulin/text.md", 0, [], null, Scanned: false, Exists: true)
        {
            Files = [new("inbox/pnnl-proinsulin/IP-ProINS_UB.bdz", "other", null, null, null, Scanned: false)],
        };
        ProtocolsViewModel.NeedsFileList(alone).ShouldBeTrue();

        var prompt = ProtocolsViewModel.UpdateFromFilesPrompt(
            new ProtocolSummary { Id = "pnnl-proinsulin", Folder = "protocols/pnnl-proinsulin" }, alone);

        prompt.ShouldContain("IP-ProINS_UB.bdz (not a document");
        prompt.ShouldContain("methods/ folder");
        prompt.ShouldNotContain("extracted its text");
    }

    [Theory]
    [InlineData("The protocol engine could not run: protocol.py: error: unrecognized arguments: b.bdz", 2, true)]
    [InlineData("The protocol engine could not run: protocol.py: error: unrecognized arguments: b.bdz", 1, false)]
    [InlineData("a.pdf is not a file", 2, false)]
    public void Several_files_on_an_older_protocol_engine_say_to_sync_first(string error, int files, bool older)
    {
        ProtocolEngine.IsOlderEngine(error, files).ShouldBe(older);
        ProtocolEngine.OlderEngine.ShouldContain("Choose Sync");
    }

    /// <summary>
    /// Ctrl+V in the message box attaches files copied in Explorer, and a picture with no text (a
    /// screen capture) as a PNG; text pastes as text.
    /// </summary>
    [Fact]
    public void A_paste_attaches_copied_files_or_a_picture_and_leaves_text_to_paste()
    {
        var copied = new System.Windows.DataObject();
        copied.SetData(System.Windows.DataFormats.FileDrop, new[] { @"C:\Data\request.pdf" });
        ChatPanel.WhatToAttach(copied).Files.ShouldBe([@"C:\Data\request.pdf"]);

        var pixels = new byte[2 * 2 * 4];
        var picture = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 8);
        var capture = new System.Windows.DataObject();
        capture.SetData(System.Windows.DataFormats.Bitmap, picture);
        var png = ChatPanel.WhatToAttach(capture).Png.ShouldNotBeNull();
        png.Take(8).ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "a PNG file");

        var asPng = new System.Windows.DataObject();
        asPng.SetData("PNG", new MemoryStream(png));
        ChatPanel.WhatToAttach(asPng).Png.ShouldBe(png, "a capture's own PNG is kept as it is");

        var text = new System.Windows.DataObject();
        text.SetData(System.Windows.DataFormats.UnicodeText, "a note");
        text.SetData(System.Windows.DataFormats.Bitmap, picture);
        ChatPanel.WhatToAttach(text).ShouldBe((null, null), "text with a picture (from a document) pastes as text");

        ChatViewModel.PastedImageName(new DateTime(2026, 10, 12, 14, 3, 27)).ShouldBe("Pasted image 2026-10-12 14.03.27.png");
    }
}
