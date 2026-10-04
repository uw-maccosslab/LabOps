using System.Diagnostics;
using LabOps.Core.Processes;
using LabOps.Core.Repositories;
using LabOps.Core.Sync;

namespace LabOps.Tests.TestSupport;

/// <summary>
/// A bare repository standing in for GitHub, with clones standing in for two people's computers.
/// Uses the real git, because the behavior under test is git's. The seed has two quotes and an
/// experiment, so one remote serves the tests of both repository profiles.
/// </summary>
public sealed class GitFixture : IDisposable
{
    private readonly TempDirectory _root = new();

    public GitFixture()
    {
        if (new ToolLocator().Find(Tool.Git) is null)
        {
            Assert.Skip("git is not installed.");
        }

        Remote = _root.Combine("github.git");
        Git(_root.Path, "init", "--quiet", "--bare", "--initial-branch=main", Remote);

        var seed = _root.Combine("seed");
        // Cloned with autocrlf off, as setup does: a global autocrlf=true (the Git for Windows
        // default) would otherwise check files out as CRLF and make every file look changed.
        Git(_root.Path, "clone", "--quiet", "-c", "core.autocrlf=false", Remote, seed);
        Configure(seed, "Seeder");
        Write(seed, ".gitattributes", "* text=auto eol=lf\n");
        Write(seed, "README.md", "# Quotes\n\n<!-- INDEX:START -->\nnothing\n<!-- INDEX:END -->\n");
        foreach (var quote in new[] { "A", "B" })
        {
            Write(seed, $"quotes/G/2026/{quote}/quote.yaml", $"quote_number: {quote}\nstatus: draft\nsamples: 10\n");
            Write(seed, $"quotes/G/2026/{quote}/calculation.md", $"# {quote}\ntotal 100\n");
            Write(seed, $"quotes/G/2026/{quote}/quote.md", $"# {quote}\n");
        }

        Write(seed, "projects/Lab/project.yaml", "group: Lab\ntitle: A collaboration\nstatus: active\n");
        Write(seed, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nstatus: active\nsamples: 10\n");

        Git(seed, "add", "-A");
        Git(seed, "commit", "--quiet", "-m", "seed");
        Git(seed, "push", "--quiet", "origin", "HEAD:main");
    }

    public string Remote { get; }

    /// <summary>Clones the remote for one person.</summary>
    public string Clone(string person)
    {
        var path = _root.Combine(person);
        Git(_root.Path, "clone", "--quiet", "-c", "core.autocrlf=false", Remote, path);
        Configure(path, person);
        return path;
    }

    /// <summary>Syncs a clone as the quotes repository.</summary>
    public SyncService SyncFor(string clone, IGeneratedFileRebuilder? rebuilder = null)
    {
        var tools = new ToolLocator();
        var git = new GitClient(new ProcessRunner(tools), tools) { RepositoryPath = clone };
        return new SyncService(git, RepositoryProfile.Quotes, rebuilder ?? new RecordingRebuilder(clone));
    }

    /// <summary>Syncs a clone as the projects repository, whose commits pass <paramref name="check"/> first.</summary>
    public SyncService ProjectsSyncFor(string clone, IPreCommitCheck check)
    {
        var tools = new ToolLocator();
        var git = new GitClient(new ProcessRunner(tools), tools) { RepositoryPath = clone };
        return new SyncService(git, RepositoryProfile.Projects, new NoGeneratedFiles(), check);
    }

    public static void Write(string clone, string relative, string text)
    {
        var path = Path.Combine(clone, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
    }

    public static string Read(string clone, string relative) =>
        File.ReadAllText(Path.Combine(clone, relative.Replace('/', Path.DirectorySeparatorChar)));

    public static string Git(string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(new ToolLocator().Find(Tool.Git)!)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}");
        }

        return output;
    }

    public void Dispose() => _root.Dispose();

    private static void Configure(string clone, string person)
    {
        Git(clone, "config", "user.name", person);
        Git(clone, "config", "user.email", $"{person.ToLowerInvariant()}@example.org");
        Git(clone, "config", "core.autocrlf", "false");
    }
}

/// <summary>Stands in for quote.py: "rebuilding" writes a recognizable calculation.md.</summary>
public sealed class RecordingRebuilder(string clone) : IGeneratedFileRebuilder
{
    public List<string> Folders { get; } = [];

    public Task RebuildAsync(string folder, CancellationToken cancellationToken)
    {
        Folders.Add(folder);
        var yaml = GitFixture.Read(clone, $"{folder}/quote.yaml");
        GitFixture.Write(clone, $"{folder}/calculation.md", $"rebuilt from:\n{yaml}");
        GitFixture.Write(clone, $"{folder}/quote.md", "rebuilt\n");
        return Task.CompletedTask;
    }
}

/// <summary>Stands in for project.py check --staged: refuses any staged file containing a marker.</summary>
public sealed class MarkerCheck(string clone, string marker = "SECRET") : IPreCommitCheck
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<CommitProblem>> CheckStagedAsync(CancellationToken cancellationToken)
    {
        Calls++;
        var staged = GitFixture.Git(clone, "diff", "--cached", "--name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<CommitProblem> problems = staged
            .Where(f => GitFixture.Git(clone, "show", $":{f.Trim()}").Contains(marker, StringComparison.Ordinal))
            .Select(f => new CommitProblem("ERROR", $"{f.Trim()}: contains {marker}"))
            .Concat([new CommitProblem("WARN", "a warning never stops a commit")])
            .ToList();
        return Task.FromResult(problems);
    }
}
