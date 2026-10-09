using System.Net;
using System.Text;
using System.Text.Json;
using LabOps.App.Services;
using LabOps.Core.Infrastructure;
using LabOps.Core.Panorama;
using LabOps.Core.Projects;
using LabOps.Core.Projects.Dashboard;
using LabOps.Core.Quotes;
using LabOps.Tests.Projects;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabOps.Tests.Panorama;

/// <summary>The lab dashboard on Panorama: what may be written, by whom, and when.</summary>
public sealed class DashboardPublisherTests
{
    private static readonly Uri Server = new("https://panoramaweb.org");
    private static readonly WikiLocation Where = new("/MacCoss/LabOps", null);
    private static readonly DateOnly Today = DashboardSample.Today;

    // -- the page ------------------------------------------------------------------------------

    [Fact]
    public void The_page_is_marked_as_LabOps_and_notices_an_edit_on_Panorama()
    {
        var page = DashboardPage.Build(DashboardSample.List(), Today);

        DashboardPage.IsOurs(page).ShouldBeTrue();
        DashboardPage.EditedOnPanorama(page).ShouldBeFalse();
        DashboardPage.EditedOnPanorama(page.Replace("Needs attention", "Needs our attention", StringComparison.Ordinal)).ShouldBeTrue();
        DashboardPage.IsOurs("<p>Welcome to the LabOps folder</p>").ShouldBeFalse();
    }

    [Fact]
    public void Only_a_person_creates_or_takes_back_the_page_and_nobody_overwrites_someone_elses()
    {
        var page = DashboardPage.Build(DashboardSample.List(), Today);
        var yesterday = DashboardPage.Build(DashboardSample.List(), Today.AddDays(-1));
        var edited = yesterday.Replace("Needs attention", "Needs our attention", StringComparison.Ordinal);
        const string handWritten = "<p>Welcome to the LabOps folder</p>";

        DashboardPage.Decide(null, page, byPerson: true).ShouldBe(DashboardDecision.Publish);
        DashboardPage.Decide(null, page, byPerson: false).ShouldBe(DashboardDecision.NotOurs);
        DashboardPage.Decide(handWritten, page, byPerson: true).ShouldBe(DashboardDecision.NotOurs);
        DashboardPage.Decide(handWritten, page, byPerson: false).ShouldBe(DashboardDecision.NotOurs);
        DashboardPage.Decide(page, page, byPerson: false).ShouldBe(DashboardDecision.Unchanged);
        DashboardPage.Decide(yesterday, page, byPerson: false).ShouldBe(DashboardDecision.Publish);
        DashboardPage.Decide(edited, page, byPerson: false).ShouldBe(DashboardDecision.Edited);
        DashboardPage.Decide(edited, page, byPerson: true).ShouldBe(DashboardDecision.Publish);
    }

    [Theory]
    [InlineData("dashboard:\n  folder: MacCoss/LabOps/\n", "/MacCoss/LabOps", "default")]
    [InlineData("dashboard:\n  folder: /MacCoss/LabOps\n  page: projects\n", "/MacCoss/LabOps", "projects")]
    [InlineData("min_app_version: \"26.11.0\"\n", null, null)]
    [InlineData("dashboard:\n  folder: /\n", null, null)]
    public void The_projects_repository_names_where_the_dashboard_goes(string yaml, string? folder, string? page)
    {
        var dashboard = RepoConfig.Parse(yaml).Dashboard;

        (dashboard?.Folder, dashboard?.PageName).ShouldBe((folder, page));
    }

    // -- publishing ----------------------------------------------------------------------------

    [Fact]
    public async Task A_person_publishes_the_first_page_and_is_told_where()
    {
        var panorama = new FakePanorama(existing: null);

        var said = await panorama.Publisher.PublishAsync(DashboardSample.List(), Where);

        said.ShouldBe("Lab dashboard published to /MacCoss/LabOps on Panorama. LabOps keeps it up to date from now on.");
        var saved = panorama.Handler.Bodies.ShouldHaveSingleItem();
        saved.GetProperty("name").GetString().ShouldBe("default");
        saved.GetProperty("title").GetString().ShouldBe(DashboardPage.Title);
        saved.GetProperty("body").GetString()!.ShouldContain("id=\"labops-dashboard\"", Case.Sensitive);
    }

