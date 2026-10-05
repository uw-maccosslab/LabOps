using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Projects;
using LabOps.Core.Protocols;
using LabOps.Core.Quotes;
using LabOps.Core.Setup;

namespace LabOps.App.Views;

public partial class SetupWindow : Window
{
    private SetupWindow(SetupViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.RefreshAsync();
    }

    /// <summary>Shows the setup checklist and waits until the user closes it.</summary>
    public static void Show(Window? owner, IServiceProvider services)
    {
        var vm = new SetupViewModel(
            services.GetRequiredService<SetupService>(),
            services.GetRequiredService<Workspace>(),
            services.GetRequiredService<QuoteEngine>(),
            services.GetRequiredService<ProjectEngine>(),
            services.GetRequiredService<ProtocolEngine>(),
            services.GetRequiredService<ClaudeSignInFlow>());

        var window = new SetupWindow(vm);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
