using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Claude;
using LabOps.Core.GitHub;
using LabOps.Core.Infrastructure;
using LabOps.Core.Processes;
using LabOps.Core.Panorama;
using LabOps.Core.Projects;
using LabOps.Core.Protocols;
using LabOps.Core.Quotes;
using LabOps.Core.Repositories;
using LabOps.Core.Setup;
using Velopack;

namespace LabOps.App;

/// <summary>Explicit entry point, so Velopack runs before anything else (see the csproj).</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must be first. On an install, update or uninstall hook this call never returns.
        VelopackApp.Build().OnRestarted(_ => UpdateService.RestartedByInstaller = true).Run();

        // LABOPS_DATA points the app at another data folder, so a developer can run a
        // build against a scratch clone without touching their own settings.
        var dataOverride = Environment.GetEnvironmentVariable("LABOPS_DATA");
        var paths = new AppPaths(dataOverride);
        paths.EnsureCreated();

        // The installer starts the app in its program folder, and every program the app opens (a
        // protocol's page in the browser, a quote's PDF or spreadsheet, Claude's sign-in) would
        // inherit that as its working folder. One left open keeps the folder in use, so the next
        // update cannot replace it. The data folder is never replaced.
        Environment.CurrentDirectory = paths.Root;

        // The first start after the rename from ChargeState takes over its settings.
        var adopted = false;
        if (string.IsNullOrEmpty(dataOverride))
        {
            try
            {
                adopted = paths.AdoptLegacy(AppPaths.LegacyRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Setup asks for what it needs instead; nothing is lost.
                System.Diagnostics.Debug.WriteLine($"ChargeState's settings were not taken over: {ex.Message}");
            }
        }

        var acquired = AcquireInstance(paths);
        if (acquired is null)
        {
            return 0;
        }

        using var instance = acquired;
        Serilog.Log.Logger = LoggingSetup.Create(paths);

        try
        {
            Serilog.Log.Information("Starting {Product} {Version} ({Rid}); data directory {Root}",
                AppInfo.ProductName, AppInfo.InformationalVersion, AppInfo.RuntimeIdentifier, paths.Root);
            if (adopted)
            {
                Serilog.Log.Information("Took over ChargeState's settings from {Legacy}.", AppPaths.LegacyRoot);
            }

            using var services = BuildServiceProvider(paths);
            var app = new App(services, instance);
            app.InitializeComponent();
            var code = app.Run();

            // The window is gone. Whatever happens while the services are disposed, the process
            // ends within seconds, so it can never linger holding the single-instance lock.
            ExitWatchdog.Arm(code, TimeSpan.FromSeconds(10));
            return code;
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
    /// This process's claim to be the one running copy, or null when another copy is running
    /// and has come forward (or the user chose not to replace it).
    /// </summary>
    /// <remarks>
    /// A running copy answers from its UI thread. One that does not answer is stuck, for example
    /// a copy that did not finish closing; the user can end it and start fresh instead of facing
    /// an app that silently will not open.
    /// </remarks>
    private static SingleInstance? AcquireInstance(AppPaths paths)
    {
        var instance = SingleInstance.Acquire(AppInfo.FolderName, paths.InstanceLockFile);
        if (instance.IsFirst)
        {
            return instance;
        }

        if (instance.SignalExisting(TimeSpan.FromSeconds(5)))
        {
            instance.Dispose();
            return null;
        }

        var stuck = instance.ExistingProcessId();
        instance.Dispose();
        var answer = MessageBox.Show(
            $"{AppInfo.ProductName} is already running but is not responding; it may not have finished closing.\n\n"
            + "End it and start again? Anything it was in the middle of is lost.",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return null;
        }

        SingleInstance.EndStuckCopy(stuck);
        instance = SingleInstance.Acquire(AppInfo.FolderName, paths.InstanceLockFile);
        if (instance.IsFirst)
        {
            return instance;
        }

        instance.Dispose();
        MessageBox.Show(
            $"The other copy of {AppInfo.ProductName} could not be ended. End LabOps.exe in Task Manager, then start it again.",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
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
        services.AddSingleton<ProjectEngine>();
        services.AddSingleton<ProtocolEngine>();
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        // LABOPS_PANORAMA points Panorama browsing at another server, for testing.
        services.AddSingleton(provider => new PanoramaSignIn(provider.GetRequiredService<ICredentialStore>(),
            Environment.GetEnvironmentVariable("LABOPS_PANORAMA") is { Length: > 0 } server ? new Uri(server) : PanoramaPaths.DefaultServer));
        services.AddSingleton<PanoramaPicker>();
        services.AddSingleton<WikiPublisher>();
        // Each open repository gets its own git client and sync service from the factory.
        services.AddSingleton(provider => new RepositoryFactory(
            provider.GetRequiredService<IProcessRunner>(), provider.GetRequiredService<ToolLocator>(),
            provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<GitHubCli>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<AppTools>();
        services.AddSingleton<PermissionMemory>();
        services.AddSingleton<ClaudeLauncher>();
        services.AddSingleton<ClaudeLogin>();
        services.AddSingleton<ClaudeSignInFlow>();
        services.AddSingleton<QuoteSearch>();

        services.AddSingleton<UpdateService>();
        services.AddSingleton<Workspace>();
        services.AddSingleton<WorkTracker>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ProjectsViewModel>();
        services.AddSingleton<ProtocolsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
