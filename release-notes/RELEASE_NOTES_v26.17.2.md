# LabOps v26.17.2

Syncing no longer failed with "GitHub did not accept your sign-in" on a computer where GitHub was
already signed in before LabOps was set up, and Setup now offers the button that fixes it.

## Bug Fixes

- **Setup connects git to your GitHub sign-in.** When the GitHub command-line tool was already
  signed in before Setup ran, Setup took the sign-in as done and never connected git to it. Every
  sync then said "GitHub did not accept your sign-in. Open Setup and sign in to GitHub again", while
  Setup said everything was ready and showed no button to press. Setup now has a **Syncing with
  GitHub** step that asks GitHub the way a sync does. When git is turned away, the step shows
  **Connect**, which connects git to your GitHub sign-in with no browser or console window, and
  Setup opens on its own at startup until that is done. The sync error now points to that step
  (`src/LabOps.Core/Setup/SetupService.cs`, `src/LabOps.Core/Sync/SyncService.cs`).
