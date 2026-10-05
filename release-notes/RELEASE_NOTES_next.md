# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **An update no longer fails because of a program LabOps opened.** Everything LabOps opened (a
  protocol's page in the browser, a quote's PDF or spreadsheet, a Word document, Claude's sign-in)
  kept LabOps's program folder as its working folder, and while one was still open, **restart to
  install** could not replace that folder: the installer gave up after 10 seconds and started the
  old version, which offered the same update again. LabOps now works from its data folder, so
  what it opens no longer holds the program folder. When a restart does not install an update,
  the button now says **Update ... did not install: see why**, and explains what to close.

## Performance

## Breaking Changes
