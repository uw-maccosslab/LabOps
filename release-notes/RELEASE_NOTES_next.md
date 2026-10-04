# ChargeState vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Each step has the buttons for its work.** The project's row of buttons is down to Ask Claude
  to update it, New experiment, Folder and On GitHub; the rest moved onto the steps they belong
  to, where what they record is shown too:
  - Metadata organized: Organize with Claude, View samples. An experiment's own metadata step,
    such as unblinded metadata, has them as well.
  - Plate layout: Open in Octopus, Import layout.
  - Sample prep: Add notebook. On an experiment, Add notebook is on assay development or data
    acquisition.
  - Data deposited to Panorama: Add raw data folder. Signal processing: Add results folder.

  They work whatever the step's status, since a Panorama folder or a notebook is often set up
  before the work starts. A timeline without the step keeps its buttons beside its heading. Add
  link now asks for one kind of link, the one its button names.
- **View samples.** A project with an organized sample table has a View samples button. It opens
  the table (`metadata/samples.csv`) in a grid: click a heading to sort (numbers sort as
  numbers, and identifiers such as 0012 keep their zeros), type to keep only the rows containing
  every word, and copy cells with their headings. It counts the rows, the study samples and the
  QC rows, and can also show each deidentified file the table was made from. It only reads, and
  the window can stay open while you work.
- **Show closed counts the closed projects** ("Show closed (12)"), which the list leaves out.

## Bug Fixes

## Performance

- **Clicks show their result about three times sooner.** A step update took about 7.4 seconds
  before the page showed it, and now takes about 2.4 (measured with GitHub's real delays: 0.6 s
  per fetch, about 1.5 s per push).
  - The change is committed on this computer and shown at once. Sharing it with GitHub (a fetch,
    a rebase and a push, about 3 seconds) happens in the background, and the status bar shows
    it. Offline, the commit waits on this computer and the next sync shares it.
  - No fetch before an action when the copy synced in the last minute, which is the usual case
    when going through several steps.
  - The list is read once per action instead of three times.
  - lab-projects' pre-commit hook no longer repeats the identifier check the app has just run on
    the same staged files (lab-projects engine change).
  - The log records how long each engine command, commit and sync took.
- After a Claude turn, the app still waits for GitHub before it says "Saved and shared".
- **The Projects list reads only the projects in progress.** Closed projects are read only
  while Show closed is ticked, or to show one that was just closed, so the list stays quick as
  finished work piles up. With lab-projects' faster `list` (same release of the project
  engine), a repository of 1,000 projects, 80% of them closed, lists in 1.8 seconds instead of
  30. A copy of lab-projects not yet synced to that engine still lists everything.

## Breaking Changes
