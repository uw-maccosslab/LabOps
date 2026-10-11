# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Each phase of the work had its own color, the same everywhere.** Samples (aqua), Preparation
  (blue), Acquisition (violet), Analysis (pink) and Results (green) marked every step:
  - **In the app:** an edge on each step of a project, and a square beside where each project
    stood in the list and beside each kind in Add a step.
  - **In the overview:** the board's column headings, the bars of the timeline and the
    instrument schedule, and the calendar's entries.
  - **On Panorama:** the lab dashboard, and the projects' wiki pages, where a key named the
    colors. A wiki page took them the next time its project was changed in LabOps, or was
    published from Wiki page.

  The colors were chosen to stay apart for color-blind readers too, and each always appeared
  beside a name. On the timeline, finished work was its phase's color faded (it had been gray),
  and late work had a red ring (it had been a red bar). The red of late work was darker
  everywhere, to stay apart from the pink of Analysis.

## Bug Fixes

- **No black box before a protocol's page.** The first protocol opened after LabOps started, and
  any protocol opened after none was selected, showed a black box for up to half a second while
  its page rendered: the preview's browser draws nothing for a page it loaded while hidden. The
  preview now loads its page again as it appears, and stays white until the protocol's page is
  ready.

## Performance

## Breaking Changes
