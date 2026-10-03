# ChargeState app: guide for AI-assisted development

A Windows desktop app (.NET 10, WPF) for MacCoss Lab Proteomics Services quotes. It keeps a clone
of [services-quotes](https://github.com/uw-maccosslab/services-quotes) in sync with GitHub, lists
and searches quotes, runs the quote engine's commands as buttons, and hosts Claude Code in a chat
pane for drafting and changing quotes. Built on the same foundations as PanoramaBridge.

Write American English and do not use em dashes, in code, comments, docs, commit messages and UI
text alike.

## The division of labor

- **The quotes repository owns the logic.** Pricing, validation and file generation are
  `scripts/quote.py`; the rules are its `CLAUDE.md`; Claude's procedures are its
  `.claude/skills/` (`new-quote`, `revise-quote`); shared settings are `config/app.yaml`
  (`approvers`, `min_app_version`). A change there reaches every user with a sync, no app release.
- **The app never computes a price.** It runs `uv run --frozen python scripts/quote.py --json ...`
  (`QuoteEngine`) and shows what comes back.
- **Claude edits only `quote.yaml`; the app alone commits and pushes.** Claude is denied every git
  command that changes history (`ClaudeLauncher.DisallowedTools`).
- **Sync is rebase-only, like pwiz-ai** (`SyncService`): commit to main, rebase before pushing,
  never merge or force-push. Generated files (`calculation.md`, `quote.md`, the README index) are
  rebuilt rather than merged; a conflict in anything a person edits stops the sync, and
  `SetAsideAsync` keeps the user's version on a local `set-aside/...` branch.

## Layout

```
Directory.Build.props        single <Version> (CalVer YY.feature.patch), warnings as errors
global.json                  .NET 10 SDK; opts dotnet test into Microsoft Testing Platform
release-notes/               one file per version; becomes the GitHub Release body
src/ChargeState.Core/     all logic, no UI types                     net10.0
  Claude/                    stream-json session, parser, in-app MCP server (AppTools)
  Quotes/                    QuoteEngine (quote.py), RepoConfig, QuoteSearch
  Sync/                      GitClient, SyncService
  Setup/, GitHub/            first-run checks; the gh CLI
src/ChargeState.App/      WPF shell (MVVM with CommunityToolkit.Mvvm) net10.0-windows
src/ChargeState.Tests/    xUnit v3 + Shouldly                        net10.0-windows
```

## Building, testing, running

```bash
dotnet build ChargeState.sln -c Debug
dotnet test --project src/ChargeState.Tests/ChargeState.Tests.csproj
```

- `SERVICES_QUOTES_REPO=<clone of services-quotes>` also runs the real quote engine in a test.
- The sync tests run real git against a temporary bare repository.
- CI (`ci.yml`) builds and runs every test on Windows for each push; `release.yml` runs them again
  before packaging, so a failing test stops a release. The real-engine test runs in CI only when a
  read-only deploy key for the quotes repository is stored as the `QUOTES_REPO_DEPLOY_KEY` secret.
  To set it up once (the private key goes straight into the secret and is then deleted):

  ```bash
  ssh-keygen -t ed25519 -N "" -C "ChargeState CI (read-only)" -f chargestate-ci
  gh repo deploy-key add chargestate-ci.pub --repo uw-maccosslab/services-quotes --title "ChargeState CI (read-only)"
  gh secret set QUOTES_REPO_DEPLOY_KEY --repo uw-maccosslab/ChargeState < chargestate-ci
  rm chargestate-ci chargestate-ci.pub
  ```
- The engine's side of the app's JSON contract is tested in the quotes repository
  (`tests/test_contract.py`); change the two together.
- `CHARGESTATE_DATA=<folder>` runs the app with its settings and logs in that folder instead
  of `%LOCALAPPDATA%\ChargeState`. Put a `settings.json` with `RepositoryPath` pointing at a
  scratch clone there to try the app without touching your own setup. A scratch clone still
  pushes to GitHub; repoint its `origin` to a local bare repository for anything that saves.
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
update check reads. The update check authenticates with the user's gh token because this
repository is not public. Installed copies check at startup and every four hours.

Engine changes have their own notes and `engine-v{version}` tags in the quotes repository.

## House style (from PanoramaBridge)

- No emojis anywhere.
- Comments explain why, especially why an obvious alternative was rejected.
- Errors are written for a lab user, not a developer: what happened and what to do.
- Put logic in Core; keep the WPF layer thin.
- Add the release note in the same commit as any user-visible change.