    [Fact]
    public async Task A_page_someone_else_made_is_left_alone_even_when_a_person_publishes()
    {
        var panorama = new FakePanorama(existing: "<p>Welcome to the LabOps folder</p>");

        (await panorama.Publisher.PublishAsync(DashboardSample.List(), Where)).ShouldStartWith("The page default in /MacCoss/LabOps", Case.Sensitive);
        (await panorama.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldBeNull();
        panorama.Handler.Bodies.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_background_keeps_LabOps_page_current_and_writes_nothing_that_has_not_changed()
    {
        var current = new FakePanorama(existing: DashboardPage.Build(DashboardSample.List(), Today));
        (await current.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldBeNull();
        current.Handler.Bodies.ShouldBeEmpty();

        var stale = new FakePanorama(existing: DashboardPage.Build(DashboardSample.List(), Today.AddDays(-1)));
        (await stale.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldBeNull();
        stale.Handler.Bodies.ShouldHaveSingleItem().GetProperty("pageVersionId").GetInt32().ShouldBe(18161);

        var none = new FakePanorama(existing: null);
        (await none.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldBeNull();
        none.Handler.Bodies.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_edit_on_Panorama_stops_the_updates_and_is_mentioned_once()
    {
        var edited = DashboardPage.Build(DashboardSample.List(), Today.AddDays(-1)).Replace("Needs attention", "Ours", StringComparison.Ordinal);
        var panorama = new FakePanorama(existing: edited);

        (await panorama.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldNotBeNull().ShouldContain("edited on Panorama", Case.Sensitive);
        (await panorama.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldBeNull();
        panorama.Handler.Bodies.ShouldBeEmpty();
    }

    [Fact]
    public void A_copy_that_has_not_just_synced_or_has_no_dashboard_publishes_nothing()
    {
        var panorama = new FakePanorama(existing: null);

        panorama.Publisher.UpdateInBackground(DashboardSample.List(), Where, synced: false);
        panorama.Publisher.UpdateInBackground(DashboardSample.List(), null, synced: true);

        panorama.Handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_sign_in_a_person_is_told_and_the_background_says_nothing()
    {
        var panorama = new FakePanorama(existing: null, signedIn: false);

        (await panorama.Publisher.PublishAsync(DashboardSample.List(), Where)).ShouldStartWith("No Panorama sign-in works", Case.Sensitive);
        (await panorama.Publisher.UpdateAsync(DashboardSample.List(), Where)).ShouldBeNull();
        panorama.Handler.Bodies.ShouldBeEmpty();
    }

    /// <summary>Panorama with one wiki page (or none) in the dashboard's folder, and a publisher pointed at it.</summary>
    private sealed class FakePanorama
    {
        public FakePanorama(string? existing, bool signedIn = true)
        {
            Handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/_webdav/" => signedIn ? Json("""{"files":[]}""") : new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "/login-whoami.api" => WhoAmI(),
                "/MacCoss/LabOps/wiki-edit.view" => existing is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Editor(existing)) },
                "/MacCoss/LabOps/wiki-saveWiki.api" => Json("""{"success":true,"wikiProps":{"pageVersionId":18200}}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
            var store = new Store();
            store.Entries["PanoramaBridge:https://panoramaweb.org"] = new StoredCredential("apikey", "k");
            Publisher = new DashboardPublisher(new PanoramaSignIn(store, Server), new WorkTracker(NullLogger<WorkTracker>.Instance),
                NullLogger<DashboardPublisher>.Instance) { Handler = Handler, Today = () => Today };
        }

        public StubHandler Handler { get; }

        public DashboardPublisher Publisher { get; }

        /// <summary>Panorama's wiki editor, holding the page's body as a JavaScript string, as wiki-edit.view does.</summary>
        private static string Editor(string body)
        {
            var js = body.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal).Replace("<", "\\x3C", StringComparison.Ordinal)
                .Replace(">", "\\x3E", StringComparison.Ordinal);
            return $$"""
                <script type="text/javascript">
                    LABKEY._wiki.setProps({
                        entityId: '9e9d2b09-a24f-103f-94c2-1cc91aa92d00',
                        rowId: 4400,
                        name: 'default',
                        title: 'Lab projects',
                        body: '{{js}}',
                        parent: null,
                        pageVersionId: 18161,
                        rendererType: 'HTML',
                        webPartId: 0,
                        showAttachments: true,
                        shouldIndex: true,
                        isDirty: false,
                        useVisualEditor: true
                    });
                </script>
                """;
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage WhoAmI()
        {
            var response = Json("""{"id":1694,"displayName":"someone","CSRF":"t0k3n"}""");
            response.Headers.Add("Set-Cookie", "JSESSIONID=abc123; Path=/; Secure; HttpOnly");
            response.Headers.Add("Set-Cookie", "X-LABKEY-CSRF=t0k3n; Path=/; Secure");
            return response;
        }
    }

    private sealed class Store : ICredentialStore
    {
        public Dictionary<string, StoredCredential> Entries { get; } = [];

        public StoredCredential? Read(string target) => Entries.TryGetValue(target, out var c) ? c : null;

        public void Write(string target, StoredCredential credential, string comment) => Entries[target] = credential;

        public void Delete(string target) => Entries.Remove(target);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>The JSON of each POST.</summary>
        public List<JsonElement> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies.Add(JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
            }

            return respond(request);
        }
    }
}
