# LabOps v26.17.0

A protocol recorded on a project's step could say what that work did differently from the version
it followed, the Protocols area showed every project and experiment that followed each protocol,
and the identifier check stopped refusing metadata workbooks with title rows over their headers.

## New Features

- **A protocol on a step recorded what the work did differently from its version.** Most
  experiments follow a published version with a few small changes, which had to become a new
  version of the protocol to be recorded at all. Add protocol gained a **Changes from this
  version** box, one change per line (for example "50 µL of plasma instead of 100 µL"), and each
  protocol on a step gained an **Edit** button. The step showed the changes under the protocol, and
  so did the Protocols table on the project's wiki page. Recording a newer version kept the
  changes. `labops projects link <item> protocol <id> --version N --deviation TEXT` did the same for
  Claude and in a terminal (`src/LabOps.Engines/Projects/ProjectsEngine.Write.cs`,
  `src/LabOps.App/Views/ChooseProtocolWindow.xaml`, `src/LabOps.App/Views/ProjectsView.xaml`).
- **The Protocols area showed where each protocol was followed.** A **Used by** column counted the
  projects and experiments that recorded a protocol, and the selected protocol listed each one with
  its step, the version followed and any changes, closed projects included, so a mistake found in a
  version showed at once whose work used it. Clicking one opened it in the Projects area.
  `labops projects uses [<protocol>]` listed the same (`src/LabOps.Engines/Projects/ProjectsEngine.cs`,
  `src/LabOps.App/ViewModels/ProtocolsViewModel.cs`, `src/LabOps.App/Views/ProtocolsView.xaml`).

## Bug Fixes

- **A metadata workbook with title rows over its headers was no longer refused.** A merged "Meta
  Data" or "Plasma samples" row above the columns it grouped had been read as the header row, so
  every column under it counted as having no header and the file was refused as identifying. The
  check now found the real headers under the titles and checked those, so an American mink sheet
  refused with 90 errors attached with none, and a name column under a title was caught by name
  (`src/LabOps.Engines/Projects/Sheets.cs`).
- **A column without a header in a collaborator's sheet only warned before Claude read the file.**
  An unlabeled plate map beside the samples had kept the whole workbook from Claude. Claude was
  told to check such a column instead. A column whose header named a person, a contact detail or a
  record number still kept the file from Claude, and nothing was committed with a column the check
  could not name (`src/LabOps.Engines/Projects/Deidentification.cs`).
- **Remove stayed in view beside a protocol with a long title.** A long protocol title on a step
  had pushed Remove past the edge of the timeline; Remove and Edit now wrapped onto the next line
  (`src/LabOps.App/Views/ProjectsView.xaml`).
