# LabOps v26.18.2

Publishing an LC-MS protocol version no longer rewrote the version recorded on earlier
experiments. It recorded the version only once the publish reached GitHub, on the right step, and
updated the wiki page of every project it changed.

## Bug Fixes

- Fixed publishing a new version of an LC-MS protocol, one that kept the same instrument method,
  rewriting the version recorded on every earlier experiment to the new one and dropping its
  deviations. Publishing recorded a version only on experiments that recorded no protocol
  version holding that method, of this protocol or another, and **Record protocol** was no longer
  offered where one was recorded already (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`).
- Fixed a publish whose commit failed, was refused or was set aside after a conflict still
  recording the version on experiments, so they named a version LabOps-Protocols never got.
  Publishing waited for the version to reach GitHub before recording it; offline, **Record
  protocol** recorded it later (`src/LabOps.App/ViewModels/ProtocolsViewModel.cs`).
- Fixed the Panorama wiki pages of the projects a publish recorded a protocol on staying out of
  date, while the selected project's page was published instead. Every project the publish
  changed had its page updated, closed ones included
  (`src/LabOps.App/ViewModels/ProjectsViewModel.cs`).
- Fixed a protocol recorded on several experiments at a publish being left half done when one of
  them failed or the commit was refused: the experiments already changed were put back as they
  were, so nothing was saved later under another change's message
  (`src/LabOps.App/ViewModels/ProjectsViewModel.cs`).
- Fixed a protocol recorded at a publish going on no step for an experiment without a Data
  acquisition step, where **Record protocol** put it on Data deposited. Both used the Data
  acquisition step, else Data deposited (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`,
  `src/LabOps.App/ViewModels/ProjectItems.cs`).
