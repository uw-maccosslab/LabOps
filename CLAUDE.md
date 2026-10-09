# LabOps app: guide for AI-assisted development

A Windows desktop app (.NET 10, WPF) for the MacCoss Lab, with three areas over three repositories:

- **Projects** over [LabOps-Projects](https://github.com/uw-maccosslab/LabOps-Projects) (open to the whole
  lab): labs, projects and experiments, step timelines with assignees, sample metadata, Octopus
  plate layouts, and Panorama folders and ELN notebooks chosen by browsing Panorama (read-only).
- **Protocols** over [LabOps-Protocols](https://github.com/uw-maccosslab/LabOps-Protocols) (open to the
  whole lab): the lab's protocols in one format with every version ever published, any version shown
  as the printable page, written and revised with Claude (from an uploaded file or from scratch),
  published as frozen versions, and recorded at their version on a project's steps.
- **Quotes** over [LabOps-Quotes](https://github.com/uw-maccosslab/LabOps-Quotes) (private, for
  the people who prepare quotes): lists and searches quotes, runs the quote engine as buttons.

It keeps each clone in sync with GitHub and hosts Claude Code in a chat pane. Built on the same
foundations as PanoramaBridge. [docs/architecture.md](docs/architecture.md) and
[docs/flows.md](docs/flows.md) explain the design with figures; keep them current when a flow
they draw changes.

Write American English and do not use em dashes, in code, comments, docs, commit messages and UI
text alike.

## The division of labor

- **Each repository owns its records, text and skills; the projects engine is ours now.**
  Quotes: `scripts/quote.py`, its `CLAUDE.md`, and the skills `new-quote` and `revise-quote`.
  Protocols: `scripts/protocol.py`, its `CLAUDE.md`, and the skills `format-protocol` and
  `revise-protocol`. Projects: its `CLAUDE.md`, templates, `config/` and the skills
  `new-experiment`, `organize-metadata` and `update-experiment`; its engine is `LabOps.Engines`, the
  C# port of `scripts/project.py` (October 2026), so a projects rule changes with an app release.
  Each repository has `config/app.yaml` (`min_app_version`; the quotes also `approvers`).
