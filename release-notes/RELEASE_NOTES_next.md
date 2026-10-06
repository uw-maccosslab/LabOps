# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **A file open in another program is no longer mistaken for someone else's change.** Making a
  statement of work opened it in Word while LabOps was still sharing it. When GitHub had moved on
  (its README index is updated after every save), git could not replace the open file, stopped
  partway, and LabOps said someone else had changed the quote. Choosing **Yes** then set aside a
  half-finished copy and asked Claude to redo "your change", which could have undone changes
  already shared. Now LabOps checks before syncing and names the file to close ("...-SOW.docx is
  open in another program"); nothing is set aside, and the change is shared at the next sync. A
  sync left partway is put back where it started, and a statement of work opens only after it
  is shared.

## Performance

## Breaking Changes
