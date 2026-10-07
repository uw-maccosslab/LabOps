# LabOps v26.8.5

Files can be attached in the chat, and New protocol and Update from file take several files at
once, with method files (a KingFisher program, say) kept in the protocol's new methods/ folder.
It goes with protocol engine 26.4.0.

## New Features

- **Files could be attached in the chat.** There was no way to give Claude a file in a
  conversation. The chat gained **Attach** beside Send, and files (or whole folders) dropped on the
  message box were added to the next message. Claude read copies kept in a folder of that
  conversation's own, outside every repository, so nothing attached was ever shared on GitHub, and
  the folder was deleted when the conversation ended. In the projects, a spreadsheet or CSV was
  checked for identifying information first and refused if it had any, as Organize metadata does;
  another kind of file was attached only when you said it held none. A batch over 200 MB or 20
  files asked first.
- **New protocol and Update from file took several files.** They took one file, so a protocol's
  appendix or its KingFisher method had to be copied in by hand. A protocol's documents (Word, PDF,
  text) and the method files its steps run (a KingFisher or other robot program, an instrument
  method) could then be chosen together: Claude formatted the documents and kept them as the
  protocol's sources, and kept each method file in the protocol's new methods/ folder, linked from
  the step that runs it, where a published version froze it like a figure. It needed protocol
  engine 26.4.0; with an older copy of the lab protocols, LabOps said to sync first.

## Bug Fixes

- **The status bar lists the repositories in the order of the toolbar.** The sync status at the
  bottom read Projects, Quotes, Protocols, while the toolbar at the top reads Projects,
  Protocols, Quotes. It now matches.
