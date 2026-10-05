# How it works, step by step

These follow the main actions through the app, the engines, git, GitHub, Claude and Panorama.
[How LabOps fits together](architecture.md) introduces the pieces.

- [Saving a change](#saving-a-change)
- [Staying in sync, and what happens on a conflict](#staying-in-sync-and-what-happens-on-a-conflict)
- [A conversation with Claude](#a-conversation-with-claude)
- [Organizing a collaborator's sample sheet](#organizing-a-collaborators-sample-sheet)
- [Choosing a Panorama folder or notebook](#choosing-a-panorama-folder-or-notebook)
- [A protocol, from upload to bench](#a-protocol-from-upload-to-bench)
- [First-run setup](#first-run-setup)

## Saving a change

Every button that changes something works the same way: make sure the copy is current, run the
engine, commit on this computer and show the result, then share with GitHub in the background.
Here, marking a step done in the Projects area:

```mermaid
sequenceDiagram
    actor You
    participant App as LabOps
    participant Git as git, in the clone
    participant Engine as project.py
    participant GitHub
    participant Actions as GitHub Actions

    You->>App: Done on "Plate layout", with a date and a note
    opt not synced with GitHub in the last minute
        App->>Git: fetch, rebase --autostash origin/main
    end
    App->>Engine: uv run --frozen python scripts/project.py --json stage MNRF-BioTRACK plate_layout done ...
    Engine-->>App: JSON: the project, its steps and any warnings
    App->>Git: add -A projects/UW-MacCoss/MNRF-BioTRACK
    App->>Engine: check --staged (LabOps-Projects only)
    alt the check finds identifying information
        Engine-->>App: ERROR problems
        App->>Git: unstage, so nothing is committed
        App-->>You: Not saved, and why
    else nothing to stop it
        App->>Git: commit "MNRF-BioTRACK: plate layout done"<br/>(the hook is told which tree was just checked)
        App->>Engine: list (reload the screen)
        App-->>You: The step shows as done
        Note over App,GitHub: In the background, while you carry on
        App->>Git: fetch, rebase --autostash origin/main
        App->>GitHub: push HEAD:main (rebases and retries up to 3 times if GitHub moved)
        GitHub->>Actions: check: engine tests and validation
        GitHub->>Actions: index: rebuild the README table, commit it as github-actions
    end
```

- **The engine does the work.** The app passes what you chose; the engine edits the YAML, keeps
  its comments, and validates the result.
- **You see the change before GitHub does.** Sharing takes about 3 seconds (a fetch, a rebase
  and a push), so it happens after the screen updates. The next action, and anything Claude
  does, waits for a share still running. Offline, the commit waits on this computer and the
  status bar says so; the next sync shares it.
- **No fetch right after a sync.** If the copy synced with GitHub in the last minute (usually
  because the previous change was just shared), the action starts at once; the rebase before
  sharing still brings in anything newer.
- **LabOps-Projects checks every commit.** The app runs `check --staged` before it commits and tells
  the repository's pre-commit hook which staged tree it checked (`LABOPS_CHECKED_TREE`). The
  hook skips only that exact tree and checks anything else, including every commit made outside
  the app. A refusal leaves the files changed but uncommitted.
- **After a Claude turn,** the app saves and waits for GitHub before saying "Saved and shared".
- **The Quotes area is the same,** without the identifier check: for example Send runs
  `quote.py send`, then saves `"<number>: sent"`.
- **Timings are in the log** (`%LOCALAPPDATA%\LabOps\logs`): each engine command, each commit
  and each sync with GitHub, with how long it took.

## Staying in sync, and what happens on a conflict

The app never merges. It rebases your commits onto GitHub's, the way the pwiz-ai repository works.
Every 5 minutes it also brings in others' work when you have nothing unsaved (and checks for an app
update every 4 hours).

```mermaid
flowchart LR
    when(["Before an action, unless synced<br/>in the last minute; after a save,<br/>in the background; every 5 minutes"]) --> fetch["Fetch GitHub's main"]
    fetch --> rebase["Put your commits<br/>on top of it (rebase)"]
    rebase --> push["Push your commits"]
    push --> done(["Up to date"])
    push -->|"someone pushed meanwhile:<br/>try again, up to 3 times"| fetch
    fetch -->|"offline"| wait(["Your commits wait<br/>until you are online"])
    rebase -->|"a conflict"| conflict(["See below"])
```

Two people working on different quotes or projects never conflict, because each item has its own
folder. When a rebase does stop on a conflict, what happens depends on the file:

```mermaid
flowchart TD
    conflict{"Which file<br/>conflicts?"}
    conflict -->|"README.md"| readme["Take GitHub's version;<br/>the index workflow rebuilds it"]
    conflict -->|"calculation.md or quote.md"| rebuild["Rebuild the quote<br/>with quote.py build"]
    readme --> resume["Continue the rebase"]
    rebuild --> resume
    conflict -->|"a file a person edits,<br/>such as quote.yaml or project.yaml"| ask{"Set your version aside<br/>and use theirs?"}
    ask -->|"No"| keep["Your commit stays on this computer,<br/>not shared, until you choose"]
    ask -->|"Yes"| aside["Your work goes to a local branch,<br/>set-aside/yyyyMMdd-HHmmss;<br/>the clone matches GitHub again"]
    aside --> redo["Claude: Redo my change.<br/>It reads the set-aside branch<br/>and makes your change again."]
```

It never guesses between two people's versions of the same file.

## A conversation with Claude

```mermaid
sequenceDiagram
    actor You
    participant App as LabOps
    participant Tools as App tool server, on 127.0.0.1
    participant Claude as Claude Code
    participant Clone as Clone folder

    App->>Tools: start once per run (random port, secret token)
    You->>App: New project, New quote, or Ask Claude
    App->>Claude: start claude.exe in the clone, with stream-json,<br/>pre-approved tools, git writes forbidden,<br/>the tool server, and the app's instructions
    App->>Claude: your request, as text
    Claude->>Clone: read CLAUDE.md and the skill, edit YAML,<br/>run the engine (pre-approved)
    opt a step that is not pre-approved
        Claude->>Tools: approve(tool, input)
        Tools->>App: remembered for this run? else ask you
        App-->>Claude: allow or deny
    end
    opt Claude needs an answer
        Claude->>Tools: ask_user(question, options)
        Tools->>App: shows the question in the chat
        App-->>Claude: your answer
    end
    Claude-->>App: turn finished, with a short summary<br/>(quotes: report_quote_summary draws the quote card)
    App->>Clone: save what changed under quotes/ or projects/<br/>(the check, commit and push from "Saving a change")
    App-->>You: Saved and shared
```

- **What Claude may do without asking:** read and edit files, search, run the repository's
  engine, and look at files with `git status`, `git diff`, `git log`, `git show`, `cat`, `head`,
  `tail`, `sed -n`, `wc` and `ls`. It may never commit, push, pull, rebase, reset, check out or
  stash, and it has no web access.
- **Anything else asks you.** Choosing "Allow ... until LabOps closes" remembers that program
  for this repository until you close the app. A command that hides another one inside `$(...)` is
  asked about every time.
- **The conversation belongs to one item.** Its id is kept in `settings.json`, so Ask Claude can
  continue it later.
- **After a turn,** only changes inside `quotes/` or `projects/` are saved. In the Quotes area an
  approver is asked about changes elsewhere (the engine, templates); in the Projects area they are
  never saved. If the identifier check refuses, the app tells Claude what it found and asks it to
  fix the files.

## Organizing a collaborator's sample sheet

```mermaid
sequenceDiagram
    actor You
    participant App as LabOps
    participant Inbox as inbox/MNRF-BioTRACK/<br/>ignored by git
    participant Engine as project.py
    participant Claude as Claude Code
    participant Octopus as Octopus, in the browser

    You->>App: Organize with Claude (on Metadata organized), and choose the file
    App->>Inbox: copy the original (it never leaves this computer)
    App->>Engine: scan (column names and patterns only)
    alt names, contact details, record numbers or dates of birth
        Engine-->>App: errors
        App-->>You: Claude will not read it until those columns are removed
    else nothing identifying
        App->>Claude: organize-metadata skill, with the file and the scan's warnings
        Claude->>Engine: sheet (prints the sheet as text)
        Claude-->>App: metadata/samples.csv written, turn finished
        App->>App: check --staged, commit, push
    end
    You->>App: Open in Octopus (on Plate layout)
    App->>Engine: octopus-input (writes the CSV to inbox/)
    App->>Octopus: opens it, and you load the file and lay out plates
    You->>App: Import layout (the exported JSON)
    App->>Engine: import-layout: keeps the layout, marks the plate layout step done
```

## Choosing a Panorama folder or notebook

**Add raw data folder** (on Data deposited to Panorama) and **Add results folder** (on Signal
processing) record where an experiment's raw data and Skyline documents are on Panorama; **Add
notebook** (on Sample prep, or an experiment's first bench step) records its ELN notebook. Each
works whatever the step's status. **Browse...** finds them on Panorama, signed in the way
PanoramaBridge is.

```mermaid
sequenceDiagram
    actor You
    participant App as LabOps
    participant Creds as Credential Manager
    participant Panorama as panoramaweb.org
    participant Engine as project.py

    You->>App: Add raw data folder, Browse...
    App->>Creds: PanoramaBridge:https://panoramaweb.org, then LabOps:https://panoramaweb.org
    loop each saved sign-in, until one works
        App->>Panorama: GET /_webdav/?method=json
    end
    opt none saved, or none accepted
        App-->>You: Sign in: an API key, or a user name and password
        App->>Panorama: checked the same way
        App->>Creds: kept as LabOps:https://panoramaweb.org
    end
    alt a folder
        App->>Panorama: GET /_webdav/MacCoss/?method=json, then each folder you open
        You->>App: choose .../2026-09-BioTRACK-Quant/@files/RawFiles
    else a notebook
        App->>Panorama: GET /MacCoss/query-selectRows.api (labbook.Notebook)
        You->>App: search and choose ELN-1567-20250730-131
    end
    App->>Engine: link 2026-09-BioTRACK-DIA panorama ... --kind raw<br/>(or notebook --id ...)
    App->>App: commit and push
```

- **Read-only.** Browsing sends only `GET` requests to Panorama; what you choose is written to
  LabOps-Projects. (The wiki page, below, is the one thing written to Panorama.)
- **Folders:** raw files are in a folder's `@files` area (where PanoramaBridge uploads), and are
  recorded with that part, for example
  `/MacCoss/Collaborations/MNRF/BioTRACK/2026-09-BioTRACK-Quant/@files/RawFiles`. The folder itself,
  with the Skyline documents, is recorded as **results**.
- **Notebooks:** the number at the end of an ELN ID is the notebook's, so the link is
  `https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/131`. A notebook recorded by
  ID alone gets that link too.

## A protocol, from upload to bench

```mermaid
sequenceDiagram
    actor You
    participant App as LabOps
    participant Inbox as inbox/<id>/<br/>ignored by git
    participant Engine as protocol.py
    participant Claude as Claude Code
    participant Projects as project.py

    You->>App: New protocol: title, category, and the original (Word, PDF, LaTeX, ...)
    App->>Engine: import (extracts the text and figures)
    Engine->>Inbox: the original, text.md, images/
    App->>Claude: format-protocol skill, with where the text is
    Claude->>Engine: new, then check
    Claude-->>App: protocol.md written, corrections and questions listed, turn finished
    App->>App: check --staged, commit, push (a draft)
    You->>App: review it (Ask Claude to change it), then Publish version 1
    App->>Engine: publish: versions/v1.md, its fingerprint, the summary
    App->>App: check --staged, commit, push
    You->>App: Add protocol, on a project's Sample prep
    App->>Projects: link <project> protocol <id> --version 1 --step sample_prep
    Note over App,Projects: the step shows "Protocol: ..., version 1"; clicking it opens that version
```

A later change goes in the draft, never in a published version: Publish makes version 2, and a
project that recorded version 1 still opens exactly what it followed. The pre-commit check and the
`check` workflow refuse any change to a published version.

## First-run setup

```mermaid
flowchart TB
    subgraph programs["1. Programs"]
        direction LR
        gitStep["Git<br/>winget install"] ~~~ ghStep["GitHub CLI<br/>winget install"] ~~~ claudeStep["Claude Code<br/>claude.ai install script"]
    end
    subgraph signIns["2. Sign-ins"]
        direction LR
        ghSign["GitHub<br/>gh auth login, in the browser"] ~~~ claudeSign["Claude<br/>claude auth login"]
    end
    subgraph repos["3. Repositories and engines"]
        direction LR
        lpClone["LabOps-Projects<br/>gh repo clone, or a copy you have"] --> lpEngine["Project engine<br/>uv sync: Python and packages"]
        sqClone["LabOps-Quotes, optional<br/>only if GitHub gives access"] --> sqEngine["Quote engine<br/>uv sync"]
    end
    identity["4. Git identity<br/>your name and GitHub no-reply email, in each clone"]
    programs --> signIns --> repos --> identity
```

Each clone is set to rebase on pull with autostash, and LabOps-Projects to use its `.githooks`. Setup
runs again whenever something is missing, for example after the engines are deleted or a sign-in
expires.

## The project's wiki page

**Wiki page** on a project shows its page on Panorama as it would be published: the status, the
figures, every step with its dates, who and note, the samples, the Panorama folders with their
Skyline documents, and where the records are. Everyone who can open the folder reads it, the
collaborators of that collaboration included; a lab member decides who that is, in Panorama.

```mermaid
sequenceDiagram
    actor You
    participant App as LabOps
    participant Engine as project.py
    participant Claude as Claude Code
    participant Panorama as panoramaweb.org

    opt the first time
        You->>App: Wiki page, then the folder and page name
        App->>Engine: link MNRF-BioTRACK wiki /MacCoss/Collaborations/MNRF/BioTRACK
    end
    App->>Panorama: each results and process control folder's Skyline documents
    App->>Engine: wiki MNRF-BioTRACK --documents (the counts)
    App->>Panorama: the page as it is now (wiki-edit.view)
    App-->>You: the preview, and what Publish would do
    alt Write the text with Claude
        You->>App: Write the text with Claude
        App->>Claude: update-wiki skill (writes wiki.yaml)
        Claude-->>App: turn finished, wiki.yaml changed
        App-->>You: the preview again
    end
    You->>App: Publish
    App->>Panorama: wiki-saveWiki.api (Panorama keeps the earlier version)
    Note over App,Panorama: after that, every change saved in the app republishes the page,<br/>until its written parts change again
```

- **Kept up to date:** after each change saved in the app (a step, a link, Claude's work), the app
  rebuilds and republishes the page in the background, and says so in the status bar. It does so
  only for a page whose footer marks it as LabOps's, that nobody has edited on Panorama since,
  and only while the written parts (`wiki.yaml`) are the ones last published; new text, and a page
  edited on Panorama, wait in the Wiki page window for a person.
- **Written parts:** the summary, plan, description of the samples and a sentence per data folder
  are Claude's, in the project's `wiki.yaml`; everything else comes from the records each time.
- **A page written or edited by hand** is replaced only from the Wiki page window, after a
  question; Panorama keeps the earlier version in the page's history.
