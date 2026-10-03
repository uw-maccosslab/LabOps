# ChargeState v26.1.0

The first release: a Windows app for MacCoss Lab Proteomics Services quotes, backed by the
services-quotes repository on GitHub.

## New features

- **Setup.** A checklist installs and signs in to everything the app needs (Git, GitHub, Claude
  Code) and downloads the quotes, or uses a copy of the quotes you already have. The GitHub tool
  and uv come with the app.
- **Quote list and search.** All quotes and historical estimates, filtered by status, with search
  across every word in each quote.
- **New quote.** Paste the request email or give a Gmail search; Claude drafts the quote, asks
  about anything missing, and shows a summary with the total, the per-sample cost, and anything to
  check.
- **Ask Claude to change it.** Describe a change in plain words. Sent quotes get a revision first.
- **Send, PO received, Invoiced, Declined, Make a revision, Draft PDF.** Each is one button, and
  only approvers listed in the quotes repository see Send.
- **Automatic syncing.** Every change is saved to GitHub right away, and other people's changes
  arrive in the background. Two people working on different quotes never get in each other's way;
  if two people change the same quote, the app keeps both versions and offers to redo yours with
  Claude.
- **Updates.** The app downloads new versions in the background and offers to restart.
