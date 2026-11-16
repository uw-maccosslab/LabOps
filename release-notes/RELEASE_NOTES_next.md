# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- Fixed an LC-MS protocol's **Used by** staying empty after it was published. Publishing a
  version now records it on every experiment whose raw files were acquired with exactly the
  instrument method it holds, at the Data acquisition step, so **Used by** lists them at once.
  An experiment whose raw files are read later still gets the **Record protocol** button, which
  now appears right after a publish instead of after the next sync or **Reload**
  (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`).

## Performance

## Breaking Changes
