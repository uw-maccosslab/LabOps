# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- **Projects had an Overview.** A **List** | **Overview** switch at the top of the Projects area
  showed the open work five ways:
  - **Needs attention**: late steps, steps due or starting in the next 7 days (today included),
    and records with errors, those in a lab's own record included.
  - **Board**: each open project and experiment at its current step, in columns from Samples to
    Results, late ones first and marked red.
  - **Timeline**: twelve weeks of each item's steps, what happened solid and what is planned
    dashed, late steps red, with a line for today.
  - **Calendar**: a month of steps started, finished, planned to start and due, three a day with a
    count of the rest; the arrows changed the month.
  - **Instruments**: each instrument's data acquisitions on the same weeks, with overlapping
    bookings listed and drawn red.

  **Whose** showed one person's work (steps assigned to them, and projects they are the lab
  contact for; in Needs attention and the calendar, only their own steps and the unassigned ones of
  those projects) and **Lab** one lab's. The overview opened on your own work when you had some.
  Work on hold was never shown as late, and its board card said "on hold". Late was said in words
  as well as in red, and people were shown by name. Clicking a project or experiment showed it in
  the list. The page ran no scripts and loaded nothing from the web.
- **Steps had a Plan button.** It set when a step should start and be finished (either date could
  be left empty, and **Remove the plan** removed both). The step then showed "Planned Oct 12 to
  Oct 20", in red with "late" once a date had passed and the step was not done.
- **Steps could be planned.** `labops projects plan <item> <step>... --start DATE --finish DATE`
  recorded when steps should start and finish (a date not given kept the one recorded, `--no-start`
  and `--no-finish` removed one, and `--clear` removed both), written on each step's line as
  `planned_start` and `planned_finish`; whatever it changed was one write. `check`
  refused a plan that was not dates or that finished before it started; nothing in the check depended
  on the day it ran. A step counted as late when its planned finish had passed and it was not done or
  skipped, or when its planned start had passed and it had not started.
- **The lab's instruments could be listed.** LabOps-Projects' `config/instruments.yaml` named the
  lab's instruments (`- name: Orbitrap Astral`); `check` warned about an experiment whose instrument
  was not one of them, and `labops projects list` gave the list. Without the file nothing changed.
- **The README index listed only open work.** `labops projects index` left closed projects and the
  closed experiments of open projects out of the table, with a line saying how many it left out, so
  the index stayed the size of the current work however many projects the repository held. `check`
  warned about an experiment still open in a closed project, which would otherwise drop out of view.

## Bug Fixes

- **A config file with a mistake no longer stopped every command.** A `config/people.yaml` or
  `config/instruments.yaml` that was not valid YAML used to crash `list`, `check` and `index` (and
  the Projects area) and to crash a step change after it had written the record. `check` now
  reported it as an ERROR and everything else carried on.

## Performance

## Breaking Changes

- **`LABOPS_PROJECT_ENGINE=python` was removed.** LabOps-Projects dropped `scripts/project.py` once
  LabOps 26.10.0 built the engine in, so the setting had nothing left to run. Setup's **Project
  engine** step always says the engine is built in, and Claude is no longer allowed to run
  `project.py` in the projects repository, only `labops projects`.
