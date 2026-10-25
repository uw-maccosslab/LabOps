# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- A protocol recorded on a step can now say what this work did differently from that version,
  for example "50 µL of plasma instead of 100 µL". Type the changes, one per line, in the new
  **Changes from this version** box when adding a protocol, or click **Edit** beside a protocol
  on a step. The step and the project's wiki page show them under the protocol, so a small change
  for one experiment no longer needs a new protocol version.
- The Protocols area shows where each protocol was followed. A **Used by** column counts the
  projects and experiments that recorded it, and the selected protocol lists each one with its
  step, the version followed and any changes; clicking one opens it in the Projects area. Closed
  projects are listed too, so a mistake found in a version shows at once whose work used it.

## Bug Fixes

- A collaborator's metadata workbook with title rows over its headers (a merged "Meta Data" above
  the columns it groups) is no longer refused. The identifier check finds the real headers under
  the titles and checks those, so an American mink sheet that was refused with 90 errors now
  attaches with none.
- A column without a header in a collaborator's sheet, such as a plate map beside the samples, now
  only warns before Claude reads the file; Claude is told to check it. A column whose header
  names a person, a contact detail or a record number still keeps the file from Claude, and
  nothing is committed with a column the check cannot name.

## Performance

## Breaking Changes
