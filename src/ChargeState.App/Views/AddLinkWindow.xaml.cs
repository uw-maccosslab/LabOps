using System.Windows;
using System.Windows.Controls;
using ChargeState.App.Services;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App.Views;

/// <summary>A link to record.</summary>
/// <param name="PanoramaKind">raw or results for a Panorama folder; null for a notebook.</param>
/// <param name="Address">The Panorama folder or address, or the notebook's link.</param>
/// <param name="NotebookId">The notebook's ID, for a notebook.</param>
public sealed record AddLinkAnswer(string? PanoramaKind, string? Address, string? NotebookId);

/// <summary>Asks for a Panorama folder (raw data or results) or an ELN notebook, by pasting its address.</summary>
public partial class AddLinkWindow : Window
{
    private const string PanoramaHome = "https://panoramaweb.org/MacCoss/maccoss/project-begin.view";
    private const string NotebooksUrl = "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks";

    private readonly Func<Window, Task<string?>>? _browse;
    private AddLinkAnswer? _answer;

    private AddLinkWindow(Window? owner, string heading, bool panorama, Func<Window, Task<string?>>? browse)
    {
        _browse = browse;
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Message.Text = panorama
            ? "Choose the Panorama folder with Browse, paste its address from your browser, or type its path, "
              + "for example /MacCoss/maccoss/@files/2026-BioTRACK. For a notebook, paste its link and its ID."
            : "Paste the notebook's link from your browser and its ID. Panorama folders go on each experiment.";
        List<Choice> choices = panorama
            ? [new("raw", "Raw data on Panorama"), new("results", "Results on Panorama"), new(null, "ELN notebook")]
            : [new(null, "ELN notebook")];
        What.ItemsSource = choices;
        What.SelectedIndex = 0;
        What.IsEnabled = choices.Count > 1;
        Loaded += (_, _) => Address.Focus();
    }

    /// <summary>The link to add, or null if cancelled.</summary>
    /// <param name="panorama">True for an experiment, which can have Panorama folders.</param>
    /// <param name="browse">Chooses a folder on Panorama; returns its path, or null.</param>
    public static AddLinkAnswer? Ask(Window? owner, string heading, bool panorama, Func<Window, Task<string?>>? browse = null)
    {
        var window = new AddLinkWindow(owner, heading, panorama, browse);
        return window.ShowDialog() == true ? window._answer : null;
    }

    private bool IsNotebook => (What.SelectedItem as Choice)?.Kind is null;

    private void OnWhatChanged(object sender, SelectionChangedEventArgs e)
    {
        AddressLabel.Text = IsNotebook ? "Link" : "Folder";
        FindIt.Content = IsNotebook ? "Find it in the notebooks on Panorama" : "Find it on Panorama";
        Address.ToolTip = IsNotebook
            ? "The notebook's address, copied from your browser"
            : "The folder's address copied from your browser, or its path, for example /MacCoss/maccoss/2026-BioTRACK";
        var notebook = IsNotebook ? Visibility.Visible : Visibility.Collapsed;
        IdLabel.Visibility = NotebookId.Visibility = notebook;
        Browse.Visibility = IsNotebook || _browse is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        Browse.IsEnabled = false;
        try
        {
            if (await _browse!(this).ConfigureAwait(true) is { } folder)
            {
                Address.Text = folder;
            }
        }
        finally
        {
            Browse.IsEnabled = true;
        }
    }

    private void OnFindIt(object sender, RoutedEventArgs e) => Shell.Open(IsNotebook ? NotebooksUrl : PanoramaHome);

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var address = string.IsNullOrWhiteSpace(Address.Text) ? null : Address.Text.Trim();
        var id = string.IsNullOrWhiteSpace(NotebookId.Text) ? null : NotebookId.Text.Trim();
        string? problem = null;
        if (!IsNotebook && address is null)
        {
            problem = "Paste the folder's address from your browser, or type its path.";
        }
        else if (IsNotebook && address is null && id is null)
        {
            problem = "Paste the notebook's link, or give its ID.";
        }
        else if (IsNotebook && address is not null && !address.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            problem = "The link should start with https://. Put a notebook ID such as ELN-4485-20230314-179 in Notebook ID.";
        }

        if (problem is not null)
        {
            MessageBox.Show(this, problem, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _answer = new AddLinkAnswer((What.SelectedItem as Choice)?.Kind, address, IsNotebook ? id : null);
        DialogResult = true;
    }

    private sealed record Choice(string? Kind, string Label)
    {
        // What screen readers and UI Automation read for the list item.
        public override string ToString() => Label;
    }
}
