# LabOps

A Windows app for the MacCoss Lab. Its **Projects** area tracks the lab's work in
[lab-projects](https://github.com/uw-maccosslab/lab-projects): labs, their projects (one set of
samples each) and the projects' experiments, from samples received to results returned, with
sample metadata, Octopus plate layouts, and links to the data and notebooks on Panorama. Its
**Quotes** area, for the people who prepare them, works on the Proteomics Services quotes in
[services-quotes](https://github.com/uw-maccosslab/services-quotes). Claude Code does the
paperwork in both, and everything stays in sync with GitHub.

[How LabOps fits together](docs/architecture.md) explains, with figures, how the app works
with the repositories, their engines, Claude Code, GitHub and Panorama.

## Installing

Download the `MacCossLab.LabOps-...-Setup.exe` from the latest
[release](https://github.com/uw-maccosslab/LabOps/releases) and run it. No
administrator rights are needed. The first time it starts, Setup walks through:

1. Installing Git (with Windows' own installer, winget).
2. Signing in to GitHub in your browser, with your lab account.
3. Installing Claude Code and signing in with your lab Claude account.
4. Downloading the lab projects to a folder you choose, or using a copy you already have, and
   the quotes too if your account has access to them.
5. Preparing the engines (downloads Python once; nothing to install yourself).

The app updates itself: when a new version is ready it shows "Update ready: restart to install".

### Coming from ChargeState

LabOps was called ChargeState until 26.7.0, and ChargeState does not update to it: install LabOps
once as above. The first time it starts it takes over ChargeState's settings (your copies of the
lab projects and quotes, and Claude conversations to resume) and its Panorama sign-in, so Setup has
nothing to ask. It then offers to open Windows' Installed apps, where you uninstall ChargeState.

## Using it

Projects:

- **New project:** describe the work or paste an email; Claude sets up the lab, the project and
  its experiments. **New experiment** on a project adds another measurement of its samples.
- **Start / Done / Skip** on each step of the samples' or an experiment's timeline, with a date
  and a note. **Assign** gives a step to a person; **Add a step** covers anything the timeline does
  not show yet.
- Each step has the buttons for its work, and shows what was recorded for it. They work whatever
  the step's status, so a Panorama folder or a notebook can be recorded before the work starts:
  - **Metadata organized:** **Organize with Claude** (choose the collaborator's sample sheet; it
    is checked for identifying information first and never shared, and Claude builds the sample
    table from it) and **View samples** (the table in a grid to sort and search, with the QC rows
    counted, and the deidentified files it was made from).
  - **Plate layout:** **Open in Octopus** and **Import layout**, to lay the samples out on plates
    and keep the layout.
  - **Sample prep** (or an experiment's assay development or data acquisition): **Add notebook**,
    the ELN notebook on Panorama.
  - **Data deposited to Panorama:** **Add raw data folder**, where PanoramaBridge uploads.
  - **Signal processing:** **Add results folder**, with the Skyline documents.
  - **Browse...** finds folders and notebooks on Panorama, signed in the way PanoramaBridge is. A
    timeline without the step keeps its buttons beside its heading.
- **Wiki page:** the project's page on Panorama, as in the BioTRACK folder: status, every step with
  its dates, who and notes, the samples, and the data with their Skyline documents. Preview it,
  have Claude write its summary, plan and description of the samples, and publish it; after that
  the app republishes it after every change. The collaborators who can open the folder read it.
- The list shows active and on-hold projects; **Show closed** adds the closed ones.

Quotes:

- **New quote:** paste the request email, or give a Gmail search that finds it. Claude drafts
  the quote and shows a summary.
- **Ask Claude to change it:** describe the change in plain words.
- **Send / PO received / Invoiced / Declined / Make a revision:** one button each. Send is shown
  only to the approvers listed in the quotes repository.
- Everything you do is saved to GitHub right away, and other people's work arrives on its own.

## Developing

See [docs/](docs/README.md) for how the pieces fit together, and [CLAUDE.md](CLAUDE.md) for
working on the code.
