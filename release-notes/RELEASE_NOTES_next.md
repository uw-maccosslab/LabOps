# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Choose the model and effort Claude uses in LabOps.** Setup has a new **Claude in LabOps**
  section with **Model** (Opus, Sonnet or Haiku) and **Effort** (low to max). Both change how much
  of your own Claude plan a conversation uses, so each person chooses for themselves. Until you
  choose, LabOps uses your Claude Code default, the same as in a terminal, and Setup names it
  when your Claude Code settings set one. A change applies to the next conversation you start.

## Bug Fixes

- **An expired Claude sign-in can be fixed from the chat.** When Claude Code could not sign in
  ("Failed to authenticate: OAuth session expired and could not be refreshed"), the chat showed only
  the error. It now offers **Sign in to Claude**, which opens Claude's sign-in, and then **Try
  again**, which starts the conversation again. Setup's Claude sign-in also offers **Sign in again**
  when it shows as signed in, because a session that can no longer be refreshed still reports as
  signed in. Both say what to do when the sign-in page says the window is too small.
- **Signing in to Claude no longer opens a console window.** The sign-in runs in the background
  and the browser does the rest; the chat shows a link to the sign-in page in case the browser
  does not open, and **Stop waiting** ends it. Only if it does not finish does LabOps offer the
  console window it used before.
- **The protocol shown no longer flickers when LabOps syncs.** Every sync that brought in a commit
  (including the README index GitHub commits after each save) emptied and refilled the protocol
  list, so the page went blank and came back. The list is now updated in place, keeping the
  protocol and version selected, and the page is redrawn only when it changed.
- **Enter sends a message to Claude, and Claude's questions are answered in the same box.** In the
  chat, Enter now sends (Shift+Enter starts a new line). When Claude asks a question, its card keeps
  the answer buttons, and a typed answer goes in the chat's own box at the bottom, which stays open
  while Claude waits (it was grayed out, and each question had a box of its own); the cursor moves
  there when the question arrives, unless you are typing somewhere else.
- **The chat says when you are looking at something else.** A conversation stays about the quote,
  project or protocol it started with, so a message typed while looking at another one went to the
  wrong conversation. When what is on screen differs, the chat now says so at the top ("This
  conversation is about QC aliquots. You are looking at SAX KFKF.") with **Talk about SAX KFKF**,
  and Send asks before the message goes to the conversation about the other one.
- **The protocol and wiki previews no longer run scripts.** A protocol's page showed any script
  written into its text, so a draft pushed by anyone could run code in LabOps and send out what
  it could read. Both previews now run no scripts, and the protocol page loads nothing from the
  web or from other files on the computer (its figures are part of the page).
- **An engine answer LabOps cannot read no longer fails without a word.** A version written as
  `1.0` in a protocol.yaml stopped the Protocols list from loading with an unexplained failure.
  Now, in the Quotes, Projects and Protocols areas alike, an answer LabOps cannot read is reported
  as that engine's problem ("LabOps could not read what the protocol engine answered..."), the same
  way as any other.

## Performance

## Breaking Changes
