# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **A copy too old for a lab repository looked for its update twice at once.** At startup, or when
  **Check for updates** was pressed during a check, a second check started and downloaded the same
  release again, and the banner could say getting the update had failed while it was on its way.
  A check asked for while one ran joined it (`src/LabOps.App/Services/UpdateService.cs`,
  `src/LabOps.Core/Infrastructure/OneAtATime.cs`).

- **The update banner stopped replacing the window's other messages.** While the app was too old
  for a repository, each step of the update rewrote the window's banner, so a message such as "The
  quotes could not be loaded" vanished within seconds, and showed the update's button. The update
  got a banner of its own (`src/LabOps.App/MainWindow.xaml`, `ViewModels/MainViewModel.cs`).

- **The update banner said what to do in two more cases.** When getting the update failed, it
  showed only the error; it then said to check the internet connection and press **Check for
  updates**. When the update downloaded was itself older than the repository needed, it said a
  restart was all it took; it then said the restart installs it and the needed one comes after
  (`ViewModels/MainViewModel.cs`).

- **A protocol's method opened when LabOps-Protocols was reached through a junction, a link or a
  mapped drive.** The page names a method by where the copy really is, which the app compared with
  the path it knew the copy by, so clicking the method did nothing. Both were compared as Windows
  resolves them (`src/LabOps.Core/Protocols/ProtocolFiles.cs`, `Infrastructure/FinalPath.cs`).

- **The projects overview and the wiki preview followed no link to another page inside the app.**
  As the protocol preview did from 26.15.0, each loaded only its own page; a link to another file
  on the computer, or to a page written into the link itself, was not followed
  (`src/LabOps.App/Services/WebViewGuard.cs`, `Views/ProjectsView.xaml.cs`, `Views/WikiWindow.xaml.cs`).

- **Pasting into the Claude pane no longer showed an error or lost a picture.** A picture copied
  from a program that had since closed made **Ctrl+V** report that something went wrong; the paste
  then went ahead as text. Two pictures pasted in the same second could collide, and the second
  was not attached; each got a name of its own (`Views/ChatPanel.xaml.cs`,
  `ViewModels/ChatViewModel.cs`).

## Performance

## Breaking Changes
