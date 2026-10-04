using System.Windows;
using ChargeState.App.Services;
using ChargeState.Core.Infrastructure;

namespace ChargeState.App.Views;

/// <summary>A link to record.</summary>
/// <param name="PanoramaKind">raw or results for a Panorama folder; null for a notebook.</param>
/// <param name="Address">The Panorama folder or address, or the notebook's link.</param>
/// <param name="NotebookId">The notebook's ID, for a notebook.</param>
public sealed record AddLinkAnswer(string? PanoramaKind, string? Address, string? NotebookId);

/// <summary>Asks for a Panorama folder (raw data or results) or an ELN notebook, by browsing or pasting its address.</summary>
public partial class AddLinkWindow : Window
{
    private const string PanoramaHome = "https://panoramaweb.org/MacCoss/maccoss/project-begin.view";
    private const string NotebooksUrl = "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks";

    private readonly PanoramaPicker? _picker;
    private readonly string _kind;
    private AddLinkAnswer? _answer;

    private AddLinkWindow(Window? owner, string heading, string kind, PanoramaPicker? picker)
    {
        _picker = picker;
        _kind = kind;
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Message.Text = kind switch
        {
            "raw" => "The folder the raw files go to, where PanoramaBridge uploads them. Choose it with Browse, paste its address "
                + "from your browser, or type its path, for example /MacCoss/maccoss/@files/2026-BioTRACK. It can be recorded "
                + "before acquisition starts.",
            "results" => "The folder with the Skyline documents. Choose it with Browse, paste its address from your browser, "
                + "or type its path, for example /MacCoss/Collaborations/MNRF/BioTRACK/2026-09-BioTRACK-Quant.",
            _ => "Choose the notebook with Browse, or give its ID (the link is filled in from it).",
        };

        AddressLabel.Text = IsNotebook ? "Link" : "Folder";
        FindIt.Content = IsNotebook ? "Find it in the notebooks on Panorama" : "Find it on Panorama";
        Address.ToolTip = IsNotebook
            ? "The notebook's address, copied from your browser"
            : "The folder's address copied from your browser, or its path, for example /MacCoss/maccoss/2026-BioTRACK";
        IdLabel.Visibility = NotebookId.Visibility = IsNotebook ? Visibility.Visible : Visibility.Collapsed;
        Browse.Visibility = _picker is null ? Visibility.Collapsed : Visibility.Visible;
        Browse.ToolTip = IsNotebook
            ? "Choose the notebook from the lab's notebooks on Panorama"
            : "Choose the folder on Panorama, signed in the way PanoramaBridge is";
        Loaded += (_, _) => Address.Focus();
    }

    /// <summary>The link to add, or null if cancelled.</summary>
    /// <param name="kind">raw or results for a Panorama folder; notebook for an ELN notebook.</param>
    /// <param name="picker">Browses Panorama for a folder or a notebook; null hides Browse.</param>
    public static AddLinkAnswer? Ask(Window? owner, string heading, string kind, PanoramaPicker? picker = null)
    {
        var window = new AddLinkWindow(owner, heading, kind, picker);
        return window.ShowDialog() == true ? window._answer : null;
    }

    private bool IsNotebook => _kind == "notebook";

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        Browse.IsEnabled = false;
        try
        {
            if (IsNotebook)
            {
                if (await _picker!.ChooseNotebookAsync(this).ConfigureAwait(true) is { } notebook)
                {
                    Address.Text = notebook.Url();
                    NotebookId.Text = notebook.Id;
                }
            }
            else if (await _picker!.ChooseFolderAsync(this).ConfigureAwait(true) is { } folder)
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
            problem = "Choose the notebook with Browse, or give its ID.";
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

        _answer = new AddLinkAnswer(IsNotebook ? null : _kind, address, IsNotebook ? id : null);
        DialogResult = true;
    }
}
