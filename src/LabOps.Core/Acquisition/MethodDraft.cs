using System.Text;

namespace LabOps.Core.Acquisition;

/// <summary>
/// An instrument method taken out of a raw file, written to a scratch folder for the protocols'
/// import: the method itself (.meth) and its settings as Markdown (<see cref="MethodSummary"/>),
/// from which Claude writes an LC-MS protocol.
/// </summary>
/// <param name="Folder">The scratch folder; delete it once the files are imported.</param>
/// <param name="MethodFile">The method's file name (Actis_DIA_24m.meth).</param>
public sealed record MethodDraft(string Folder, string MethodPath, string SettingsPath, string MethodFile)
{
    public static MethodDraft Write(RawMethodCapture capture, string? methodFile, string? instrument)
    {
        var name = SafeName(capture.MethodFile ?? methodFile ?? "method.meth");
        if (!name.EndsWith(".meth", StringComparison.OrdinalIgnoreCase))
        {
            name += ".meth";
        }

        var folder = Path.Combine(Path.GetTempPath(), $"labops-method-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var method = Path.Combine(folder, name);
        File.WriteAllBytes(method, capture.Method);
        var settings = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}-settings.md");
        File.WriteAllText(settings, MethodSummary.ToMarkdown(capture.Texts, name, instrument), new UTF8Encoding(false));
        return new MethodDraft(folder, method, settings, name);
    }

    /// <summary>Removes the scratch folder; one that cannot be removed now is left to the system's temp cleanup.</summary>
    public void Delete()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    /// <summary>A name the file system takes: the method's name as the instrument computer gave it, less any path.</summary>
    private static string SafeName(string name)
    {
        var leaf = name[(name.LastIndexOfAny(['\\', '/']) + 1)..].Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. leaf.Select(c => invalid.Contains(c) ? '_' : c)]);
        return safe.Length == 0 || safe.Trim('.').Length == 0 ? "method.meth" : safe;
    }
}
