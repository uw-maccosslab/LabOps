# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Setup names each Claude model with its version.** **Model** now offers Opus 5.5, Opus 5,
  Sonnet 5.5, Sonnet 5 and Haiku 4.5, and passes each one's full name, so the version shown is the
  one used; versions of the same model differ a lot. Before, it offered Opus, Sonnet and Haiku,
  which meant whichever version was newest. Anyone who chose one of those keeps it, now shown as
  "Opus, the latest version" (and so on), until they choose again.

## Bug Fixes

## Performance

## Breaking Changes
