# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **The project engine is built into LabOps.** The rules for labs, projects and experiments (the
  steps, the identifier check, the record edits, the wiki page, the Octopus files) used to run as
  LabOps-Projects' `scripts/project.py` through Python. They now run inside the app, rewritten in
  C# to give the same answers: every record it writes is byte for byte what `project.py` wrote,
  and each wiki page is identical, so pages published before keep updating on their own. The
  Projects area no longer needs Python, and Setup's **Project engine** step now just says it is
  built in.
  - Claude's skills and the pre-commit hook run the same engine as the **labops** tool
    (`labops projects stage ...`, with the commands and options of `project.py`), which LabOps
    keeps in its tools folder. Claude may run it without asking.
  - `LABOPS_PROJECT_ENGINE=python` runs `scripts/project.py` instead, for this release only, in
    case the new engine misses something. Tell Mike if you need it.

## Bug Fixes

- **Records the old engine stopped on are reported instead.** Where `project.py` stopped with a
  Python error on a loosely written record, the engine now says what to fix: an impossible date
  such as 2026-02-30, a start date compared with a date and time, a step status it does not know,
  a number as a key in wiki.yaml, a record that is not UTF-8, or a step kind written as a list.
- **Two record edits no longer damage a line.** A quoted value with " #" in it keeps all of itself
  when the field is changed (it used to lose its end to the comment), and a funding value with a #
  in it is replaced rather than added a second time.

## Performance

- **Projects commands are three to seven times faster.** A command no longer starts Python:
  listing the lab's projects takes about 80 ms with the labops tool, against about 300 ms before,
  and the app runs the engine in-process, without starting anything. LabOps-Projects' own test
  suite runs in one minute instead of two and a half.

## Breaking Changes
