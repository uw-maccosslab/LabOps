# ChargeState vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **Closing the app no longer shows "Something went wrong".** When no Claude conversation was
  open, closing the window showed an error about a window closing, and the app stayed open and
  disabled until closed again. It now closes at once.
- **Claude keeps collection dates and ages in sample tables.** The app told Claude never to copy
  full dates or ages over 89 from a human study, so organizing a manifest such as MNRF's dropped
  them even after lab-projects stopped flagging them (its engine 26.2.0). Claude now follows the
  lab-projects CLAUDE.md for what counts as identifying, so a change to that rule no longer needs
  an app release. The message shown when the check does find something no longer mentions dates.

## Performance

## Breaking Changes
