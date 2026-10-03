using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ChargeState.App.Services;
using ChargeState.App.ViewModels;
using ChargeState.Core.Quotes;
using ChargeState.Core.Setup;

namespace ChargeState.App.Views;

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
            services.GetRequiredService<QuoteEngine>());

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
