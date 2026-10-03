# ChargeState vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Updates are found while the app is open.** ChargeState looked for a new release only when it
  started, so a copy left open all day never offered one. It now also checks every four hours, as
  PanoramaBridge does, downloads in the background, and shows **Update ready: restart to install**.
  It never checks again once an update is waiting, and installing still waits for you
  (`src/ChargeState.App/Services/UpdateService.cs`).

## Bug Fixes

## Performance

## Breaking Changes
