using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using ServicesQuotes.App.Services;
using ServicesQuotes.App.ViewModels;
using ServicesQuotes.Core.Claude;
using ServicesQuotes.Core.GitHub;
using ServicesQuotes.Core.Infrastructure;
using ServicesQuotes.Core.Processes;
using ServicesQuotes.Core.Quotes;
using ServicesQuotes.Core.Setup;
using ServicesQuotes.Core.Sync;
using Velopack;

namespace ServicesQuotes.App;

/// <summary>Explicit entry point, so Velopack runs before anything else (see the csproj).</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must be first. On an install, update or uninstall hook this call never returns.
        VelopackApp.Build().Run();

        // SERVICES_QUOTES_DATA points the app at another data folder, so a developer can run a
        // build against a scratch clone without touching their own settings.
        var paths = new AppPaths(Environment.GetEnvironmentVariable("SERVICES_QUOTES_DATA"));
        paths.EnsureCreated();

        using var instance = SingleInstance.Acquire(AppInfo.FolderName, paths.InstanceLockFile);
        if (!instance.IsFirst)
        {
            instance.SignalExisting();
            return 0;
        }

        Serilog.Log.Logger = LoggingSetup.Create(paths);

        try
        {
            Serilog.Log.Information("Starting {Product} {Version} ({Rid}); data directory {Root}",
                AppInfo.ProductName, AppInfo.InformationalVersion, AppInfo.RuntimeIdentifier, paths.Root);

            using var services = BuildServiceProvider(paths);
            var app = new App(services, instance);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Startup failed.");
            MessageBox.Show(
                $"{AppInfo.ProductName} could not start.\n\n{ex.Message}\n\nThe log is in {paths.LogDirectory}",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            Serilog.Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Wires everything the window needs. Internal so a test can build it: a missing registration
    /// is a window that will not open, and nothing else would catch it.
    /// </summary>
    internal static ServiceProvider BuildServiceProvider(AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new SerilogLoggerProvider(dispose: false));
        });

        services.AddSingleton(provider => new SettingsStore(paths.SettingsFile, provider.GetRequiredService<ILogger<SettingsStore>>()));
        services.AddSingleton(provider => provider.GetRequiredService<SettingsStore>().Load());

        services.AddSingleton<ToolLocator>();
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<IProcessRunner>(provider => provider.GetRequiredService<ProcessRunner>());
        services.AddSingleton<QuoteEngine>();
        services.AddSingleton<IGeneratedFileRebuilder, EngineRebuilder>();
        services.AddSingleton<GitClient>();
        services.AddSingleton<SyncService>();
        services.AddSingleton<GitHubCli>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<AppTools>();
        services.AddSingleton<ClaudeLauncher>();
        services.AddSingleton<QuoteSearch>();

        services.AddSingleton<UpdateService>();
        services.AddSingleton<Workspace>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
