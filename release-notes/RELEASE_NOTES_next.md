# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- **The repositories have LabOps names.** lab-projects is now LabOps-Projects and services-quotes is
  now LabOps-Quotes, so the lab's GitHub page lists LabOps, LabOps-Projects and LabOps-Quotes
  together. LabOps clones, checks access and reads CI status under the new names, and new copies go
  in folders named LabOps-Projects and LabOps-Quotes. A copy made under an old name still opens,
  and the first time it does, LabOps points its remote at the new name (GitHub redirects the old
  one in the meantime). Existing folders keep their names.

## Performance

## Breaking Changes
