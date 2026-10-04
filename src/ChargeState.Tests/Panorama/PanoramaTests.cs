using System.Net;
using System.Text;
using ChargeState.App.ViewModels;
using ChargeState.Core.Panorama;

namespace ChargeState.Tests.Panorama;

/// <summary>Signing in to Panorama with PanoramaBridge's saved sign-in, and browsing its folders read-only.</summary>
public sealed class PanoramaTests
{
    private static readonly Uri Server = new("https://panoramaweb.org");

    /// <summary>The form of panoramaweb.org's ?method=json (captured from /_webdav/home/@files/).</summary>
    private const string Listing = """
    {"files":[
      {"id":"/_webdav/home/%40files/Slides","href":"/_webdav/home/%40files/Slides/","text":"Slides",
       "options":"OPTIONS, GET, HEAD, COPY, LOCK, UNLOCK, PROPFIND","canRead":true,"collection":true,"leaf":false},
      {"id":"/_webdav/home/%40files/poster.pdf","href":"/_webdav/home/%40files/poster.pdf","text":"poster.pdf",
       "collection":false,"contentlength":8185763,"leaf":true},
      {"text":"@files","leaf":false}
    ]}
    """;

    // -- sign-in ------------------------------------------------------------------------------

    [Fact]
    public void PanoramaBridges_sign_in_is_tried_first_then_ChargeStates()
    {
        var store = new FakeStore();
        var signIn = new PanoramaSignIn(store, Server);
        signIn.Candidates().ShouldBeEmpty();

        store.Entries["ChargeState:https://panoramaweb.org"] = new("mike@uw.edu", "typed-password");
        store.Entries["PanoramaBridge:https://panoramaweb.org"] = new("apikey", "bridge-key");
        var candidates = signIn.Candidates();

        candidates.Select(c => (c.Source, c.UserName, c.IsApiKey)).ShouldBe(
            [("PanoramaBridge", "apikey", true), ("ChargeState", "mike@uw.edu", false)]);
        candidates[0].ToString().ShouldBe("an API key");
        candidates[1].ToString().ShouldBe("user mike@uw.edu");
        candidates.ShouldAllBe(c => !c.ToString().Contains("key") || c.IsApiKey);
    }

    [Fact]
    public void Saving_and_forgetting_touch_only_ChargeStates_own_entry()
    {
        var store = new FakeStore();
        store.Entries["PanoramaBridge:https://panoramaweb.org"] = new("apikey", "bridge-key");
        var signIn = new PanoramaSignIn(store, Server);

        var saved = signIn.Save(PanoramaCredential.Login("mike@uw.edu", "secret"));
        saved.Source.ShouldBe("ChargeState");
        store.Entries["ChargeState:https://panoramaweb.org"].ShouldBe(new StoredCredential("mike@uw.edu", "secret"));

        signIn.Forget();
        store.Entries.Keys.ShouldBe(["PanoramaBridge:https://panoramaweb.org"]);
    }

