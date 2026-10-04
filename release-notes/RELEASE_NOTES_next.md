# ChargeState vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

### Projects

- **A Projects area for the lab's collaborations.** ChargeState now also works on
  [lab-projects](https://github.com/uw-maccosslab/lab-projects), open to everyone in the lab. Each
  experiment shows its collaborator, funding (a quote number, a grant, or internal), ELN notebook,
  Panorama folders and analysis repository, and a timeline of nine stages from samples received to
  results returned.
- **One-click stage updates.** Start, Done, Skip and Reopen on each stage ask for the date (today
  unless changed) and an optional note, record who did it, and save and share at once.
- **New experiment** starts Claude on the lab-projects `new-experiment` skill from a short form or
  a pasted email; **Ask Claude to update it** records progress or adds links in plain words.
- **Organize metadata with Claude.** Choose a collaborator's sample sheet: it is copied to the
  experiment's `inbox/` folder (never shared), checked for identifying information, and only when
  the check finds none does Claude turn it into a `samples.csv` ready for plate layout. When it
  does find some, the app lists the columns and how to fix them, and Claude does not read the file.
- **Plate layouts with Octopus.** **Open in Octopus** writes the sample table in the form Octopus
  needs and opens it; **Import Octopus layout** keeps the exported layout with the experiment and
  marks the plate layout done. A new layout is refused once sample prep has started.
- **Nothing identifying is shared.** Every save in the projects repository first passes
  `project.py check --staged`; when it finds names, contact details, or (for human studies) full
  dates or ages over 89, nothing is committed and the app says what to fix. After a Claude
  conversation, the message box is filled in asking Claude to fix it.

### Both areas

- **Projects | Quotes tabs.** The Quotes area appears only for people with a copy of the quotes;
  Setup asks GitHub whether the account has access before offering the download, and the app works
  with the projects alone. The status bar shows the sync state of each repository.
- **Modified column, and sorting by any column.** The quote and experiment lists have a Modified
  column (when the folder last changed, from its history on GitHub, so it is the same on every
  computer; unsaved changes count as now). Click any column heading to sort by it, and again to
  reverse.

## Bug Fixes

- **Fewer permission prompts.** "Allow" with the box checked now remembers each program in the step
  (for example `uv` and `sed` in `quote.py build ... && sed -n ...`) until ChargeState closes,
  instead of only the first program and only for one conversation, which asked again for almost
  every step. Viewing files from the shell (`sed -n`, `head`, `tail`, `cat`, `wc`, `ls`) no longer
  asks at all, and Claude is told to run one command per step. A step that hides a command inside
  another (`$(...)`) is still asked about every time.
- **Engine changes no longer break GitHub's check.** When Claude changes files outside the quotes
  folder (the engine, templates or rates) while working on a quote, an approver is now asked
  whether to share them with the quote. Before, they stayed on the computer while the quote built
  with them was shared, so GitHub's check failed.

## Performance

## Breaking Changes

- **Needs the lab projects.** Setup now asks for a copy of lab-projects (or an existing one); the
  quotes become optional. Existing quotes clones are kept and found automatically.
