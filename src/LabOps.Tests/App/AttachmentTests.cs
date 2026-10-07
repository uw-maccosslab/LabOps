using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Engines;
using LabOps.Core.Infrastructure;
using LabOps.Core.Processes;
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

    [Fact]
    public async Task Several_files_on_an_older_protocol_engine_say_to_sync_first()
    {
        var engine = new ProtocolEngine(new Refusing(), new ToolLocator()) { RepositoryPath = _root };

        var error = await Should.ThrowAsync<EngineException>(() => engine.ImportAsync(["a.pdf", "b.bdz"]));

        error.Message.ShouldBe(ProtocolEngine.OlderEngine);
    }

    /// <summary>protocol.py from before several files: argparse refuses the extra ones.</summary>
    private sealed class Refusing : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(2, "", "usage: protocol.py import [-h] file\nprotocol.py: error: unrecognized arguments: b.bdz"));
    }
}
