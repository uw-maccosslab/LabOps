# LabOps v26.18.0

An experiment's Data acquisition step now reads how its data were acquired from the raw files on
Panorama, and records the LC-MS protocol whose instrument method they were acquired with, or
writes one with Claude.

## New Features

### Raw files

- **Read raw files** on an experiment's Data acquisition step, once its raw data folder is
  recorded. LabOps reads the Thermo raw files on Panorama for how they were acquired, about a
  megabyte of each by HTTP range requests instead of the whole file (292 Astral files of 3 to 7 GB
  each read in about 3 seconds), and records the instrument and its serial number, the method
  file, how many files used each method, and the first and last file's dates. Those dates become
  the step's dates and the step is marked done, replacing dates typed from memory. A step in
  batches gets each batch's dates from its own folder when the folders are numbered like the
  batches (RawFiles/Plate_1, Plate_2, ...). A raw file that cannot be read, or a recorded folder
  not on Panorama yet, is listed afterwards and the rest are still recorded. The experiment's
  instrument is filled in from the serial when it names none. Afterwards the button is
  **Update from raw files**, for files added since (`src/LabOps.Core/Acquisition`).
- `config/instruments.yaml` in LabOps-Projects can list each instrument's serial numbers
  (`serials: [OA10308]`). The check warns when raw files were acquired on a serial it does not
  list, and when an experiment names another instrument than its raw files were acquired on.

### LC-MS protocols

- **Record protocol** and **Write a protocol from this method** on the Data acquisition step,
  after the raw files are read. When a published LC-MS protocol version holds exactly the
  instrument method the raw files were acquired with, one click records it on the step. When none
  does, LabOps takes the method out of one raw file on Panorama (about a megabyte) and opens a
  chat in Protocols where Claude writes the protocol from every setting in it (LC system, columns,
  gradient, source, scans, DIA windows or PRM targets), as a new protocol or the next version of
  one; you review and publish it. Any change to an LC-MS method is a new protocol or version,
  never a deviation. This needs the protocol engine 26.5.0, which LabOps-Protocols already has
  (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`).

## Bug Fixes

- Fixed the status bar showing "UpToDate" instead of "Up to date" after a save that had nothing
  new to record, such as **Update from raw files** when no files had been added
  (`src/LabOps.Core/Sync/SyncService.cs`).
