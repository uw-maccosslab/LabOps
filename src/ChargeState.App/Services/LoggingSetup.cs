using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App.Services;

/// <summary>Builds the Serilog pipeline: rolling files under %LOCALAPPDATA%\ChargeState\logs.</summary>
public static class LoggingSetup
{
    public static ILogger Create(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.With<SecretRedactingEnricher>()
            .WriteTo.File(
                path: paths.LogFileTemplate,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 16L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}

/// <summary>
/// Scrubs anything that looks like a credential out of log properties. The code never logs a
/// token on purpose (the update check and the MCP server both hold one), but a secret in a log
/// file cannot be taken back, so this is worth having twice, as in PanoramaBridge.
/// </summary>
public sealed partial class SecretRedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var name in logEvent.Properties.Keys.ToArray())
        {
            if (SensitiveName().IsMatch(name))
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, "[redacted]"));
            }
        }
    }

    [GeneratedRegex("password|secret|token|authorization|credential", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveName();
}
