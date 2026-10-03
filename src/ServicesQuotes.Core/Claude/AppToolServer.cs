using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ServicesQuotes.Core.Infrastructure;

namespace ServicesQuotes.Core.Claude;

/// <summary>
/// Serves <see cref="AppTools"/> to Claude Code over MCP, on 127.0.0.1 only.
/// </summary>
/// <remarks>
/// <para>
/// Bound to the loopback address on a port the OS picks, and every request must carry a bearer
/// token generated for this run, so no other program on the machine (or the network) can ask
/// the user questions or approve tool calls in the app's name. The token reaches Claude Code
/// through the MCP config file, which is written to the user's own app data folder.
/// </para>
/// <para>
/// Stateless HTTP: a tool call that waits for the user (a question, a permission prompt) simply
/// holds its request open until the user answers.
/// </para>
/// </remarks>
public sealed class AppToolServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private AppToolServer(WebApplication app, Uri endpoint, string token)
    {
        _app = app;
        Endpoint = endpoint;
        Token = token;
    }

    public Uri Endpoint { get; }

    public string Token { get; }

    public static async Task<AppToolServer> StartAsync(AppTools tools, CancellationToken cancellationToken = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = [],
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new Implementation { Name = AppTools.ServerName, Version = AppInfo.InformationalVersion })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools(tools);

        var app = builder.Build();
        var expected = $"Bearer {token}";
        app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Headers.Authorization.ToString(), expected, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapMcp("/mcp");

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new AppToolServer(app, new Uri(new Uri(address), "/mcp"), token);
    }

    /// <summary>Writes the --mcp-config file for one Claude session and returns its path.</summary>
    public string WriteMcpConfig(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"mcp-{Guid.NewGuid():N}.json");
        var config = new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                [AppTools.ServerName] = new
                {
                    type = "http",
                    url = Endpoint.ToString(),
                    headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" },
                },
            },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
