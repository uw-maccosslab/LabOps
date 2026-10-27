# LabOps vNEXT Release Notes

Working draft for the next release. Append entries in the same commit as each user-visible change;
rename to `RELEASE_NOTES_v{version}.md` at release time. The release workflow publishes this file as
the GitHub Release description and fails if it is missing.

## New Features

## Bug Fixes

- Fixed being stuck with someone else's GitHub account when LabOps was installed on a computer where
  GitHub was already signed in. Setup's GitHub sign-in step now keeps a **Use a different account**
  button after it is done, and the Git identity step keeps **Use my GitHub name**, so the changes you
  save carry your name after switching. When a GH_TOKEN or GITHUB_TOKEN environment variable picks
  the account, the step says so and what to remove.

## Performance

## Breaking Changes
