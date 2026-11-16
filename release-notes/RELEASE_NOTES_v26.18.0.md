# LabOps v26.18.0

An experiment's Data acquisition step now reads how its data were acquired from the raw files on
Panorama, and records the LC-MS protocol whose instrument method they were acquired with, or
writes one with Claude.

## New Features

### Raw files

- Added **Read raw files** to an experiment's Data acquisition step, once its raw data folder is
  recorded. LabOps read the Thermo raw files on Panorama for how they were acquired, about a
  megabyte of each by HTTP range requests instead of the whole file (292 Astral files of 3 to 7 GB
  each read in about 3 seconds), and recorded the instrument and its serial number, the method
  file, how many files used each method, and the first and last file's dates. Those dates became
  the step's dates and the step was marked done, replacing dates typed from memory. A step in
  batches got each batch's dates from its own folder when the folders were numbered like the
  batches (RawFiles/Plate_1, Plate_2, ...). A raw file that could not be read, or a recorded folder
  not on Panorama yet, was listed afterwards and the rest were still recorded. The experiment's
  instrument was filled in from the serial when it named none. Afterwards the button became
  **Update from raw files**, for files added since (`src/LabOps.Core/Acquisition`).
- Added instrument serial numbers to `config/instruments.yaml` in LabOps-Projects
  (`serials: [OA10308]`). The check warned when raw files were acquired on a serial it did not
  list, and when an experiment named another instrument than its raw files were acquired on.

### LC-MS protocols

- Added **Record protocol** and **Write a protocol from this method** to the Data acquisition
  step, after the raw files are read. When a published LC-MS protocol version held exactly the
  instrument method the raw files were acquired with, one click recorded it on the step. When none
  did, LabOps took the method out of one raw file on Panorama (about a megabyte) and opened a chat
  in Protocols where Claude wrote the protocol from every setting in it (LC system, columns,
  gradient, source, scans, DIA windows or PRM targets), as a new protocol or the next version of
  one, for you to review and publish. Any change to an LC-MS method was a new protocol or version,
  never a deviation. This needed the protocol engine 26.5.0 in LabOps-Protocols
  (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`).

## Bug Fixes

- Fixed the status bar showing "UpToDate" instead of "Up to date" after a save that had nothing
  new to record, such as **Update from raw files** when no files had been added
  (`src/LabOps.Core/Sync/SyncService.cs`).
