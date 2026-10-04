# ChargeState

A Windows app for the MacCoss Lab. Its **Projects** area tracks the lab's collaborations and
experiments in [lab-projects](https://github.com/uw-maccosslab/lab-projects), from samples received
to results returned, with sample metadata and Octopus plate layouts. Its **Quotes** area, for the
people who prepare them, works on the Proteomics Services quotes in
[services-quotes](https://github.com/uw-maccosslab/services-quotes). Claude Code does the
paperwork in both, and everything stays in sync with GitHub.

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

- **New experiment:** describe the work or paste an email; Claude sets it up.
- **Start / Done / Skip** on each stage of an experiment's timeline, with a date and a note.
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

See [CLAUDE.md](CLAUDE.md).
