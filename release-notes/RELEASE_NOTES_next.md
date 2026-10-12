# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **A protocol's method could be opened from its page.** Clicking a method's name (a KingFisher
  program, an instrument method) on a protocol's page had done nothing; it opened File Explorer
  with the file selected, ready to copy or open. This needed LabOps-Protocols' engine, which links
  each method to its file. The preview also stopped following a link to any other file on the
  computer (`src/LabOps.App/Views/ProtocolsView.xaml.cs`).

- **A copy too old for a lab repository could update from the banner that said so.** The banner
  said to update but offered no way to, and the app looked for updates only every 4 hours, so a
  release just made could wait hours. While a repository needed a newer app, LabOps looked for the
  update at once and every 10 minutes after, the banner said how the update was going, and its
  button read **Check for updates**, or **Restart to update** once the update was downloaded
  (`src/LabOps.App/ViewModels/MainViewModel.cs`, `Services/UpdateService.cs`).

- **The app's examples named an invented protocol instead of one of the lab's.** The title box's
  tooltip in **New protocol**, the message when a replacement protocol is not found, and the
  engine's error asking for a protocol's ID now give the made-up examples *Spin trap digestion* and
  `spin-trap-digestion` (`src/LabOps.App/Views/NewProtocolWindow.xaml`,
  `ViewModels/ProtocolsViewModel.cs`, `src/LabOps.Engines/Projects/ProjectsEngine.Write.cs`).

## Performance

## Breaking Changes
