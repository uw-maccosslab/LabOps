using System.Data;
using System.IO;
using LabOps.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LabOps.App.ViewModels;

/// <summary>One CSV file the samples window can show.</summary>
public sealed record MetadataFile(string Path, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A project's sample table in a grid, read only: the organized samples.csv, or a deidentified
/// file as it was received. Sorting is the grid's; the search keeps rows containing every word.
/// </summary>
public sealed partial class SamplesViewModel : ObservableObject
{
    private Task _loading = Task.CompletedTask;

    public SamplesViewModel(string project, string projectFolder)
    {
        Project = project;
        Files = [.. SampleTable.FilesIn(projectFolder).Select(path => new MetadataFile(path,
            System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) == "received"
                ? $"As received: {System.IO.Path.GetFileName(path)}"
                : "Sample table (samples.csv)"))];
        Query = "";
        Summary = "";
    }

    public string Project { get; }

    public string Title => $"Samples: {Project}";

    public IReadOnlyList<MetadataFile> Files { get; }

    [ObservableProperty] public partial MetadataFile? SelectedFile { get; set; }

    /// <summary>The file read; the window builds its columns from this.</summary>
    [ObservableProperty] public partial SampleTable? Table { get; set; }

    [ObservableProperty] public partial DataView? Rows { get; set; }

    [ObservableProperty] public partial string Query { get; set; }

    [ObservableProperty] public partial string Summary { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>The read of the chosen file, finished once its rows are shown.</summary>
    public Task Loading => _loading;

    /// <summary>Shows the first file: the organized table when there is one.</summary>
    public Task InitializeAsync()
    {
        SelectedFile = Files.FirstOrDefault();
        return _loading;
    }

    partial void OnSelectedFileChanged(MetadataFile? value) => _loading = LoadAsync(value);

    partial void OnQueryChanged(string value) => ApplyQuery();

    private async Task LoadAsync(MetadataFile? file)
    {
        Error = null;
        if (file is null)
        {
            Table = null;
            Rows = null;
            Summary = "";
            return;
        }

        try
        {
            var (table, data) = await Task.Run(() =>
            {
                var t = SampleTable.Read(file.Path);
                return (t, t.ToDataTable());
            }).ConfigureAwait(true);
            if (!Equals(file, SelectedFile))
            {
                return;  // another file was chosen while this one was read
            }

            Table = table;
            Rows = data.DefaultView;
            ApplyQuery();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Table = null;
            Rows = null;
            Summary = "";
            Error = $"{System.IO.Path.GetFileName(file.Path)} could not be read: {ex.Message}";
        }
    }

    private void ApplyQuery()
    {
        if (Rows is null || Table is null)
        {
            return;
        }

        Rows.RowFilter = SampleTable.SearchFilter(Query);
        Summary = Rows.Count == Table.Rows.Count ? Table.Summary : $"Showing {Rows.Count} of {Table.Summary}";
    }
}
