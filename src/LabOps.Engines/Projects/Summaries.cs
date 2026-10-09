using System.Text.Json.Nodes;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>
/// Every lab with its projects and their experiments, as the JSON LabOps reads (project.py's
/// collect, summarize_project and summarize_experiment). Its shape never depends on how loosely
/// someone wrote the YAML: the keys and their order are fixed, text is a string or null.
/// </summary>
public static class Summaries
{
    public sealed record Collected(List<JsonObject> Labs, List<(string Level, string Message)> Problems, int Hidden);

    /// <summary>A record's mapping and its problem; a file that is not UTF-8 is a problem too, not a crash.</summary>
    public static (PyDict Record, string? Problem) LoadRecord(string path)
    {
        try
        {
            return Records.Parse(YamlText.ReadText(path, Path.GetFileName(path)), Path.GetFileName(path));
        }
        catch (EngineError ex)
        {
            return (new PyDict(), ex.Message);
        }
    }

    /// <summary>
    /// collect: with activeOnly, a closed project is not validated or summarized (only its
    /// project.yaml is read, to see that it is closed).
    /// </summary>
    public static Collected Collect(ProjectRepository repo, bool activeOnly = false)
    {
        var known = repo.People();
        var problems = new List<(string, string)>();
        foreach (var (what, files) in (ReadOnlySpan<(string, IReadOnlyList<string>)>)[("project", repo.ProjectFiles()), ("experiment", repo.ExperimentFiles())])
        {
            var names = new List<(string Name, List<string> Where)>();
            foreach (var f in files)
            {
                var folder = Path.GetDirectoryName(f)!;
                var name = Path.GetFileName(folder);
                var at = names.FindIndex(n => n.Name == name);
                if (at < 0)
                {
                    names.Add((name, [repo.Rel(folder)]));
                }
                else
                {
                    names[at].Where.Add(repo.Rel(folder));
                }
            }

            problems.AddRange(names.Where(n => n.Where.Count > 1)
                .Select(n => ("ERROR", $"{what} name {n.Name} is used more than once: {string.Join(", ", n.Where)}")));
        }

        // Files of the layout before labs existed would otherwise be silently ignored.
        foreach (var f in ProjectRepository.Records(repo.Projects, 1, "project.yaml"))
        {
            problems.Add(("ERROR", $"{repo.Rel(f)} is in the older layout: the lab's file is now lab.yaml, and projects are "
                                   + "folders inside the lab (projects/<Lab>/<Project>/project.yaml)"));
        }

        foreach (var f in ProjectRepository.Records(repo.Projects, 2, "experiment.yaml"))
        {
            problems.Add(("ERROR", $"{repo.Rel(Path.GetDirectoryName(f)!)} is in the older layout: experiments are now folders inside a "
                                   + "project (projects/<Lab>/<Project>/<Experiment>/)"));
        }

        var labFiles = repo.LabFiles();
        var labsWithFiles = labFiles.Select(f => Path.GetDirectoryName(f)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pf in repo.ProjectFiles())
        {
            var lab = Path.GetDirectoryName(Path.GetDirectoryName(pf))!;
            if (!labsWithFiles.Contains(lab))
            {
                problems.Add(("ERROR", $"{repo.Rel(lab)} has projects but no lab.yaml"));
            }
        }

        var labs = new List<JsonObject>();
        var hidden = 0;
        foreach (var lf in labFiles)
        {
            var (lab, labProblem) = LoadRecord(lf);
            var labIssues = labProblem is not null ? [("ERROR", labProblem)] : Validation.ValidateLab(repo, lf, lab);
            var projects = new JsonArray();
            foreach (var pf in ProjectRepository.Records(Path.GetDirectoryName(lf)!, 1, "project.yaml"))
            {
                var (p, problem) = LoadRecord(pf);
                if (activeOnly && Equals(p["status"], "closed"))
                {
                    hidden++;
                    continue;
                }

                var issues = problem is not null ? [("ERROR", problem)] : Validation.ValidateProject(repo, pf, known, p);
                var funding = Values.FundingOf(p["funding"]);
                var experiments = new JsonArray();
                foreach (var ef in ProjectRepository.Records(Path.GetDirectoryName(pf)!, 1, "experiment.yaml"))
                {
                    var (e, eProblem) = LoadRecord(ef);
                    var eIssues = eProblem is not null ? [("ERROR", eProblem)] : Validation.ValidateExperiment(repo, ef, known, e);
                    // Work on a closed project drops out of the index and the overview, so an
                    // experiment still open there would be forgotten without a word.
                    if (eProblem is null && Equals(p["status"], "closed") && !Equals(e["status"], "closed"))
                    {
                        eIssues.Add(("WARN", "its project is closed but this experiment is not; close it too, or reopen the project"));
                    }

                    experiments.Add((JsonNode)Experiment(repo, Path.GetDirectoryName(ef)!, eIssues, funding, e));
                }

                projects.Add((JsonNode)Project(repo, Path.GetDirectoryName(pf)!, issues, experiments, p));
            }

            labs.Add(new JsonObject
            {
                ["lab"] = Path.GetFileName(Path.GetDirectoryName(lf)),
                ["folder"] = repo.Rel(Path.GetDirectoryName(lf)!),
                ["title"] = Values.Json(Values.Text(lab["title"])),
                ["pi"] = Values.Json(Values.Text(lab["pi"])),
                ["institution"] = Values.Json(Values.Text(lab["institution"])),
                ["status"] = Values.Json(Values.Text(lab["status"])),
                ["lab_contact"] = Values.Json(Values.Text(lab["lab_contact"])),
                ["analysis_repo"] = Values.Json(Values.Text(lab["analysis_repo"])),
                ["notebooks"] = Values.Json(Values.Links(lab["notebooks"], Values.NotebookFields)),
                ["issues"] = Values.Issues(labIssues),
                ["projects"] = projects,
            });
        }

        problems.AddRange(repo.ConfigProblems.Select(c => ("ERROR", c)));
        return new Collected(labs, problems, hidden);
    }

