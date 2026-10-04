# ChargeState v26.6.0

Choose Panorama folders and ELN notebooks by browsing Panorama, signed in the way PanoramaBridge
is, instead of copying addresses.

## New Features

### Projects

- **Browse Panorama's folders from Add link.** **Browse...** opens Panorama's folder tree at the
  MacCoss project, the way PanoramaBridge's folder picker does, and a folder is chosen rather than
  typed: raw files in a file area (for example `.../2026-09-BioFIND-Quant/@files/RawFiles`, where
  PanoramaBridge uploads) or the folder with the Skyline documents. It signs in with what
  PanoramaBridge saved on this computer, an API key or a user name and password. Only when there
  is none, or Panorama no longer accepts it, does it ask for one; that sign-in is checked with
  Panorama and kept in Windows Credential Manager under ChargeState's own name. ChargeState only
  reads from Panorama.
- **Browse the lab's ELN notebooks.** For an ELN notebook, **Browse...** lists the notebooks in the
  MacCoss project's ELN on Panorama, newest first, with a search by ID, title, author or status
  (archived ones on request). Choosing one fills in its ID and its link. A notebook recorded by
  ID alone now links to it too: the number at the end of the ID is the notebook's
  (`ELN-1567-20250730-131` opens `.../samplemanager-app.view#/notebooks/131`).
- A raw-data folder now opens Panorama's listing of its files, and a pasted WebDAV address keeps
  its `@files/...` part (lab-projects' `project.py link`).
