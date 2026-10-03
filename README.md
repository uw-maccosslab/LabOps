# Services Quotes

A Windows app for MacCoss Lab Proteomics Services quotes. It keeps the
[services-quotes](https://github.com/uw-maccosslab/services-quotes) repository in sync with
GitHub, lists and searches every quote, and drafts and revises quotes with Claude Code.

## Installing

Download `MacCossLab.ServicesQuotes-win-Setup.exe` from the latest
[release](https://github.com/uw-maccosslab/services-quotes-app/releases) and run it. No
administrator rights are needed. The first time it starts, Setup walks through:

1. Installing Git (with Windows' own installer, winget).
2. Signing in to GitHub in your browser, with an account that has access to the quotes.
3. Installing Claude Code and signing in with your lab Claude account.
4. Downloading the quotes to a folder you choose, or using a copy you already have.
5. Preparing the quote engine (downloads Python once; nothing to install yourself).

The app updates itself: when a new version is ready it shows "Update ready: restart to install".

## Using it

- **New quote:** paste the request email, or give a Gmail search that finds it. Claude drafts
  the quote and shows a summary.
- **Ask Claude to change it:** describe the change in plain words.
- **Send / PO received / Invoiced / Declined / Make a revision:** one button each. Send is shown
  only to the approvers listed in the quotes repository.
- Everything you do is saved to GitHub right away, and other people's work arrives on its own.

## Developing

See [CLAUDE.md](CLAUDE.md).
