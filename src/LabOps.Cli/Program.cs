using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LabOps.Engines;
using LabOps.Engines.CommandLine;
using LabOps.Engines.Projects;

namespace LabOps.Cli;

/// <summary>
/// labops: the lab's engines on the command line.
///
///   labops projects [--json] [--root FOLDER] COMMAND ...   LabOps-Projects (the commands of project.py)
///   labops --version
///
/// The repository is the clone the current folder is in (a folder holding projects/ and config/),
/// or --root. With --json a command answers in JSON, and an error comes back as
/// {"ok": false, "error": "..."} with exit code 1. A usage error exits 2.
/// </summary>
public static class Program
{
    public static string Version { get; } =
        (typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static int Main(string[] args)
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n" };
        var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        try
        {
            return Run(args, stdout, stderr, Directory.GetCurrentDirectory());
        }
        finally
        {
            stdout.Flush();
        }
    }

    private static string Usage(string? command = null) => command is null
        ? "usage: labops [--version] projects [--json] [--root FOLDER] {" + string.Join(",", ProjectsCommandLine.Names) + "} ..."
        : $"usage: labops projects {command} [--json] ...";

    /// <summary>The whole command line: what it prints, and its exit code.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, string currentFolder)
    {
        if (args.Count == 0 || args[0] is "-h" or "--help")
        {
            (args.Count == 0 ? stderr : stdout).WriteLine(Usage());
            return args.Count == 0 ? 2 : 0;
        }

        if (args[0] == "--version")
        {
            stdout.WriteLine($"labops {Version}");
            return 0;
        }

        if (args[0] != "projects")
        {
            stderr.WriteLine(Usage());
            stderr.WriteLine($"labops: error: unknown area '{args[0]}' (choose from projects)");
            return 2;
        }

        // Options before the command: --json and --root. --json may also come after it, as with project.py.
        var json = false;
        string? root = null;
        var i = 1;
        for (; i < args.Count && args[i].StartsWith('-'); i++)
        {
            if (args[i] == "--json")
            {
                json = true;
            }
            else if (args[i] == "--root" && i + 1 < args.Count)
            {
                root = args[++i];
            }
            else if (args[i].StartsWith("--root=", StringComparison.Ordinal))
            {
                root = args[i]["--root=".Length..];
            }
            else if (args[i] is "-h" or "--help")
            {
                stdout.WriteLine(Usage());
                return 0;
            }
            else
            {
                stderr.WriteLine(Usage());
                stderr.WriteLine($"labops projects: error: unrecognized arguments: {args[i]}");
                return 2;
            }
        }

        var commandLine = args.Skip(i).ToList();
        var name = commandLine.Count > 0 ? commandLine[0] : null;
        if (commandLine.Skip(1).Any(a => a is "-h" or "--help"))
        {
            stdout.WriteLine(Usage(name));
            return 0;
        }

        json |= commandLine.RemoveAll(a => a == "--json") > 0;
        try
        {
            var repository = new ProjectRepository(root is not null ? Path.GetFullPath(root, currentFolder) : FindRoot(currentFolder));
            var result = ProjectsCommandLine.Execute(repository, commandLine);
            if (json)
            {
                stdout.WriteLine(result.Payload.ToJsonString(Indented));
            }
            else
            {
                foreach (var line in result.Lines)
                {
                    stdout.WriteLine(line);
                }
            }

            return result.ExitCode;
        }
        catch (UsageError ex)
        {
            stderr.WriteLine(Usage(name is not null && ProjectsCommandLine.IsCommand(name) ? name : null));
            stderr.WriteLine($"labops projects{(name is not null && ProjectsCommandLine.IsCommand(name) ? " " + name : "")}: error: {ex.Message}");
            return 2;
        }
        catch (EngineError ex)
        {
            if (json)
            {
                stdout.WriteLine(new JsonObject { ["ok"] = false, ["error"] = ex.Message }.ToJsonString(Compact));
            }
            else
            {
                stderr.WriteLine($"error: {ex.Message}");
            }

            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            if (json)
            {
                stdout.WriteLine(new JsonObject { ["ok"] = false, ["error"] = message }.ToJsonString(Compact));
            }
            else
            {
                stderr.WriteLine($"error: {message}");
            }

            return 2;
        }
    }

    /// <summary>The LabOps-Projects clone the folder is in: the nearest folder holding projects/ and config/.</summary>
    public static string FindRoot(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "projects")) && Directory.Exists(Path.Combine(dir.FullName, "config")))
            {
                return dir.FullName;
            }
        }

        throw new EngineError($"{folder} is not in a LabOps-Projects clone (no projects/ and config/ folders here or above); "
                              + "run labops from the clone, or give --root");
    }
}
