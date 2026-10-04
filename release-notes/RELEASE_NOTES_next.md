# ChargeState vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

## Performance

- **Clicks show their result about three times sooner.** A step update took about 7.4 seconds
  before the page showed it, and now takes about 2.4 (measured with GitHub's real delays: 0.6 s
  per fetch, about 1.5 s per push).
  - The change is committed on this computer and shown at once. Sharing it with GitHub (a fetch,
    a rebase and a push, about 3 seconds) happens in the background, and the status bar shows
    it. Offline, the commit waits on this computer and the next sync shares it.
  - No fetch before an action when the copy synced in the last minute, which is the usual case
    when going through several steps.
  - The list is read once per action instead of three times.
  - lab-projects' pre-commit hook no longer repeats the identifier check the app has just run on
    the same staged files (lab-projects engine change).
  - The log records how long each engine command, commit and sync took.
- After a Claude turn, the app still waits for GitHub before it says "Saved and shared".

## Breaking Changes
