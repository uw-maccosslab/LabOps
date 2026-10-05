# LabOps v26.8.0

The lab's protocols come to LabOps: a new Protocols area holds every protocol in one format with
every version ever published, Claude writes them from your Word documents and PDFs, and a
project's sample prep records the exact version it followed. The repositories also have their
LabOps names.

## New Features

- **Protocols.** A new **Protocols** area holds the lab's protocols, from the new
  [LabOps-Protocols](https://github.com/uw-maccosslab/LabOps-Protocols) repository, each in one
  format (Arial, ready to print) with every version ever published. It starts with thirteen of the
  lab's protocols, from S-Trap and FASP digestion to Kasil frits.
  - The list sorts by category, version, publication date or owner, and searches by title, tag,
    sample type or instrument. **Showing** picks any published version, or the draft, and shows it
    as the page; an old version says which one is current. **Print** opens it in the browser.
  - **New protocol** writes one with Claude, from a Word document (including old `.doc` files),
    PDF, LaTeX, Markdown or text file you choose, or by asking you for the steps. Claude keeps
    everything the original says, checks every recipe's arithmetic, and lists each correction and
    question for you to review. **Ask Claude to change it** and **Update from a file** change the
    draft.
  - **Publish version N** makes the draft the next version, with what it changes; **Show changes**
    shows the difference first. A published version never changes: the repository's check
    refuses it. **Retire** marks a protocol as no longer used and names its replacement.
- **The protocol a step followed.** **Add protocol** on a project's Sample prep step (or an
  experiment's assay development or data acquisition) records a protocol at the published version
  used. The step then shows it, clicking it opens that version in the Protocols area, and the
  project's wiki page lists it.
- **Setup downloads the lab protocols.** They are optional, so the app opens before they are
  downloaded; the Protocols area offers Setup until then.

## Bug Fixes

- **The repositories have LabOps names.** lab-projects is now LabOps-Projects and services-quotes is
  now LabOps-Quotes, so the lab's GitHub page lists LabOps, LabOps-Projects and LabOps-Quotes
  together. LabOps clones, checks access and reads CI status under the new names, and new copies go
  in folders named LabOps-Projects and LabOps-Quotes. A copy made under an old name still opens,
  and the first time it does, LabOps points its remote at the new name (GitHub redirects the old
  one in the meantime). Existing folders keep their names.
- **The warning that the app is too old clears on Reload.** After a repository lowered the
  version it needs, the banner stayed until the app restarted.