    public static JsonObject Experiment(ProjectRepository repo, string folder, IEnumerable<(string, string)> issues, Values.Funding projectFunding, PyDict e)
    {
        var own = Values.FundingOf(e["funding"]);
        var (current, stages) = Steps.Timeline(e);
        var project = Path.GetDirectoryName(folder)!;
        return new JsonObject
        {
            ["experiment"] = Path.GetFileName(folder),
            ["folder"] = repo.Rel(folder),
            ["project"] = Path.GetFileName(project),
            ["lab"] = Path.GetFileName(Path.GetDirectoryName(project)),
            ["title"] = Values.Json(Values.Text(e["title"])),
            ["status"] = Values.Json(Values.Text(e["status"])),
            ["lab_contact"] = Values.Json(Values.Text(e["lab_contact"])),
            ["instrument"] = Values.Json(Values.Text(e["instrument"])),
            ["funding"] = Values.Json(own.Type is not null ? own : projectFunding),
            ["funding_inherited"] = own.Type is null,
            ["notebooks"] = Values.Json(Values.Links(e["notebooks"], Values.NotebookFields)),
            ["panorama"] = Values.Json(Values.Links(e["panorama"], Values.PanoramaFields)),
            ["protocols"] = Values.Json(Values.Protocols(e)),
            ["analysis"] = Values.Analysis(e),
            ["current_stage"] = current,
            ["stages"] = stages,
            ["issues"] = Values.Issues(issues),
        };
    }

    public static JsonObject Project(ProjectRepository repo, string folder, IEnumerable<(string, string)> issues, JsonArray experiments, PyDict p)
    {
        var layout = p["layout"] as PyDict;
        var wiki = p["wiki"] as PyDict;
        var (current, stages) = Steps.Timeline(p);
        static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        return new JsonObject
        {
            ["project"] = Path.GetFileName(folder),
            ["folder"] = repo.Rel(folder),
            ["lab"] = Path.GetFileName(Path.GetDirectoryName(folder)),
            ["title"] = Values.Json(Values.Text(p["title"])),
            ["status"] = Values.Json(Values.Text(p["status"])),
            ["series"] = Values.Json(Values.Text(p["series"])),
            ["lab_contact"] = Values.Json(Values.Text(p["lab_contact"])),
            ["funding"] = Values.Json(Values.FundingOf(p["funding"])),
            ["human"] = PyText.Truthy(p["human"]),
            ["species"] = Values.Json(Values.Text(p["species"])),
            ["sample_type"] = Values.Json(Values.Text(p["sample_type"])),
            ["expected_samples"] = Values.JsonInt(Values.Int(p["expected_samples"])),
            ["notebooks"] = Values.Json(Values.Links(p["notebooks"], Values.NotebookFields)),
            ["protocols"] = Values.Json(Values.Protocols(p)),
            ["analysis"] = Values.Analysis(p),
            ["layout"] = layout is null ? null : new JsonObject
            {
                ["plates"] = Values.JsonInt(Values.Int(layout["plates"])),
                ["samples"] = Values.JsonInt(Values.Int(layout["samples"])),
                ["imported"] = Values.Json(Values.Text(layout["imported"])),
                ["octopus_version"] = Values.Json(Values.Text(layout["octopus_version"])),
            },
            ["wiki"] = wiki is null || Values.Text(wiki["folder"]) is not { } wikiFolder ? null : new JsonObject
            {
                ["folder"] = wikiFolder,
                ["page"] = Values.Text(wiki["page"]) ?? "default",
            },
            ["current_stage"] = current,
            ["stages"] = stages,
            ["files"] = new JsonObject
            {
                ["samples"] = Exists(Path.Combine(folder, "metadata", "samples.csv")),
                ["layout"] = Exists(Path.Combine(folder, "layout", "octopus-layout.json")),
                ["wiki"] = Exists(Path.Combine(folder, Validation.WikiFile)),
            },
            ["issues"] = Values.Issues(issues),
            ["experiments"] = experiments,
        };
    }
}
