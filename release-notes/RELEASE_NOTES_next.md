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

## Performance

## Breaking Changes
