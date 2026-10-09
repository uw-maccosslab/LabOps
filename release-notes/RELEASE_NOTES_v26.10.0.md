# LabOps v26.10.0

The project engine moved into LabOps, so the Projects area no longer needed Python and its commands
ran three to seven times faster.

## New Features

- **The project engine was built into LabOps.** The rules for labs, projects and experiments (the
  steps, the identifier check, the record edits, the wiki page, the Octopus files) used to run as
  LabOps-Projects' `scripts/project.py` through Python. They ran inside the app instead, rewritten in
  C# to give the same answers: every record it wrote was byte for byte what `project.py` wrote, and
  each wiki page was identical, so pages published before kept updating on their own. The Projects
  area stopped needing Python, and Setup's **Project engine** step said it was built in.
  - Claude's skills and the pre-commit hook could run the same engine as the **labops** tool
    (`labops projects stage ...`, with the commands and options of `project.py`), which LabOps kept
    in its tools folder. Claude could run it without asking.
  - The engine's messages, the comments it wrote into records, and the heading of the README's
    project index named `labops projects` commands rather than `project.py`.
  - `LABOPS_PROJECT_ENGINE=python` ran `scripts/project.py` instead, for this release only, in case
    the new engine missed something.

## Bug Fixes

- **Records the old engine stopped on were reported instead.** Where `project.py` stopped with a
  Python error on a loosely written record, the engine said what to fix: an impossible date such as
  2026-02-30, a start date compared with a date and time, a step status it did not know, a number
  as a key in wiki.yaml, a record that was not UTF-8, or a step kind written as a list.
- **Two record edits no longer damaged a line.** A quoted value with " #" in it kept all of itself
  when the field was changed (it used to lose its end to the comment), and a funding value with a #
  in it was replaced rather than added a second time.

## Performance

- **Projects commands ran three to seven times faster.** A command no longer started Python:
  listing the lab's projects took about 80 ms with the labops tool, against about 300 ms before,
  and the app ran the engine in-process, without starting anything.
