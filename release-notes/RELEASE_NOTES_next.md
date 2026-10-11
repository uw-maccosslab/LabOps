# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **No black box before a protocol's page.** The first protocol opened after LabOps started, and
  any protocol opened after none was selected, showed a black box for up to half a second while
  its page rendered: the preview's browser draws nothing for a page it loaded while hidden. The
  preview now loads its page again as it appears, and stays white until the protocol's page is
  ready.

## Performance

## Breaking Changes
