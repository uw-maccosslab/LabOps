using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ChargeState.App.Services;
using ChargeState.App.ViewModels;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App;

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

            // Answered from the UI thread, so a copy whose UI is stuck does not seem alive.
            _instance.Acknowledge();
        }));

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The window's closing has already ended Claude and stopped the tool server. Nothing here
        // may wait on async work: it would need this (UI) thread, which is the one waiting. If the
        // app is going without its window closing (Windows signing out), end Claude's process
        // outright rather than leave it running.
        Services.GetRequiredService<ChatViewModel>().KillSession();
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
