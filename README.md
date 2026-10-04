# ChargeState

A Windows app for the MacCoss Lab. Its **Projects** area tracks the lab's work in
[lab-projects](https://github.com/uw-maccosslab/lab-projects): labs, their projects (one set of
samples each) and the projects' experiments, from samples received to results returned, with
sample metadata, Octopus plate layouts, and links to the data and notebooks on Panorama. Its
**Quotes** area, for the people who prepare them, works on the Proteomics Services quotes in
[services-quotes](https://github.com/uw-maccosslab/services-quotes). Claude Code does the
paperwork in both, and everything stays in sync with GitHub.

[How ChargeState fits together](docs/architecture.md) explains, with figures, how the app works
with the repositories, their engines, Claude Code, GitHub and Panorama.

## Installing

Download `MacCossLab.ChargeState-win-Setup.exe` from the latest
[release](https://github.com/uw-maccosslab/ChargeState/releases) and run it. No
administrator rights are needed. The first time it starts, Setup walks through:

1. Installing Git (with Windows' own installer, winget).
2. Signing in to GitHub in your browser, with your lab account.
3. Installing Claude Code and signing in with your lab Claude account.
4. Downloading the lab projects to a folder you choose, or using a copy you already have, and
   the quotes too if your account has access to them.
5. Preparing the engines (downloads Python once; nothing to install yourself).

The app updates itself: when a new version is ready it shows "Update ready: restart to install".

## Using it

Projects:

- **New project:** describe the work or paste an email; Claude sets up the lab, the project and
  its experiments. **New experiment** on a project adds another measurement of its samples.
- **Start / Done / Skip** on each step of the samples' or an experiment's timeline, with a date
  and a note. **Assign** gives a step to a person; **Add a step** covers anything the timeline does
  not show yet.
- **Add link:** record where an experiment's raw data and results are on Panorama, or its ELN
  notebook. **Browse...** finds them on Panorama, signed in the way PanoramaBridge is.
- **Organize metadata with Claude:** choose the collaborator's sample sheet. It is checked for
  identifying information first and never shared; Claude builds the sample table from it.
- **Open in Octopus / Import Octopus layout:** lay the samples out on plates and keep the layout.

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
