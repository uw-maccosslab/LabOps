# LabOps v26.18.1

Publishing an LC-MS protocol recorded it on the experiments whose raw files were acquired with its
method, so its Used by listed them at once.

## Bug Fixes

- Fixed an LC-MS protocol's **Used by** staying empty after it was published. Publishing a
  version recorded it on every experiment whose raw files were acquired with exactly the
  instrument method it held, at the Data acquisition step, so **Used by** listed them at once.
  An experiment whose raw files were read later still got the **Record protocol** button, which
  appeared right after a publish instead of after the next sync or **Reload**
  (`src/LabOps.Core/Protocols/ProtocolMethodIndex.cs`).
