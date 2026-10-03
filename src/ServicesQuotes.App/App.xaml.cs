using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ServicesQuotes.App.Services;
using ServicesQuotes.App.ViewModels;
using ServicesQuotes.Core.Infrastructure;

namespace ServicesQuotes.App;

public partial class App : Application
{
    private readonly SingleInstance _instance;

    public App(ServiceProvider services, SingleInstance instance)
    {
        Services = services;
        _instance = instance;
    }

    /// <summary>The service container, for the few windows created outside it.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        _instance.ListenForSecondLaunch(() => Dispatcher.InvokeAsync(() =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }));

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // End Claude politely and stop the tool server; a few seconds at most.
        Services.GetRequiredService<ChatViewModel>().EndSessionAsync().GetAwaiter().GetResult();
        Services.GetRequiredService<Workspace>().DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Serilog.Log.Error(e.Exception, "Unhandled exception.");
        MessageBox.Show(
            $"Something went wrong: {e.Exception.Message}\n\nYour quotes are safe. Details are in the log.",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
