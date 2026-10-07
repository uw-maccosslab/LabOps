using System.Windows;
using Microsoft.Win32;
using LabOps.Core.Infrastructure;
using LabOps.Core.Protocols;

namespace LabOps.App.Views;

/// <summary>What New protocol starts Claude with.</summary>
/// <param name="Category">The category's ID.</param>
/// <param name="Files">The originals to format (documents, and method files to keep), or none to work it out with the person.</param>
public sealed record NewProtocolAnswer(string Title, string Category, IReadOnlyList<string> Files);

/// <summary>Asks for a new protocol's title and category, and optionally the file it comes from.</summary>
public partial class NewProtocolWindow : Window
{
    private NewProtocolAnswer? _answer;
    private IReadOnlyList<string> _files = [];

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
            Title = "Choose the protocol's files: its documents, and any method files the procedure runs",
            Filter = ProtocolFiles.Filter,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _files = dialog.FileNames;
            FileBox.Text = ProtocolFiles.Describe(_files);
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                var first = _files.FirstOrDefault(ProtocolFiles.IsDocument) ?? _files[0];
                TitleBox.Text = Path.GetFileNameWithoutExtension(first).Replace('_', ' ');
            }
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _files = [];
        FileBox.Text = "";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text) || CategoryBox.SelectedItem is not ProtocolCategory category)
        {
            MessageBox.Show(this, "Give the protocol a title and a category.", AppInfo.ProductName, MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _answer = new NewProtocolAnswer(TitleBox.Text.Trim(), category.Id, _files);
        DialogResult = true;
    }
}
