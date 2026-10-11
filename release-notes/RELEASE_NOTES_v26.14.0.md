# LabOps v26.14.0

Each phase of the work got its own color wherever a step is shown, finished work turned gray,
and the protocol preview stopped flashing a black box before its first page.

## New Features

- **Each phase of the work had its own color, the same everywhere.** Samples (aqua), Preparation
  (blue), Acquisition (violet), Analysis (pink) and Results (green) marked every step not yet
  done, and finished work was gray:
  - **In the app:** an edge on each step of a project, and a square beside where each project
    stood in the list and beside each kind in Add a step. A finished step's check mark was gray
    (it had been green).
  - **In the overview:** the board's column headings, the bars of the timeline and the
    instrument schedule, and the calendar's entries. Late work had a red ring (it had been a red
    bar), in the darker red now used for late everywhere, which stayed apart from the pink of
    Analysis. On the calendar, started entries were white and finished ones gray.
  - **On Panorama:** the lab dashboard, and the projects' wiki pages, where a key named the
    colors and a step's Done badge was gray (it had been green). A wiki page took the colors the
    next time its project was changed in LabOps, or was published from Wiki page.

  The colors were chosen to stay apart for color-blind readers too, and each always appeared
  beside a name. They were defined once, in `src/LabOps.Engines/Projects/Phases.cs`.

## Bug Fixes

- **No black box before a protocol's page.** The first protocol opened after LabOps started, and
  any protocol opened after none was selected, showed a black box for up to half a second while
  its page rendered: the browser drew nothing for a page it had loaded while hidden, nor for an
  empty page. The preview loaded its page again as it appeared, and showed white until the
  protocol's page was ready. The quote preview and the projects overview, which loaded their pages
  the same way, did the same (`src/LabOps.App/Services/WebViewGuard.cs`, `HiddenLoads.cs`).
