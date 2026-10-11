# The three engines

Each of LabOps's three repositories has an engine: the program that holds its rules, makes every
change a button asks for, and writes every file that is generated rather than typed. The app never
edits a record itself; it asks the engine. Claude never edits a record by hand when a command does
it; it runs the same engine. This page explains how each engine works and how the app, Claude,
the commit hooks and GitHub use it. [How LabOps fits together](architecture.md) puts them in
context, and [What LabOps does](what-labops-does.md) describes the app around them.

| | Projects | Protocols | Quotes |
|---|---|---|---|
| Repository | [LabOps-Projects](https://github.com/uw-maccosslab/LabOps-Projects) | [LabOps-Protocols](https://github.com/uw-maccosslab/LabOps-Protocols) | [LabOps-Quotes](https://github.com/uw-maccosslab/LabOps-Quotes) |
| Engine | `LabOps.Engines` in this repository, and the `labops` tool | `scripts/protocol.py` | `scripts/quote.py` |
| Language | C# (.NET 10) | Python 3.13 | Python 3.13 |
| The app runs it | In its own process | `uv run --frozen python scripts/protocol.py --json ...` | `uv run --frozen python scripts/quote.py --json ...` |
| Claude, the hook and CI run it | `labops projects ...` | `uv run python scripts/protocol.py ...` | `uv run python scripts/quote.py ...` |
| Its tests | `src/LabOps.Tests` (xUnit) | `tests/` (pytest) | `tests/` (pytest) |
| A rule change reaches people | With a LabOps release | On their next sync | On their next sync |

## What every engine has in common

```mermaid
flowchart LR
    subgraph callers["Who runs it"]
        app["LabOps<br/>buttons, lists, previews"]
        claude["Claude Code<br/>skills, in the clone"]
        hook["pre-commit hook<br/>check --staged"]
        ci["GitHub Actions<br/>check, index"]
    end

    engine{{"The engine<br/>one command per action"}}

    subgraph clone["The repository"]
        records["Records<br/>YAML, typed or edited by the engine"]
        generated["Generated files<br/>README index, quote text, PDFs, pages"]
        config["config/<br/>people, categories or instruments,<br/>app.yaml: min_app_version"]
    end

    app -->|"command line, --json"| engine
    claude -->|"the same commands"| engine
    hook --> engine
    ci --> engine
    engine -->|"reads, validates"| records
    engine -->|"edits, keeping comments"| records
    engine -->|"writes"| generated
    engine -->|"reads"| config
    engine -->|"JSON reply, or ok: false and why"| app
```

- **One command per action.** A button becomes one command line, such as
  `labops projects stage MNRF-BioTRACK plate_layout done --by jdoe`, the same line Claude would
  type. Every command takes `--json`; the reply is the result, or `{"ok": false, "error": "..."}`
  with the reason to show. Exit codes are 0 (done), 1 (refused, with a message) and 2 (the command
  line was wrong).
- **Records keep their comments.** The records are YAML that people and Claude also read and edit,
  with comments explaining each field. The engines change them line by line rather than rewriting
  them, so the comments and the order stay.
- **The rules run three times.** When a command makes a change; when anything is committed (the
  pre-commit hook runs `check --staged`, and the app runs it before its own commits); and on GitHub
  after every push (`check`, or `verify` against the commit before the push), which catches a change
  made without the hook.
- **The JSON is a contract.** The app reads what the engines print. The quotes' and protocols'
  `tests/test_contract.py` hold their side; LabOps's `src/LabOps.Tests/Fixtures/*.json`, recorded
  from the engines, hold the app's. A change to that JSON needs a matching app release and a higher
  `min_app_version`.
- **`min_app_version`** in each repository's `config/app.yaml` names the oldest LabOps allowed to
  change it. An older copy shows a banner, keeps working read-only, and offers its update.

## The projects engine

The projects engine was `scripts/project.py` in LabOps-Projects until October 2026, when it was
ported to C# and moved into this repository (`src/LabOps.Engines`, LabOps 26.10.0). It gives the
same answers the Python engine gave, byte for byte for every record it writes, which the tests hold
it to. It has no Python and no uv to install, it answers in milliseconds, and the app reads every
project in memory instead of starting a program for each list.

### Who runs it

```mermaid
flowchart LR
    subgraph labopsRepo["LabOps (this repository)"]
        engines["LabOps.Engines<br/>ProjectsEngine, the rules,<br/>YAML, sheets, wiki pages"]
        cmd["ProjectsCommandLine<br/>the command lines, parsed<br/>as project.py did"]
        cli["LabOps.Cli<br/>labops.exe, compiled ahead<br/>of time (no .NET needed)"]
        core["LabOps.Core<br/>ProjectEngine, the overview"]
        cmd --> engines
        cli --> cmd
        core --> cmd
    end

    app["The app<br/>in its own process"] --> core
    tools["labops.exe in the app's<br/>tools folder, on Claude's PATH"] -.-> cli
    claude["Claude's skills"] --> tools
    hook["LabOps-Projects<br/>.githooks/pre-commit"] --> tools
    ci["LabOps-Projects CI<br/>builds labops from the tag<br/>v{min_app_version}"] -.-> cli
```

- The app builds the same command lines the `labops` tool takes and runs them in-process, so a
  button and the tool always mean the same thing.
- The installer puts `labops.exe` in the app's `tools` folder, which the app puts first on the
  PATH of every Claude conversation. The pre-commit hook looks for `labops` on the PATH, then in
  that folder.
- LabOps-Projects' GitHub Actions build `labops` from this repository at the tag its
  `min_app_version` names, so CI checks the records with the rules everyone's app has.

### What it keeps

```mermaid
flowchart TB
    lab["projects/ClearwaterZoo-Cole/lab.yaml<br/>a lab: PI, institution, lab contact,<br/>contacts, analysis repository"]
    project["CWZ-Otter-EV/project.yaml<br/>one set of samples: funding, quotes or grants,<br/>human or not, expected samples, the samples' steps,<br/>links, wiki location, reviewed free-text columns"]
    meta["metadata/samples.csv<br/>one row per sample, deidentified<br/>metadata/received/: what the collaborator sent"]
    layout["layout/<br/>the plate layout from Octopus,<br/>with the exact input that made it"]
    wiki["wiki.yaml<br/>the written parts of the wiki page"]
    exp["2026-10-Otter-DIA/experiment.yaml<br/>one measurement: instrument, the measurement<br/>and analysis steps, Panorama folders,<br/>notebooks, protocols followed"]
    inbox["inbox/CWZ-Otter-EV/<br/>originals, ignored by git,<br/>never leave the computer"]

    lab --> project
    project --> meta
    project --> layout
    project --> wiki
    project --> exp
    inbox -.->|"scanned, then organized<br/>into"| meta
```

A step is a line under `steps:` with an id, a kind (one of eleven, such as `plate_layout` or
`data_acquisition`), a status (`pending`, `in_progress`, `done`, `skipped`), and the dates, people
and note that go with it: started, finished, by, assigned, and the planned start and finish. A new
project starts with the sample steps and a new experiment with the measurement steps; `add-step`
adds any kind anywhere (a second shipment, assay development, a second analysis).

The kinds fall into five phases, each with one color wherever a step is shown (defined in
`LabOps.Engines/Projects/Phases.cs`):

```mermaid
flowchart LR
    subgraph samples["Samples"]
        s1["Samples received"] --> s2["Metadata organized"] --> s3["Plate layout"]
    end
    subgraph prep["Preparation"]
        p1["Sample prep"] ~~~ p2["Assay development"]
    end
    subgraph acq["Acquisition"]
        a1["Data acquisition"] --> a2["Data deposited<br/>to Panorama"]
    end
    subgraph analysis["Analysis"]
        n1["Signal processing"] --> n2["Data analysis"]
    end
    subgraph results["Results"]
        r1["Results returned"]
    end
    samples --> prep --> acq --> analysis --> results

    style samples fill:#1baf7a22,stroke:#1baf7a
    style prep fill:#2a78d622,stroke:#2a78d6
    style acq fill:#4a3aa722,stroke:#4a3aa7
    style analysis fill:#e87ba422,stroke:#e87ba4
    style results fill:#00830022,stroke:#008300
```

A step of kind `other` belongs to no phase and is gray. Status is never told by a hue: finished
work is gray, and late work has a red ring.

### Inside the engine

```mermaid
flowchart TB
    subgraph commands["Commands (ProjectsEngine)"]
        read["Read<br/>list, check, scan, sheet,<br/>wiki, index"]
        write["Write<br/>new-lab, new-project, new-experiment,<br/>stage, assign, plan, add-step, remove-step,<br/>link, unlink, review,<br/>octopus-input, import-layout"]
    end

    subgraph rules["Rules"]
        validation["Validation<br/>every field of every record"]
        deid["Deidentification<br/>headers, emails, phone numbers,<br/>free text"]
        steps["Steps and Phases<br/>kinds, order, plans, late"]
        reviews["Reviews<br/>free-text columns a person read"]
    end

    subgraph files["Reading and writing files"]
        yaml["YAML<br/>read as PyYAML 1.1 did;<br/>edited line by line"]
        sheets["Sheets<br/>CSV and Excel, every sheet"]
        git["FileChecks<br/>check --staged, from git's index"]
    end

    subgraph outputs["What it produces"]
        summaries["Summaries<br/>the list the app shows"]
        wikipage["WikiPage<br/>a project's page for Panorama"]
        readme["README index<br/>open work only"]
    end

    commands --> rules
    commands --> files
    commands --> outputs
```

- **YAML as Python read it.** Records were written for PyYAML, which reads `yes` as true, `012` as
  octal and `2026-10-09` as a date. The engine types plain scalars the same way, and writes values
  quoted exactly as PyYAML would, so a record looks the same whichever engine last touched it.
- **Edits, not rewrites.** `stage`, `link` and the rest change only the lines they mean to,
  keeping every comment and the order of the fields.
- **Sheets.** `scan` and `sheet` read CSV and Excel files (`.xlsx`, `.xlsm`, every sheet, hidden
  ones too), with dates found from the cell's number format, as the Python engine did.

### Keeping identifiers out

LabOps-Projects holds only deidentified sample information. Git keeps whatever is committed, even
after a file is deleted, so the rule is enforced before anything is committed:

```mermaid
flowchart TB
    original["A collaborator's sheet<br/>copied to inbox/, which<br/>git ignores"] --> scan["scan<br/>column names and value patterns,<br/>never the values"]
    scan -->|"ERROR: names, contact details,<br/>record numbers, dates of birth"| stop(["Not read until those<br/>columns are removed"])
    scan -->|"nothing identifying"| organize["Claude organizes it<br/>(organize-metadata skill)"]
    organize --> csv["metadata/samples.csv<br/>metadata/received/*.csv"]
    csv --> check["check --staged<br/>before every commit"]
    check -->|"ERROR"| refused(["Commit refused"])
    check -->|"WARN: a free-text column"| read["A person reads it;<br/>review records that,<br/>with a fingerprint"]
    read --> committed(["Committed; warns again<br/>only if the wording changes"])
    check -->|"clean"| committed
```

The app runs `check --staged` before each of its commits and tells the repository's hook which
staged tree it checked (`LABOPS_CHECKED_TREE`); the hook checks any other tree itself, including
every commit made at a terminal.

### Its tests

- `Fixtures/engine-golden/*.json`: what `project.py` did, as tables: about 10,000 YAML values as
  PyYAML wrote them, YAML and JSON as Python read them, the line edits, and the identifier rules on
  every header in LabOps-Projects' tests.
- `Engines/Commands`: LabOps-Projects' command tests, moved here when `project.py` was removed. Each
  makes a scratch repository and runs `labops projects` in-process.
- `Engines/ParityTests` (opt-in): both engines on the real repository and on a repository of edge
  cases, compared byte for byte. The wiki page's HTML is left out, since it now shows the phase
  colors `project.py` never drew.

### Changing a rule

A projects rule changes with the app: a change in `LabOps.Engines`, with a test and a release note,
ships in a LabOps release. Once it is out, LabOps-Projects raises `min_app_version` if the
repository needs it (for example, because its skills use a new command), and its CI starts checking
with that release's engine.

## The protocols engine

`scripts/protocol.py` in LabOps-Protocols. Everything about it serves one rule: **a published
version never changes**, because it is what someone followed at the bench, and a project records
which version it used.

### What it keeps

```mermaid
flowchart TB
    protocol["protocols/s-trap-micro-digestion/protocol.yaml<br/>title, category, status, owner, authors, tags,<br/>what it applies to, and every published version<br/>with its date, who, summary and fingerprints"]
    draft["protocol.md<br/>the working text: the draft<br/>of the next version"]
    versions["versions/v1.md, v2.md<br/>published, never changed"]
    attachments["attachments/<br/>figures the text shows"]
    methods["methods/<br/>files run at an instrument,<br/>kept byte for byte"]
    sources["sources/<br/>the originals it was written from"]
    protocol --> draft
    protocol --> versions
    protocol --> attachments
    protocol --> methods
    protocol --> sources
```

### From an upload to a published version

```mermaid
stateDiagram-v2
    [*] --> Uploaded: import
    Uploaded --> Draft: Claude formats it, new
    Draft --> Active: publish version 1
    Active --> Revising: protocol.md changed
    Revising --> Active: publish version N+1
    Active --> Retired: status retired
    Retired --> Active: status active
    Uploaded: Uploaded, in inbox/
    Draft: Draft, protocol.md only
    Active: Active, a version to follow
    Revising: Active, with a draft of the next version
```

While a protocol is a draft or being revised, **Ask Claude to change it** and **Update from a file**
change `protocol.md`, never a published version.

- **`import`** copies the files into `inbox/<id>/` (ignored by git) and pulls the text and figures
  out of the documents into one `text.md`, each under its own header. Word colors and highlighting
  come through as `{yellow: ...}`, because old protocols used color for meaning. Method files (a
  KingFisher program) are kept, unread.
- **`publish`** copies `protocol.md` to `versions/v<N>.md` and records the version in
  `protocol.yaml`, with the fingerprints of its text, of the figures it shows and of the methods it
  links. Publishing is a person's decision, never Claude's.
- **`render`** draws a version (or the draft) as one printable HTML page with its figures inside
  it: what the app's preview shows and what Print opens.
- **`diff`** is Show changes: what changed from one version to another, or from the last one to the
  draft.
- **`check --staged`** (the pre-commit hook) and **`verify --base <commit>`** (CI after each push)
  refuse any change to a published version, its record, or a figure or method it uses.

### Changing a rule

A change to `protocol.py`, the templates, the config, the skills or `CLAUDE.md` reaches everyone on
their next sync. It gets a test under `tests/` and an entry in
`release-notes/RELEASE_NOTES_engine-next.md`; an engine release is tagged `engine-v{version}`.

## The quotes engine

`scripts/quote.py` in LabOps-Quotes, the private repository of the people who prepare quotes. It
prices a quote from the rate card, checks its design, and writes everything sent to a client.

### From a request to the PDF

```mermaid
flowchart LR
    yaml["quote.yaml<br/>the only file typed:<br/>client, service, samples,<br/>design, extra services,<br/>overrides, text changes"]
    rates["rates/rates.yaml<br/>the current rate card"]
    lock["rates.lock.yaml<br/>the rates when the quote<br/>was first built"]
    compute["compute<br/>every line item,<br/>in exact decimals"]
    validate["validate<br/>plates, controls,<br/>instrument, overrides"]
    text["text<br/>templates/text.yaml and<br/>services.yaml, with the<br/>quote's own changes"]

    rates -->|"first build only"| lock
    yaml --> compute
    lock --> compute
    compute --> validate
    compute --> text
    validate --> calc["calculation.md<br/>every line with its formula,<br/>and the checks' results"]
    text --> md["quote.md<br/>the quotation text<br/>(quote.md.j2)"]
    md --> pdf["number.pdf<br/>(pdf.css, the lab's fonts)"]
    compute --> xlsx["number.xlsx<br/>one sheet, live formulas,<br/>for the department"]
```

- **`build`** writes `calculation.md`, `quote.md` and a draft spreadsheet; **`pdf`** writes a draft
  PDF; **`check`** validates without writing. The drafts are not committed; `send` writes the final
  PDF and spreadsheet, which are.
- **Rates are locked.** The first build copies the rate card into the quote's `rates.lock.yaml`, so
  a later change to the rate card never reprices a quote. Re-pricing a draft means deleting the
  lock and building again.
- **The design rules:** a batch is one 96-well plate (study samples, controls and extra wells);
  controls default to 2 per 10 study samples; the instrument must be in the rate card. A quote that
  breaks one says so in `calculation.md`, and the build warns.
- **`sow`** writes a statement of work at several sample counts, in the same style.

### A quote's life

```mermaid
stateDiagram-v2
    [*] --> draft: new
    draft --> sent: send
    sent --> accepted: status accepted, with the PO
    accepted --> invoiced: status invoiced
    draft --> declined: status declined
    sent --> declined
    accepted --> declined
    sent --> revision: revise
    draft: draft, built and checked, a draft PDF to review
    sent: sent, the final PDF and spreadsheet, frozen
    revision: number-R1, a new draft from the sent quote
```

- **Sent quotes are frozen.** `send` writes the PDF that went to the client, and nothing
  regenerates it; `verify` (CI after each push) rejects any change to a sent quote, and any change
  of status that `send` or `status` did not make.
- **Statuses only move forward**, each with its date: draft, sent, accepted (purchase order
  received), invoiced; a quote can be declined at any point before it is invoiced. A change after
  sending is a revision, a new quote that starts from the old one.
- **Historical estimates** were imported from the lab's old cost workbook; `verify` holds each to
  the amounts in its tab.

### Changing a rule

As for the protocols: a change to `quote.py`, the templates or the rate card reaches everyone on
their next sync, with a test and an engine release note. Each quote keeps the rates it was built
with, so a new rate applies only to quotes built after it.

## How a change to a rule reaches people

```mermaid
flowchart TB
    subgraph python["Protocols and quotes"]
        direction LR
        pyEdit["Change the engine,<br/>with a test and a note"] --> pyPush["Push to main"] --> pySync["Everyone's next sync<br/>(within 5 minutes)"]
    end
    subgraph csharp["Projects"]
        direction LR
        csEdit["Change LabOps.Engines,<br/>with a test and a note"] --> release["LabOps release<br/>v{version}"] --> appUpdate["Each app offers the update<br/>(within 4 hours)"]
        release --> raise["LabOps-Projects raises<br/>min_app_version"]
        raise --> gate["Older copies: read-only,<br/>look for the update at once"]
        raise --> ciEngine["Its CI checks with<br/>the new engine"]
    end
```

The Python engines change by syncing, so a new rule can arrive before the app that understands a
new reply; `min_app_version` covers that case too. The projects engine changes only with the app,
so everyone with the same app version has the same rules.