- **The projects engine is one engine, three ways in.** The app runs it in-process
  (`ProjectEngine`, which builds `project.py`'s command lines and hands them to
  `ProjectsCommandLine`); Claude's skills, the pre-commit hook and GitHub Actions run the same
  command lines as the `labops` tool (`labops projects stage ...`), which the app ships in its
  `tools\` folder. Records it writes must stay byte for byte what `project.py` wrote (see the
  engine tests below). LabOps-Projects no longer has `project.py` (its CI builds `labops` from the
  LabOps tag its `min_app_version` names).
- **The app never computes a price.** It runs `uv run --frozen python scripts/<engine>.py --json ...`
  for quotes and protocols (`QuoteEngine`, `ProtocolEngine`) and shows what comes back.
- **One `Repository` per clone.** `RepositoryProfile` holds what differs (GitHub name, engine,
  root folder and item depth, generated files, whether commits are checked);
  `RepositoryFactory` gives each open clone its own `GitClient` and `SyncService`. Never share a
  git client between repositories. `Workspace` holds the open ones; any may be missing.
- **Nothing identifying reaches LabOps-Projects' history.** Its `SyncService` runs the
  `IPreCommitCheck` (the engine's `check --staged`) before every commit and refuses on an error;
  clones also get `core.hooksPath=.githooks`. Originals stay in its git-ignored `inbox/`, and the
  app scans a collaborator's file before Claude may read it.
- **A published protocol version never changes.** LabOps-Protocols' `SyncService` runs
  `protocol.py check --staged` before every commit, which refuses a change to a published
  version's text, its record or a figure it shows; `protocol.py publish` is the only way a version
  is made. A project records a protocol at its version (`project.py link <item> protocol`).
- **Claude edits files; the app alone commits and pushes.** Claude is denied every git command
  that changes history (`ClaudeLauncher.DisallowedTools`). A conversation belongs to one repository
  (`ChatTurnResult.Repository`), and its turn's changes are saved there.
- **Sync is rebase-only, like pwiz-ai** (`SyncService`): commit to main, rebase before pushing,
  never merge or force-push. Generated files (`calculation.md`, `quote.md`, the README index) are
  rebuilt rather than merged; a conflict in anything a person edits stops the sync, and
  `SetAsideAsync` keeps the user's version on a local `set-aside/...` branch.

## Layout

```
Directory.Build.props        single <Version> (CalVer YY.feature.patch), warnings as errors
global.json                  .NET 10 SDK; opts dotnet test into Microsoft Testing Platform
release-notes/               one file per version; becomes the GitHub Release body
src/LabOps.Core/     all logic, no UI types                     net10.0
  Claude/                    stream-json session, parser, in-app MCP server (AppTools), PermissionMemory
  Engines/                   what both engines share: uv run, JSON answers, EngineException
  Projects/                  ProjectEngine (runs LabOps.Engines in-process), lab, project and experiment models
  Protocols/                 ProtocolEngine (protocol.py), protocol models
  Panorama/                  read-only Panorama client, sign-in (PanoramaBridge's, then ours)
  Quotes/                    QuoteEngine (quote.py), RepoConfig, QuoteSearch
  Repositories/              RepositoryProfile, Repository, RepositoryFactory
  Sync/                      GitClient, SyncService, ItemHistory (the Modified column)
  Setup/, GitHub/            first-run checks; the gh CLI
src/LabOps.Engines/  the lab's rules, no UI and no ASP.NET      net10.0 (AOT-compatible)
  Python/                    Python's str, repr, ==, truth, float repr, json.dumps and json.loads
  Yaml/                      PyYAML 1.1 reading (YamlLoader) and yaml.dump's quoting (YamlEmitter); line edits
  Projects/                  the port of project.py: validation, identifiers, sheets, summaries, wiki page, commands
  CommandLine/               argparse-compatible command lines (ProjectsCommandLine), for the tool and the app
src/LabOps.Cli/      the labops tool, published Native AOT        net10.0
src/LabOps.App/      WPF shell (MVVM with CommunityToolkit.Mvvm) net10.0-windows
src/LabOps.Tests/    xUnit v3 + Shouldly                        net10.0-windows
tools/engine-golden/         make_golden.py and make_workbooks.py: the Python engine's behavior, as test tables
docs/                        how the app, repositories, engines, Claude and Panorama fit together
```

## Building, testing, running

```bash
dotnet build LabOps.sln -c Debug
dotnet test --project src/LabOps.Tests/LabOps.Tests.csproj
```

- `SERVICES_QUOTES_REPO=<clone of LabOps-Quotes>` also runs the real quote engine in a test,
  `LAB_PROJECTS_REPO=<clone of LabOps-Projects>` the engine on its real records (and, at a
  commit that still has `scripts/project.py`, such as 7bb1432, the parity tests), and
  `LAB_PROTOCOLS_REPO=<clone of LabOps-Protocols>` the real protocol engine.
- **The projects engine is tested against the Python one it replaced.**
  - `Fixtures/engine-golden/*.json` hold what `project.py` did: about 10,000 YAML scalars as
    `yaml.dump` wrote them, YAML and JSON as PyYAML and `json.loads` read them (problems worded as
    libyaml words them), the line edits, and the identifier rules on every header in
    LabOps-Projects' tests. `tools/engine-golden/make_golden.py` records them; run it again only to
    add cases.
  - `Engines/ParityTests` and `ParityWriteTests` (opt-in: `LAB_PROJECTS_REPO` and uv) run both
    engines on the real clone, on a repository of edge cases, on generated workbooks, on staged
    commits, and through a 90-command write scenario, comparing JSON, text, exit codes and every
    file byte for byte.
  - `Engines/Commands` holds LabOps-Projects' command tests, moved here from its pytest suite when
    `project.py` was removed: `TestRepo` makes a scratch repository from `Fixtures/projects-repo`
    (the templates with made-up examples) and runs `labops projects` in-process
    (`LabopsCommandLine.Run`), as the tool does.
  - Where the C# engine deliberately differs (Python crashed, or a line edit was wrong),
    `Engines/EngineFixTests` and `YamlEditTests` say so; anything else that differs is a bug.
- `Fixtures/project-*.json` were recorded from the real `project.py`; they test the app's reading
  of the engine's JSON, which the C# engine keeps unchanged. `Fixtures/protocol-*.json` were
  recorded from the real `protocol.py` (its `tests/test_contract.py` guards that side).
- **Publishing the labops tool** compiles it ahead of time, which needs Visual Studio's C++ build
  tools (release.yml's runner has them). In Git Bash, put
  `/c/Program Files (x86)/Microsoft Visual Studio/Installer` on the PATH first, or the link step
  cannot find vswhere. A development build of the app copies the tool's ordinary build into
  `tools\` instead.
- The sync tests run real git against a temporary bare repository.
- CI (`ci.yml`) builds and runs every test on Windows for each push; `release.yml` runs them again
  before packaging, so a failing test stops a release. The real-engine test runs in CI only when a
  read-only deploy key for the quotes repository is stored as the `QUOTES_REPO_DEPLOY_KEY` secret.
  To set it up once (the private key goes straight into the secret and is then deleted):

  ```bash
  ssh-keygen -t ed25519 -N "" -C "LabOps CI (read-only)" -f labops-ci
  gh repo deploy-key add labops-ci.pub --repo uw-maccosslab/LabOps-Quotes --title "LabOps CI (read-only)"
  gh secret set QUOTES_REPO_DEPLOY_KEY --repo uw-maccosslab/LabOps < labops-ci
  rm labops-ci labops-ci.pub
  ```
- The engine's side of the app's JSON contract is tested in the quotes repository
  (`tests/test_contract.py`); change the two together.
- `LABOPS_DATA=<folder>` runs the app with its settings and logs in that folder instead
  of `%LOCALAPPDATA%\LabOps`. Put a `settings.json` with `ProjectsRepositoryPath` (and
  `RepositoryPath` for quotes) pointing at scratch clones there to try the app without touching
  your own setup. It runs beside your installed copy: the single-instance lock is per data folder. A scratch clone still
  pushes to GitHub; repoint its `origin` to a local bare repository for anything that saves.
- `LABOPS_PANORAMA=<server>` points Panorama browsing at another server, for example a local
  stand-in; sign-ins are kept per server, so a test server never sees the real one's.
- Screenshot the running app with `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)`; a plain screen
  capture misses WPF and WebView2 content.

## Claude Code integration

`ClaudeSession` runs `claude --print --input-format stream-json --output-format stream-json
--verbose` as one long-lived process per conversation, with the user's own Claude login (team
plan seat, no API key). `AppToolServer` serves `AppTools` over MCP on 127.0.0.1 with a per-run
bearer token: `ask_user`, `report_quote_summary`, and `approve`, which Claude Code calls (as
`--permission-prompt-tool`) for anything outside the allowlist. A test replays a transcript
recorded from Claude Code 2.1.280 (`Fixtures/claude-read-file.jsonl`); re-record it if a CLI
update changes the stream format.

## Traps already found

- **Line endings.** A clone made with the Git for Windows default `core.autocrlf=true` checks
  files out as CRLF; switching the clone to `false` afterwards makes every file look changed, and
  the next save commits them all. Clones are made with `-c core.autocrlf=false` from the start.
- **cmd.exe quoting.** `cmd /c` strips the first and last quote of its command line, which breaks
  tool paths with spaces. Setup commands run through PowerShell with single-quoted paths.
- **WebView2 follows dark mode.** The preview page sets a light color scheme and a white
  background explicitly.
- **Disposal.** `Program.Main` disposes the container with a plain `using`, so every singleton
  must implement `IDisposable` if it implements `IAsyncDisposable`.
- **WebView2 in a hidden area.** The quote preview lives in the Quotes area, which is collapsed
  when the Projects area opens first; startup does not wait for the preview to initialize. The
  Protocols area's page view starts the same way, on its own.
- **A protocol's page is a file.** `NavigateToString` is limited to 2 MB, and a protocol's figures
  are inside its page, so the engine renders each version to a file under the app's data folder
  (`protocols/`) and the view navigates to it.
- **Engine changes made in a quote conversation.** The app saves the item's folder; files outside
  it are shared only when an approver says so. A quote built with an engine change that stays
  local fails the quotes repository's `verify` on GitHub.

## Releasing

**`release-notes/README.md` is the canonical convention**, the same one Skyline-PRISM uses. Add the
release-note entry to `release-notes/RELEASE_NOTES_next.md` **in the same commit** as any change a
user can notice; the draft is seeded with `## New Features / ## Bug Fixes / ## Performance /
## Breaking Changes`. Past tense, lead with user impact, include numbers, name buttons and
settings as the user sees them.

To release: rename the draft to `RELEASE_NOTES_v{version}.md` and **delete its empty headings** (the
file is published verbatim as the Release description), seed a fresh draft, set `<Version>`, push,
let CI go green, then push tag `v{version}`. `release.yml` runs the tests, downloads pinned uv and gh
(checked against their published SHA-256), packs with Velopack, and publishes the Release the app's
update check reads. The repository is public, so the update check works signed out; it uses the
user's gh token when there is one, for GitHub's per-account rate limit. Installed copies check at startup and every four hours.

Engine changes have their own notes and `engine-v{version}` tags in each data repository.

## House style (from PanoramaBridge)

- No emojis anywhere.
- Comments explain why, especially why an obvious alternative was rejected.
- Errors are written for a lab user, not a developer: what happened and what to do.
- Put logic in Core; keep the WPF layer thin.
- Add the release note in the same commit as any user-visible change.
