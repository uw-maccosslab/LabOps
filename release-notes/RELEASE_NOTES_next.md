# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

## Performance

## Breaking Changes

- **`LABOPS_PROJECT_ENGINE=python` was removed.** LabOps-Projects dropped `scripts/project.py` once
  LabOps 26.10.0 built the engine in, so the setting had nothing left to run. Setup's **Project
  engine** step always says the engine is built in, and Claude is no longer allowed to run
  `project.py` in the projects repository, only `labops projects`.
