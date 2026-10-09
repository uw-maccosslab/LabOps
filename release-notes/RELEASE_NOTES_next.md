# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Steps could be planned.** `labops projects plan <item> <step>... --start DATE --finish DATE`
  recorded when steps should start and finish (a date not given kept the one recorded, and `--clear`
  removed the plan), written on each step's line as `planned_start` and `planned_finish`. `check`
  refused a plan that was not dates or that finished before it started; nothing in the check depended
  on the day it ran. A step counted as late when its planned finish had passed and it was not done or
  skipped, or when its planned start had passed and it had not started.
- **The lab's instruments could be listed.** LabOps-Projects' `config/instruments.yaml` named the
  lab's instruments (`- name: Orbitrap Astral`); `check` warned about an experiment whose instrument
  was not one of them, and `labops projects list` gave the list. Without the file nothing changed.
- **The README index listed only open work.** `labops projects index` left closed projects out of
  the table, with a line saying how many it left out, so the index stayed the size of the current
  work however many projects the repository held.

## Bug Fixes

## Performance

## Breaking Changes

- **`LABOPS_PROJECT_ENGINE=python` was removed.** LabOps-Projects dropped `scripts/project.py` once
  LabOps 26.10.0 built the engine in, so the setting had nothing left to run. Setup's **Project
  engine** step always says the engine is built in, and Claude is no longer allowed to run
  `project.py` in the projects repository, only `labops projects`.
