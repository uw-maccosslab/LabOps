# ChargeState vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

### Projects

- **Add link** records where an experiment's raw data and results are on Panorama, and a project's
  or an experiment's ELN notebook. Paste the address from your browser (any Panorama address for
  the folder works; it is kept as the folder path, for example `/MacCoss/maccoss/2026-BioTRACK`),
  or a notebook's link and its ID. **Find it on Panorama** in the dialog opens Panorama to copy
  it from. The links show as **Raw data on Panorama** and **Results on Panorama** in the
  experiment's section, and each has **Remove**, which changes nothing on Panorama. Needs
  lab-projects' `project.py link`, which reaches every copy on its next sync.

## Bug Fixes

## Performance

## Breaking Changes
