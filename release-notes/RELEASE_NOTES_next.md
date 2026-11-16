# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

- Added **Read raw files** on an experiment's Data acquisition step, once its raw data folder is
  recorded. LabOps reads the Thermo raw files on Panorama for how they were acquired, about a
  megabyte of each by HTTP range requests instead of the whole file (292 Astral files of 3 to 7 GB
  each read in about 10 seconds), and records the instrument and its serial number, the method
  file, how many files used each method, and the first and last file's dates. Those dates become
  the step's dates and the step is marked done; a step in batches gets each batch's dates from its
  own folder when the folders are numbered like the batches (RawFiles/Plate_1, Plate_2, ...). A
  raw file that cannot be read, or a recorded folder not on Panorama yet, is listed afterwards and
  the rest are still recorded. The experiment's instrument is filled in from the serial when it names none. Afterwards the button
  is **Update from raw files**, for files added since.
- `config/instruments.yaml` in LabOps-Projects can list each instrument's serial numbers
  (`serials: [OA10308]`). The check warns when raw files were acquired on a serial it does not
  list, and when an experiment names another instrument than its raw files were acquired on.

## Bug Fixes

## Performance

## Breaking Changes
