# LabOps v26.8.4

Syncing is safer: a file open in Word or a PDF viewer is no longer mistaken for someone else's
change, and a statement of work is shared before it opens. It goes with quote engine 26.4.0,
which writes the statement of work as Markdown and a PDF in the quotation's style.

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
- **Syncing is safer when git stops partway.** LabOps leaves alone a rebase someone started by
  hand (in a terminal), instead of undoing it; it skips a commit only when git says the commit
  became empty, never for another reason; and when it has to put the copy back, changes not yet
  saved come back too. A conflict in a statement of work is resolved by making it again from the
  quote, like the quote's own text, rather than asking you or Claude to redo it.
