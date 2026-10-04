using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ChargeState.Core.Panorama;

namespace ChargeState.App.ViewModels;

/// <summary>A Panorama folder in the browser tree, whose subfolders load when it is opened.</summary>
/// <remarks>
/// As in PanoramaBridge's remote browser: the MacCoss project alone has dozens of folders, each
/// with its own tree, so loading everything up front would be thousands of requests.
/// </remarks>
public sealed partial class PanoramaFolderNode : ObservableObject
{
    private readonly IPanoramaFiles? _files;
    private Task? _loading;
    private bool _loaded;

    public PanoramaFolderNode(IPanoramaFiles files, string path, string name)
    {
        _files = files;
        Path = PanoramaPaths.AsFolder(path);
        Name = name;
        // A placeholder child makes the node expandable before its contents are known.
        Children.Add(new PanoramaFolderNode());
    }

    private PanoramaFolderNode()
    {
        Path = "";
        Name = "Loading...";
        IsPlaceholder = true;
    }

    public bool IsPlaceholder { get; }

    /// <summary>WebDAV path, /_webdav/MacCoss/maccoss/@files/.</summary>
    public string Path { get; }

    public string Name { get; }

    /// <summary>A folder's file area (@files), where raw data is uploaded.</summary>
    public bool IsFileArea => Name.StartsWith('@');

    public ObservableCollection<PanoramaFolderNode> Children { get; } = [];

    [ObservableProperty] public partial bool IsExpanded { get; set; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>What screen readers and UI Automation read for the tree item.</summary>
    public override string ToString() => Name;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadChildrenAsync();
        }
    }

    /// <summary>Lists the subfolders once; after a failure, opening it again tries again.</summary>
    public Task LoadChildrenAsync() =>
        _loaded ? Task.CompletedTask : _loading is { IsCompleted: false } running ? running : _loading = LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var entries = await _files!.ListAsync(Path).ConfigureAwait(true);
            Children.Clear();
            foreach (var entry in entries.Where(e => e.IsFolder && !e.Name.StartsWith('.'))
                         .OrderBy(e => e.Name.StartsWith('@') ? 0 : 1)
                         .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Children.Add(new PanoramaFolderNode(_files, entry.Path, entry.Name));
            }

            Error = null;
            _loaded = true;
        }
        catch (PanoramaException ex)
        {
            Children.Clear();
            Error = ex.Message;
        }
    }
}

/// <summary>Chooses a folder on Panorama, starting at the lab's shared folder.</summary>
public sealed partial class PanoramaBrowserViewModel(IPanoramaFiles files) : ObservableObject
{
    /// <summary>The projects this account can see.</summary>
    public ObservableCollection<PanoramaFolderNode> Roots { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChoose), nameof(Chosen))]
    public partial PanoramaFolderNode? Selected { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>A sign-in problem while loading, so the caller can offer to sign in again.</summary>
    public bool SignInFailed { get; private set; }

    public bool CanChoose => Selected is { IsPlaceholder: false };

    /// <summary>The folder as lab-projects records it: /MacCoss/maccoss/@files/2026-BioTRACK.</summary>
    public string Chosen => Selected is { IsPlaceholder: false } s ? PanoramaPaths.ToFolder(s.Path) : "";

    /// <summary>Lists the projects, then opens the way down to <paramref name="start"/> and selects it.</summary>
    public async Task InitializeAsync(string start = PanoramaPaths.StartFolder)
    {
        IsLoading = true;
        try
        {
            var entries = await files.ListAsync("/_webdav/").ConfigureAwait(true);
            Roots.Clear();
            foreach (var entry in entries.Where(e => e.IsFolder && !e.Name.StartsWith('.'))
                         .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Roots.Add(new PanoramaFolderNode(files, entry.Path, entry.Name));
            }

            await RevealAsync(start).ConfigureAwait(true);
        }
        catch (PanoramaException ex)
        {
            Error = ex.Message;
            SignInFailed = ex.IsSignInProblem;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Opens each folder on the way to a WebDAV path and selects the last one found.</summary>
    public async Task RevealAsync(string path)
    {
        var level = Roots;
        PanoramaFolderNode? found = null;
        var target = PanoramaPaths.AsFolder(path);
        foreach (var name in target.Trim('/').Split('/').Skip(1))
        {
            var next = level.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                break;
            }

            // Open it, the last one too, so its subfolders show.
            found = next;
            await next.LoadChildrenAsync().ConfigureAwait(true);
            next.IsExpanded = true;
            level = next.Children;
        }

        if (found is not null)
        {
            found.IsSelected = true;
            Selected = found;
        }
    }
}
