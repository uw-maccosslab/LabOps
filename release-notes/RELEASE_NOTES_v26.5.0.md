# ChargeState v26.5.0

Record where an experiment's raw data and results are on Panorama, and its ELN notebook, with
Add link.

## New Features

### Projects

- **Add link** records where an experiment's raw data and results are on Panorama, and a project's
  or an experiment's ELN notebook. Paste the address from your browser (any Panorama address for
  the folder works; it is kept as the folder path, for example `/MacCoss/maccoss/2026-BioFIND`),
  or a notebook's link and its ID. **Find it on Panorama** in the dialog opens Panorama to copy
  it from. The links show as **Raw data on Panorama** and **Results on Panorama** in the
  experiment's section, and each has **Remove**, which changes nothing on Panorama. Needs
  lab-projects' `project.py link`, which reaches every copy on its next sync.