    [Fact]
    public void An_API_key_signs_in_as_apikey_and_a_legacy_prefix_is_dropped()
    {
        var key = PanoramaCredential.ApiKey("  apikey|abc123  ");
        key.Secret.ShouldBe("abc123");
        key.ToAuthenticationHeader().ToString().ShouldBe("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("apikey:abc123")));
        PanoramaCredential.FromStored(new StoredCredential("", "x"), "PanoramaBridge").ShouldBeNull();
        new StoredCredential("apikey", "abc123").ToString().ShouldNotContain("abc123");
    }

    [Fact]
    public void The_Windows_credential_store_round_trips_a_secret_as_PanoramaBridge_writes_it()
    {
        var store = new WindowsCredentialStore();
        var target = $"ChargeState-test:https://example.invalid#{Guid.NewGuid():n}";
        try
        {
            store.Read(target).ShouldBeNull();
            store.Write(target, new StoredCredential("apikey", "test-secret-é"), "ChargeState test");
            store.Read(target).ShouldBe(new StoredCredential("apikey", "test-secret-é"));
        }
        finally
        {
            store.Delete(target);
        }

        store.Read(target).ShouldBeNull();
    }

    // -- listing ------------------------------------------------------------------------------

    [Fact]
    public void A_listing_gives_folders_and_files_with_paths_built_from_the_parent()
    {
        var entries = PanoramaClient.Parse(Listing, "/_webdav/home/@files");

        entries.Select(e => (e.Name, e.Path, e.IsFolder)).ShouldBe(
        [
            ("Slides", "/_webdav/home/@files/Slides/", true),
            ("poster.pdf", "/_webdav/home/@files/poster.pdf", false),
            ("@files", "/_webdav/home/@files/@files/", true),
        ]);
    }

    [Fact]
    public void A_sign_in_page_instead_of_a_listing_is_a_sign_in_problem() =>
        Should.Throw<PanoramaException>(() => PanoramaClient.Parse("<!DOCTYPE html><html>Sign in</html>", "/_webdav/"))
            .IsSignInProblem.ShouldBeTrue();

    [Fact]
    public async Task Listing_asks_for_json_signed_in_and_keeps_at_signs_in_the_address()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listing) });
        using var files = new PanoramaClient(Server, PanoramaCredential.ApiKey("k"), handler);

        var entries = await files.ListAsync("/_webdav/MacCoss/maccoss/@files/Dog Aging");

        entries.Count.ShouldBe(3);
        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.ShouldBe("https://panoramaweb.org/_webdav/MacCoss/maccoss/@files/Dog%20Aging/?method=json");
        request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        entries[0].Path.ShouldBe("/_webdav/MacCoss/maccoss/@files/Dog Aging/Slides/");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true, "did not accept the sign-in saved by PanoramaBridge (an API key)")]
    [InlineData(HttpStatusCode.Found, true, "did not accept the sign-in")]
    [InlineData(HttpStatusCode.Forbidden, false, "cannot open /MacCoss/private")]
    [InlineData(HttpStatusCode.NotFound, false, "/MacCoss/private is not on Panorama")]
    [InlineData(HttpStatusCode.InternalServerError, false, "Panorama answered 500")]
    public async Task Failures_say_what_went_wrong(HttpStatusCode status, bool signIn, string message)
    {
        using var files = new PanoramaClient(Server, PanoramaCredential.ApiKey("k", "PanoramaBridge"),
            new StubHandler(_ => new HttpResponseMessage(status)));

        var ex = await Should.ThrowAsync<PanoramaException>(() => files.ListAsync("/_webdav/MacCoss/private/"));

        ex.IsSignInProblem.ShouldBe(signIn);
        ex.Message.ShouldContain(message);
        ex.Message.ShouldNotContain("\"k\"");
    }

    [Fact]
    public async Task A_key_typed_into_the_sign_in_window_is_called_this_API_key()
    {
        using var files = new PanoramaClient(Server, PanoramaCredential.ApiKey("k"),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var ex = await Should.ThrowAsync<PanoramaException>(() => files.ListAsync("/_webdav/"));

        ex.Message.ShouldBe("Panorama did not accept this API key. Check it and try again.");
    }

    // -- notebooks ----------------------------------------------------------------------------

    /// <summary>The form of query-selectRows.api for labbook.Notebook on panoramaweb.org (made-up notebooks).</summary>
    private const string Notebooks = """
    {"schemaName":"labbook","queryName":"Notebook","rowCount":3,"rows":[
      {"Status":"inProgress","RowId":131,"_labkeyurl_RowId":"/MacCoss/query-detailsQueryRow.view?schemaName=labbook&query.queryName=Notebook&RowId=131",
       "Title":"Plasma prep, BioTRACK","Name":"ELN-1567-20250730-131","Modified":"2025-07-30 10:50:16.399","CreatedBy/DisplayName":"pat","Archived":false},
      {"Status":"submittedForReview","RowId":170,"Title":"Stellar PRM assay","Name":"ELN-1652-20260819-170",
       "Modified":"2026-08-19 09:00:00.000","CreatedBy/DisplayName":"lee","Archived":false},
      {"Status":"signed","RowId":12,"Title":"Old gradients","Name":"ELN-12","Modified":"2023-01-05 12:00:00.000",
       "CreatedBy/DisplayName":"pat","Archived":true}
    ]}
    """;

    [Fact]
    public void Notebooks_are_read_with_their_ID_title_status_and_address()
    {
        var notebooks = PanoramaClient.ParseNotebooks(Notebooks);

        notebooks.Select(n => (n.RowId, n.Id, n.Title, n.StatusText, n.Author, n.Archived)).ShouldBe(
        [
            (131, "ELN-1567-20250730-131", "Plasma prep, BioTRACK", "In progress", "pat", false),
            (170, "ELN-1652-20260819-170", "Stellar PRM assay", "Submitted for review", "lee", false),
            (12, "ELN-12", "Old gradients", "Signed", "pat", true),
        ]);
        notebooks[0].Url().ShouldBe("https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/131");
        notebooks[0].ModifiedText.ShouldBe("2025-07-30");
    }

    [Theory]
    [InlineData("ELN-1567-20250730-131", "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/131")]
    [InlineData("ELN-20250730-131", "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/131")]
    [InlineData(" eln-131 ", "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/131")]
    [InlineData("Lab book 7", null)]
    [InlineData(null, null)]
    public void A_notebook_ID_gives_its_address(string? id, string? url) =>
        PanoramaPaths.NotebookUrlFromId(id).ShouldBe(url);

    [Fact]
    public async Task Notebooks_come_from_labbook_in_the_MacCoss_project_without_templates()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Notebooks) });
        using var client = new PanoramaClient(Server, PanoramaCredential.ApiKey("k"), handler);

        (await client.ListNotebooksAsync()).Count.ShouldBe(3);

        var url = handler.Requests.Single().RequestUri!;
        url.AbsolutePath.ShouldBe("/MacCoss/query-selectRows.api");
        url.Query.ShouldContain("schemaName=labbook&query.queryName=Notebook");
        url.Query.ShouldContain("query.Template~eq=false");
    }

    [Fact]
    public void A_sign_in_page_instead_of_notebooks_is_a_sign_in_problem() =>
        Should.Throw<PanoramaException>(() => PanoramaClient.ParseNotebooks("<html>Sign in</html>")).IsSignInProblem.ShouldBeTrue();

    [Fact]
    public async Task The_notebook_picker_searches_and_hides_archived_notebooks()
    {
        var vm = new NotebookPickerViewModel(new FakeFiles { NotebookList = PanoramaClient.ParseNotebooks(Notebooks) });

        await vm.InitializeAsync();

        vm.Notebooks.Select(n => n.RowId).ShouldBe([170, 131]);  // newest first, archived hidden
        vm.Summary.ShouldBe("2 of 3 notebooks; 1 archived hidden");
        vm.Query = "pat";
        vm.Notebooks.Select(n => n.RowId).ShouldBe([131]);
        vm.ShowArchived = true;
        vm.Notebooks.Select(n => n.RowId).ShouldBe([131, 12]);
        vm.Query = "review stellar";
        vm.Notebooks.Single().Id.ShouldBe("ELN-1652-20260819-170");
        vm.CanChoose.ShouldBeFalse();
        vm.Selected = vm.Notebooks.Single();
        vm.CanChoose.ShouldBeTrue();
    }

    // -- paths --------------------------------------------------------------------------------

    [Theory]
    [InlineData("/_webdav/MacCoss/maccoss/@files/2026-BioTRACK/", "/MacCoss/maccoss/@files/2026-BioTRACK")]
    [InlineData("/_webdav/MacCoss/maccoss/", "/MacCoss/maccoss")]
    [InlineData("/MacCoss/maccoss", "/MacCoss/maccoss")]
    public void WebDav_paths_become_the_folder_lab_projects_records(string webDav, string folder)
    {
        PanoramaPaths.ToFolder(webDav).ShouldBe(folder);
        PanoramaPaths.ToWebDav(folder).ShouldBe(PanoramaPaths.AsFolder("/_webdav" + folder));
    }

    [Theory]
    [InlineData("/MacCoss/maccoss/@files/2026-BioTRACK", "https://panoramaweb.org/_webdav/MacCoss/maccoss/@files/2026-BioTRACK/")]
    [InlineData("/MacCoss/maccoss/@files/Dog Aging", "https://panoramaweb.org/_webdav/MacCoss/maccoss/@files/Dog%20Aging/")]
    [InlineData("/MacCoss/maccoss/2026-Marten", "https://panoramaweb.org/MacCoss/maccoss/2026-Marten/project-begin.view")]
    public void A_file_area_opens_as_its_listing_and_a_folder_as_its_start_page(string folder, string url) =>
        PanoramaPaths.BrowserUrl(folder).ShouldBe(url);

    // -- the browser --------------------------------------------------------------------------

    [Fact]
    public async Task The_browser_opens_at_the_MacCoss_project_with_maccoss_and_Collaborations_showing()
    {
        var files = new FakeFiles
        {
            ["/_webdav/"] = ["MacCoss", "home", ".hidden"],
            ["/_webdav/MacCoss/"] = ["maccoss", "Collaborations", "@files"],
        };
        var vm = new PanoramaBrowserViewModel(files);

        await vm.InitializeAsync();

        vm.Roots.Select(r => r.Name).ShouldBe(["home", "MacCoss"]);
        vm.Chosen.ShouldBe("/MacCoss");
        vm.Selected.ShouldNotBeNull().Children.Select(c => c.Name).ShouldBe(["@files", "Collaborations", "maccoss"]);
    }

    [Fact]
    public async Task The_browser_can_open_the_way_to_a_folder_and_loads_each_folder_once()
    {
        var files = new FakeFiles
        {
            ["/_webdav/"] = ["MacCoss", "home"],
            ["/_webdav/MacCoss/"] = ["maccoss", "@files"],
            ["/_webdav/MacCoss/maccoss/"] = ["2026-Marten", "@files"],
        };
        var vm = new PanoramaBrowserViewModel(files);

        await vm.InitializeAsync("/_webdav/MacCoss/maccoss/");

        vm.Selected.ShouldNotBeNull().Name.ShouldBe("maccoss");
        vm.Chosen.ShouldBe("/MacCoss/maccoss");
        vm.Selected.Children.Select(c => c.Name).ShouldBe(["@files", "2026-Marten"]);  // the file area first
        vm.Selected.Children[0].IsFileArea.ShouldBeTrue();

        vm.Selected.IsExpanded = false;
        vm.Selected.IsExpanded = true;
        files.Calls.Count(c => c == "/_webdav/MacCoss/maccoss/").ShouldBe(1);
    }

    [Fact]
    public async Task A_rejected_sign_in_is_reported_so_the_app_can_ask_for_another()
    {
        var vm = new PanoramaBrowserViewModel(new FakeFiles { SignInFails = true });

        await vm.InitializeAsync();

        vm.SignInFailed.ShouldBeTrue();
        vm.Error.ShouldNotBeNull();
        vm.CanChoose.ShouldBeFalse();
    }

    [Fact]
    public async Task A_folder_that_fails_to_open_can_be_tried_again()
    {
        var files = new FakeFiles { ["/_webdav/"] = ["MacCoss"], ["/_webdav/MacCoss/"] = ["maccoss"] };
        var vm = new PanoramaBrowserViewModel(files);
        await vm.InitializeAsync("/_webdav/");
        var node = vm.Roots.Single();
        files.FailOnce.Add("/_webdav/MacCoss/");

        await node.LoadChildrenAsync();
        node.Error.ShouldNotBeNull();
        await node.LoadChildrenAsync();
        node.Error.ShouldBeNull();
        node.Children.Single().Name.ShouldBe("maccoss");
    }

    private sealed class FakeStore : ICredentialStore
    {
        public Dictionary<string, StoredCredential> Entries { get; } = [];

        public StoredCredential? Read(string target) => Entries.TryGetValue(target, out var c) ? c : null;

        public void Write(string target, StoredCredential credential, string comment) => Entries[target] = credential;

        public void Delete(string target) => Entries.Remove(target);
    }

    private sealed class FakeFiles : Dictionary<string, string[]>, IPanoramaClient
    {
        public List<string> Calls { get; } = [];

        public HashSet<string> FailOnce { get; } = [];

        public bool SignInFails { get; init; }

        public IReadOnlyList<PanoramaNotebook> NotebookList { get; init; } = [];

        public Task<IReadOnlyList<PanoramaNotebook>> ListNotebooksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(NotebookList);

        public Task<IReadOnlyList<PanoramaEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            Calls.Add(path);
            if (SignInFails)
            {
                throw new PanoramaException("Panorama did not accept the sign-in.") { IsSignInProblem = true };
            }

            if (FailOnce.Remove(path))
            {
                throw new PanoramaException("Could not reach Panorama.");
            }

            IReadOnlyList<PanoramaEntry> entries = TryGetValue(path, out var names)
                ? [.. names.Select(n => new PanoramaEntry(n, path + n + "/", IsFolder: true))]
                : [];
            return Task.FromResult(entries);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
