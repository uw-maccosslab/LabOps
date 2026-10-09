using LabOps.Engines.Projects;

namespace LabOps.Engines.CommandLine;

/// <summary>
/// The commands of LabOps-Projects' engine as command lines, project.py's exactly: the labops
/// tool runs them for Claude, the pre-commit hook and GitHub Actions, and the app runs the same
/// command lines in-process, so both always mean the same thing.
/// </summary>
public static class ProjectsCommandLine
{
    private sealed record Command(string Name, Positional[] Positionals, Option[] Options, Func<ProjectsEngine, Parsed, CommandResult> Run);

    private static readonly string[] Funding = ["grant", "internal", "quote"];

    private static readonly Command[] Commands =
    [
        new("list", [], [new("--active", OptionKind.Flag)], (e, a) => e.List(a.Flag("active"))),
        new("check", [], [new("--staged", OptionKind.Flag)], (e, a) => e.Check(a.Flag("staged"))),
        new("scan", [new("file")], [], (e, a) => e.Scan(a.Get("file")!, a.Get("file"))),
        new("sheet", [new("file")], [new("--sheet"), new("--rows", Integer: true)],
            (e, a) => e.Sheet(a.Get("file")!, a.Get("sheet"), (int)(a.Int("rows") ?? 0))),
        new("new-lab", [new("lab")], [new("--title", Required: true), new("--pi", Required: true), new("--institution", Required: true), new("--lab-contact")],
            (e, a) => e.NewLab(a.Get("lab")!, a.Get("title")!, a.Get("pi")!, a.Get("institution")!, a.Get("lab-contact"))),
        new("new-project", [new("lab"), new("project")],
            [new("--title", Required: true), new("--funding", Choices: Funding, Default: "internal"), new("--human", OptionKind.Flag),
             new("--lab-contact"), new("--series"), new("--species"), new("--sample-type"), new("--expected-samples", Integer: true),
             new("--quote", OptionKind.Append), new("--grant")],
            (e, a) => e.NewProject(a.Get("lab")!, a.Get("project")!, new NewProjectOptions(a.Get("title")!, a.Get("funding")!, a.All("quote"),
                a.Get("grant"), a.Flag("human"), a.Get("lab-contact"), a.Get("series"), a.Get("species"), a.Get("sample-type"), a.Int("expected-samples")))),
        new("new-experiment", [new("project"), new("name")],
            [new("--title", Required: true), new("--instrument"), new("--funding", Choices: Funding), new("--with", OptionKind.Append),
             new("--lab-contact"), new("--quote", OptionKind.Append), new("--grant")],
            (e, a) => e.NewExperiment(a.Get("project")!, a.Get("name")!, new NewExperimentOptions(a.Get("title")!, a.Get("instrument"),
                a.Get("funding"), a.All("quote"), a.Get("grant"), a.All("with"), a.Get("lab-contact")))),
        new("stage", [new("item"), new("step"), new("action", Choices: ["start", "done", "skip"])], [new("--date"), new("--by"), new("--note")],
            (e, a) => e.Stage(a.Get("item")!, a.Get("step")!, a.Get("action")!, a.Get("date"), a.Get("by"), a.Get("note"))),
        new("assign", [new("item"), new("step", '+')], [new("--to"), new("--nobody", OptionKind.Flag)],
            (e, a) => e.Assign(a.Get("item")!, a.All("step"), a.Get("to"), a.Flag("nobody"))),
        new("plan", [new("item"), new("step", '+')],
            [new("--start"), new("--finish"), new("--clear", OptionKind.Flag), new("--no-start", OptionKind.Flag), new("--no-finish", OptionKind.Flag)],
            (e, a) => e.Plan(a.Get("item")!, a.All("step"), a.Get("start"), a.Get("finish"), a.Flag("clear"), a.Flag("no-start"), a.Flag("no-finish"))),
        new("add-step", [new("item"), new("kind")], [new("--label"), new("--after"), new("--before"), new("--id"), new("--assigned")],
            (e, a) => e.AddStep(a.Get("item")!, a.Get("kind")!, a.Get("label"), a.Get("after"), a.Get("before"), a.Get("id"), a.Get("assigned"))),
        new("remove-step", [new("item"), new("step")], [], (e, a) => e.RemoveStep(a.Get("item")!, a.Get("step")!)),
        new("link", [new("item"), new("what", Choices: ["panorama", "notebook", "wiki", "protocol"]), new("value", '?')],
            [new("--kind", Choices: ["qc", "raw", "results"]), new("--id"), new("--page"), new("--version", Integer: true), new("--step"), new("--title")],
            (e, a) => e.Link(a.Get("item")!, a.Get("what")!, new LinkOptions(a.Get("value"), a.Get("kind"), a.Get("id"), a.Get("page"),
                a.Int("version"), a.Get("step"), a.Get("title")))),
        new("unlink", [new("item"), new("what", Choices: ["panorama", "notebook", "wiki", "protocol"]), new("value", '?')],
            [new("--step"), new("--all", OptionKind.Flag)],
            (e, a) => e.Unlink(a.Get("item")!, a.Get("what")!, a.Get("value"), a.Get("step"), a.Flag("all"))),
        new("review", [new("item"), new("file"), new("column", '*')], [new("--by", Required: true)],
            (e, a) => e.Review(a.Get("item")!, a.Get("file")!, a.All("column"), a.Get("by")!)),
        new("wiki", [new("project")], [new("--documents"), new("--out"), new("--date")],
            (e, a) => e.Wiki(a.Get("project")!, a.Get("documents"), a.Get("out"),
                a.Get("date") is { } d ? ProjectsEngine.ParseDate(d) : null)),
        new("octopus-input", [new("project")], [], (e, a) => e.OctopusInput(a.Get("project")!)),
        new("import-layout", [new("project"), new("layout")], [new("--input"), new("--step"), new("--by")],
            (e, a) => e.ImportLayout(a.Get("project")!, a.Get("layout")!, a.Get("input"), a.Get("step"), a.Get("by"))),
        new("index", [], [], (e, _) => e.Index()),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. Commands.Select(c => c.Name)];

    public static bool IsCommand(string name) => Commands.Any(c => c.Name == name);

    /// <summary>
    /// Runs one command line (the command's name, then its arguments, without --json) on a clone.
    /// </summary>
    /// <exception cref="UsageError">The command line is not one the command takes.</exception>
    /// <exception cref="EngineError">The command refused, with a message for the user.</exception>
    public static CommandResult Execute(ProjectRepository repository, IReadOnlyList<string> commandLine)
    {
        if (commandLine.Count == 0 || Commands.FirstOrDefault(c => c.Name == commandLine[0]) is not { } command)
        {
            throw new UsageError(commandLine.Count == 0
                ? "the following arguments are required: command"
                : $"argument command: invalid choice: '{commandLine[0]}' (choose from {string.Join(", ", Names)})");
        }

        var parsed = Arguments.Parse([.. commandLine.Skip(1)], command.Positionals, command.Options);
        return command.Run(new ProjectsEngine(repository), parsed);
    }
}
