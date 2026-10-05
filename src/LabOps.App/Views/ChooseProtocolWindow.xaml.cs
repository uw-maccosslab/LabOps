using System.Windows;
using System.Windows.Controls;
using LabOps.Core.Infrastructure;
using LabOps.Core.Protocols;

namespace LabOps.App.Views;

/// <summary>The protocol and published version a step followed.</summary>
public sealed record ProtocolChoice(ProtocolSummary Protocol, int Version);

/// <summary>Picks a protocol from LabOps-Protocols and one of its published versions.</summary>
public partial class ChooseProtocolWindow : Window
{
    private readonly IReadOnlyList<Entry> _entries;
    private ProtocolChoice? _answer;

    private ChooseProtocolWindow(Window? owner, string heading, IReadOnlyList<ProtocolSummary> protocols, string? currentId, int? currentVersion)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        _entries = [.. protocols
            .Where(p => !p.IsRetired || p.Id == currentId)
            .OrderBy(p => p.IsPublished ? 0 : 1)
            .ThenBy(p => p.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new Entry(p))];
        Filter("");
        Choices.SelectedItem = _entries.FirstOrDefault(e => e.Protocol.Id == currentId);
        if (Choices.SelectedItem is Entry && currentVersion is { } v)
        {
            VersionBox.SelectedItem = VersionBox.Items.OfType<VersionEntry>().FirstOrDefault(x => x.Version == v) ?? VersionBox.SelectedItem;
        }

        Loaded += (_, _) => Search.Focus();
    }

    /// <summary>The protocol and version, or null if cancelled.</summary>
    /// <param name="currentId">The protocol the step records now, to start from.</param>
    /// <param name="currentVersion">The version it records now.</param>
    public static ProtocolChoice? Ask(Window? owner, string heading, IReadOnlyList<ProtocolSummary> protocols, string? currentId = null,
        int? currentVersion = null)
    {
        var window = new ChooseProtocolWindow(owner, heading, protocols, currentId, currentVersion);
        return window.ShowDialog() == true ? window._answer : null;
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Filter(Search.Text);

    private void Filter(string query)
    {
        var selected = Choices.SelectedItem;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Choices.ItemsSource = _entries.Where(e => words.All(w => e.Text.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        Choices.SelectedItem = selected;
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Choices.SelectedItem is not Entry entry)
        {
            VersionBox.ItemsSource = null;
            Note.Text = "";
            OkButton.IsEnabled = false;
            return;
        }

        var p = entry.Protocol;
        var versions = p.Versions.Reverse()
            .Select(v => new VersionEntry(v.Version, $"Version {v.Version}{(v.Version == p.LatestVersion ? " (current)" : "")}, {v.Date ?? "date not recorded"}"))
            .ToList();
        VersionBox.ItemsSource = versions;
        VersionBox.SelectedItem = versions.FirstOrDefault();
        OkButton.IsEnabled = versions.Count > 0;
        Note.Text = versions.Count == 0
            ? "Not published yet. Publish version 1 in the Protocols area first."
            : p.DraftChanges ? "Its draft has changes not yet published; those are not part of any version." : "";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Choices.SelectedItem is Entry entry && VersionBox.SelectedItem is VersionEntry version)
        {
            _answer = new ProtocolChoice(entry.Protocol, version.Version);
            DialogResult = true;
        }
    }

    private sealed record Entry(ProtocolSummary Protocol)
    {
        public string Title => Protocol.DisplayTitle;

        public string Detail => string.Join("   ", new[]
        {
            Protocol.Id, Protocol.CategoryLabel, Protocol.IsPublished ? $"current version {Protocol.LatestVersion}" : "not published yet",
            Protocol.IsRetired ? "retired" : null,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        public string Text => string.Join(' ', new[] { Protocol.Id, Protocol.Title, Protocol.ShortTitle, Protocol.CategoryLabel }
            .Concat(Protocol.Tags).Concat(Protocol.AppliesTo.SampleTypes).Concat(Protocol.AppliesTo.Instruments));

        public override string ToString() => Title;
    }

    private sealed record VersionEntry(int Version, string Label)
    {
        public override string ToString() => Label;
    }
}
