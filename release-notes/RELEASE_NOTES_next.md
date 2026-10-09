# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **The lab dashboard went on Panorama.** When LabOps-Projects' `config/app.yaml` named a folder
  (`dashboard: {folder: /MacCoss/LabOps}`), the Overview had **Publish to Panorama** and **Open on
  Panorama**. Publishing, after a confirmation, put a summary of every lab's open work on that
  folder's page: what was late or due in the next 7 days, how much work each lab had at each phase,
  and the instruments' bookings from two weeks back to four ahead, every list capped at 25 rows.
  From then on each lab member's LabOps kept it up to date after their projects reloaded, but only
  when their copy had just synced with GitHub, only a page LabOps had made, and never one edited on
  Panorama (publishing again took it back). Error messages and names of records stayed in LabOps;
  the page said only how many records had errors.

## Bug Fixes

- **LabOps started again.** LabOps 26.10.0 and 26.11.0 could close straight away at startup ("Could
  not load file or assembly 'Serilog'"): the build had put the labops tool's list of libraries in
  place of the app's. The release now checks the app's own list before publishing. A copy that does
  not start cannot update itself; download **LabOps Setup** from this release's page and run it.
- **A page shown in LabOps loaded nothing from the web, frames included.** The protocol preview and
  the Overview already ran no scripts and blocked images and scripts from the web, but a frame in a
  page could still have loaded a web address. Frames now show nothing but an empty or inline page.
- **The timeline and the instrument schedule could be used without a mouse.** Each bar could be
  reached with Tab, said its item, step and dates to a screen reader, showed them under it when
  focused, and showed its item in the list when clicked.
- **An instrument listed twice with different capitals counted once.** `config/instruments.yaml`
  with both "Stellar" and "stellar" gave one instrument, as first spelled, matching how
  experiments' instruments were compared.

## Performance

## Breaking Changes
