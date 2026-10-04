using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ChargeState.Core.Panorama;

namespace ChargeState.App.ViewModels;

/// <summary>Chooses one of the lab's ELN notebooks on Panorama, by searching its ID, title, author or status.</summary>
public sealed partial class NotebookPickerViewModel : ObservableObject
{
    private readonly IPanoramaClient _client;
    private IReadOnlyList<PanoramaNotebook> _all = [];

    public NotebookPickerViewModel(IPanoramaClient client)
    {
        _client = client;
        Query = "";
        Summary = "";
    }

    /// <summary>The notebooks matching the search, newest first.</summary>
    public ObservableCollection<PanoramaNotebook> Notebooks { get; } = [];

    [ObservableProperty] public partial string Query { get; set; }

    /// <summary>Archived notebooks are hidden unless asked for.</summary>
    [ObservableProperty] public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChoose))]
    public partial PanoramaNotebook? Selected { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty] public partial string Summary { get; set; }

    public bool CanChoose => Selected is not null;

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            _all = [.. (await _client.ListNotebooksAsync().ConfigureAwait(true)).OrderByDescending(n => n.Modified ?? DateTimeOffset.MinValue)];
            ApplyFilter();
        }
        catch (PanoramaException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnShowArchivedChanged(bool value) => ApplyFilter();

    /// <summary>Every word of the search appears in the notebook's ID, title, author or status.</summary>
    private void ApplyFilter()
    {
        var selected = Selected;
        var words = (Query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Notebooks.Clear();
        foreach (var notebook in _all.Where(n => ShowArchived || !n.Archived).Where(n =>
                 {
                     var text = string.Join(' ', n.Id, n.Title, n.Author, n.StatusText);
                     return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
                 }))
        {
            Notebooks.Add(notebook);
        }

        Selected = Notebooks.Contains(selected!) ? selected : null;
        var hidden = _all.Count(n => n.Archived && !ShowArchived);
        Summary = $"{Notebooks.Count} of {_all.Count} notebooks" + (hidden > 0 ? $"; {hidden} archived hidden" : "");
    }
}
