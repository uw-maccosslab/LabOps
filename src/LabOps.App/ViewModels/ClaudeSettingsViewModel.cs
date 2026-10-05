using CommunityToolkit.Mvvm.ComponentModel;
using LabOps.Core.Claude;
using LabOps.Core.Infrastructure;

namespace LabOps.App.ViewModels;

/// <summary>
/// Setup's choice of the model and effort Claude uses in LabOps, saved with this person's settings
/// as soon as it changes. It applies to the next conversation; one already open keeps its own.
/// </summary>
public sealed partial class ClaudeSettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly bool _loaded;

    /// <param name="settings">This person's settings, where the choices are kept.</param>
    /// <param name="save">Saves <paramref name="settings"/>.</param>
    /// <param name="claudeDefaults">The model and effort in their own Claude Code settings, if set there.</param>
    public ClaudeSettingsViewModel(AppSettings settings, Action save, (string? Model, string? Effort) claudeDefaults)
    {
        _settings = settings;
        _save = save;
        Models = ClaudeChoices.Models(settings.ClaudeModel, claudeDefaults.Model);
        Efforts = ClaudeChoices.Efforts(settings.ClaudeEffort, claudeDefaults.Effort);
        Model = ClaudeChoices.Find(Models, settings.ClaudeModel);
        Effort = ClaudeChoices.Find(Efforts, settings.ClaudeEffort);
        _loaded = true;
    }

    public IReadOnlyList<ClaudeChoice> Models { get; }

    public IReadOnlyList<ClaudeChoice> Efforts { get; }

    [ObservableProperty] public partial ClaudeChoice Model { get; set; }

    [ObservableProperty] public partial ClaudeChoice Effort { get; set; }

    partial void OnModelChanged(ClaudeChoice value)
    {
        if (_loaded)
        {
            _settings.ClaudeModel = value?.Value;
            _save();
        }
    }

    partial void OnEffortChanged(ClaudeChoice value)
    {
        if (_loaded)
        {
            _settings.ClaudeEffort = value?.Value;
            _save();
        }
    }
}
