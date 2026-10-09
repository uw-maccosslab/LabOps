# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **A free-text column could be marked as read.** `check` warns about every notes or comments column,
  since no rule can tell a note from a name. Once someone had read one and found nothing identifying,
  `labops projects review <project> <file> [column...] --by LOGIN` recorded it in the project's
  record under `reviewed:`, with who, when, and a fingerprint of the column's wording. `check` (and
  the pre-commit check) then left that column alone, and warned again, naming who read it and when,
  as soon as its wording changed; more rows saying the same things needed no new review.

## Bug Fixes

## Performance

## Breaking Changes
