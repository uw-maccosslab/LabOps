# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- Fixed publishing a new version of an LC-MS protocol, one that kept the same instrument method,
  rewriting the version recorded on every earlier experiment to the new one and dropping its
  deviations. Publishing now records a version only on experiments that do not record that
  protocol at all, and **Record protocol** is no longer offered where a version holding the same
  method is recorded already (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`).
- Fixed a publish whose commit failed or was refused still recording the version on experiments,
  so they named a version LabOps-Protocols never got
  (`src/LabOps.App/ViewModels/ProtocolsViewModel.cs`).
- Fixed the Panorama wiki pages of the projects a publish recorded a protocol on staying out of
  date, while the selected project's page was published instead. Every project the publish
  changed now has its page updated (`src/LabOps.App/ViewModels/ProjectsViewModel.cs`).
- Fixed a protocol recorded on several experiments at a publish being left half done when one of
  them failed: the experiments already changed were put back as they were, so nothing is saved
  later under another change's message.
- Fixed a protocol recorded at a publish going on no step for an experiment without a Data
  acquisition step, where **Record protocol** put it on Data deposited. Both now use the Data
  acquisition step, else Data deposited.

## Performance

## Breaking Changes
