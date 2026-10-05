using System.Windows;
using Microsoft.Win32;
using LabOps.Core.Infrastructure;
using LabOps.Core.Protocols;

namespace LabOps.App.Views;

/// <summary>What New protocol starts Claude with.</summary>
/// <param name="Category">The category's ID.</param>
/// <param name="File">The original to format, or null to work it out with the person.</param>
public sealed record NewProtocolAnswer(string Title, string Category, string? File);

/// <summary>Asks for a new protocol's title and category, and optionally the file it comes from.</summary>
public partial class NewProtocolWindow : Window
{
    private NewProtocolAnswer? _answer;

    private NewProtocolWindow(Window? owner, IReadOnlyList<ProtocolCategory> categories)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        CategoryBox.ItemsSource = categories;
        CategoryBox.SelectedItem = categories.FirstOrDefault(c => c.Id == "sample-preparation") ?? categories.FirstOrDefault();
        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>The answer, or null if cancelled.</summary>
    public static NewProtocolAnswer? Ask(Window? owner, IReadOnlyList<ProtocolCategory> categories)
    {
        var window = new NewProtocolWindow(owner, categories);
        return window.ShowDialog() == true ? window._answer : null;
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the protocol to format",
            Filter = "Protocols (*.docx;*.doc;*.pdf;*.md;*.txt;*.tex)|*.docx;*.doc;*.pdf;*.md;*.txt;*.tex|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            FileBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName).Replace('_', ' ');
            }
        }
    }

    private void OnClear(object sender, RoutedEventArgs e) => FileBox.Text = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text) || CategoryBox.SelectedItem is not ProtocolCategory category)
        {
            MessageBox.Show(this, "Give the protocol a title and a category.", AppInfo.ProductName, MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _answer = new NewProtocolAnswer(TitleBox.Text.Trim(), category.Id,
            string.IsNullOrWhiteSpace(FileBox.Text) ? null : FileBox.Text);
        DialogResult = true;
    }
}
