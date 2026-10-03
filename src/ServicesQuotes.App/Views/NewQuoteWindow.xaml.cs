using System.Windows;
using ServicesQuotes.App.ViewModels;
using ServicesQuotes.Core.Infrastructure;

namespace ServicesQuotes.App.Views;

public partial class NewQuoteWindow : Window
{
    private readonly NewQuoteViewModel _vm = new();

    private NewQuoteWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    /// <summary>Shows the form; returns the filled-in request, or null if cancelled.</summary>
    public static NewQuoteViewModel? Ask(Window? owner)
    {
        var window = new NewQuoteWindow { Owner = owner };
        return window.ShowDialog() == true ? window._vm : null;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsComplete)
        {
            MessageBox.Show(this, "Paste the request email, give a Gmail search, or at least fill in the requester and the number of samples.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
