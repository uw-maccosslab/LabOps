# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **A screen reader read a project, quote or notebook in a list by its name.** It had read each row
  of the Projects list, the quotes list and the notebook picker as every field the app kept for it,
  nested lists and all. A project read as its name and title, a quote as its number and title, and
  a notebook as its ID and title (`src/LabOps.App/ViewModels/ProjectItems.cs`,
  `src/LabOps.Core/Quotes/QuoteSummary.cs`, `src/LabOps.Core/Panorama/PanoramaClient.cs`).

## Performance

## Breaking Changes
