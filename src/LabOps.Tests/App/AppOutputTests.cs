using System.Text.Json;

namespace LabOps.Tests.App;

/// <summary>
/// The app's own folder, as the build leaves it. LabOps 26.10.0 and 26.11.0 shipped with the
/// labops tool's dependency manifest in place of the app's (Windows took labops.deps.json for
/// LabOps.deps.json), so the app could not find its libraries and did not start.
/// </summary>
public sealed class AppOutputTests
{
    private static string AppFolder()
    {
        var folder = AppContext.BaseDirectory;
        var configuration = folder.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release" : "Debug";
        while (!File.Exists(Path.Combine(folder, "LabOps.sln")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return Path.Combine(folder, "src", "LabOps.App", "bin", configuration, "net10.0-windows");
    }

    [Fact]
    public void The_app_folder_has_the_apps_own_dependency_manifest()
    {
        var folder = AppFolder();
        var manifest = Directory.EnumerateFiles(folder, "*.deps.json").ShouldHaveSingleItem();

        // The exact name, as the host looks for it, and the app's content: its own entry and its libraries.
        Path.GetFileName(manifest).ShouldBe("LabOps.deps.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var libraries = doc.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(t => t.Value.EnumerateObject()).Select(l => l.Name).ToList();
        libraries.ShouldContain(l => l.StartsWith("LabOps/", StringComparison.Ordinal));
        libraries.ShouldContain(l => l.StartsWith("Serilog/", StringComparison.Ordinal));
        libraries.ShouldNotContain(l => l.StartsWith("labops/", StringComparison.Ordinal));
        File.Exists(Path.Combine(folder, "tools", "labops.exe")).ShouldBeTrue("the tool is in tools, for Claude's sessions");
    }
}
