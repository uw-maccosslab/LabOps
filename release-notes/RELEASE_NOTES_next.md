# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- Fixed syncing that failed with "GitHub did not accept your sign-in" while Setup said everything
  was ready and offered no button. This happened when the GitHub command-line tool was already
  signed in before Setup ran: Setup counted the sign-in as done, so git was never connected to it.
  Setup now has a **Syncing with GitHub** step that asks GitHub the way a sync does. When git is
  turned away, the step shows **Connect**, which connects git to your GitHub sign-in with no
  browser or console window. Setup then opens on its own at startup until this is fixed.

## Performance

## Breaking Changes
