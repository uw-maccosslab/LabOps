# Release Notes

This directory contains per-version release notes for ChargeState. The convention is the one
Skyline-PRISM uses; this file is the canonical description of it for this repository.

## Versioning Scheme

ChargeState uses a `YY.feature.patch` versioning convention:

- **YY**: Two-digit year (e.g., `26` for 2026)
- **feature**: Incremented for each release containing new features
- **patch**: Incremented for bug-fix-only releases within the same feature version

Examples: `26.1.0` (first feature release of 2026), `26.1.1` (patch), `26.2.0` (second feature release).

The version lives in exactly one place, `<Version>` in `Directory.Build.props`, and is bumped only
at release time, not during development. `release.yml` refuses to publish when the tag disagrees
with it.

## File Format

Each release gets one file: `RELEASE_NOTES_v{version}.md`. During development, the unreleased draft
lives in `RELEASE_NOTES_next.md` and gets renamed at release time.

```text
release-notes/
  README.md                  # this file
  RELEASE_NOTES_next.md      # working draft for the next release
  RELEASE_NOTES_v26.1.0.md
  RELEASE_NOTES_v26.1.1.md
```

> [!NOTE]
> `RELEASE_NOTES_v26.1.0.md` and `RELEASE_NOTES_v26.1.1.md` predate this convention (their headings
> differ). They are left as published, because each was served verbatim as its GitHub Release
> description; editing them now would only make the repository disagree with what people read.

## Writing Release Notes

### During Development

Maintain `RELEASE_NOTES_next.md` as a working draft for the next planned version. Append entries
**in the same commit as the change**, not at release time. **User-visible** means behavior, a
message someone reads, a button or setting, or performance they would notice. Refactors, tests and
documentation get no entry; say so in the commit message instead, so the omission reads as a
decision rather than an oversight. The file stays unversioned until the release is finalized, so the
target version can still change (a planned patch release becomes a feature release once new
functionality lands).

### Content Structure

```markdown
# ChargeState v{version}

One-sentence summary of the release.

## New Features

- Grouped by area (Quotes, Claude, Syncing, Setup, Updates)
- Focus on what changed from the user's perspective, not implementation details

## Bug Fixes

- The bug, its impact, and what was fixed

## Performance

- Improvements with context ("the quote list loads in 1 s instead of 4 s")

## Breaking Changes

- Anything requiring user action. Omit the section when there is nothing.
```

Sections can be omitted if empty. For patch releases, a flat list is sufficient.

> [!IMPORTANT]
> **Delete the empty headings when you rename the draft.** The rolling draft
> (`RELEASE_NOTES_next.md`) is seeded with all four headings so entries have somewhere to go during
> development, which means a renamed draft always arrives carrying the ones nobody filled in.
> Removing them is a step of the release: the file is published verbatim as the GitHub Release
> description.

### Style

- Write in past tense ("Added", "Fixed", "Removed")
- Lead with user impact, not implementation details
- Include specific numbers where relevant (sizes, times, counts)
- Name buttons and settings exactly as the user sees them (**Open PDF**, `"BetaUpdates"`)
- Reference modified files with paths so reviewers can locate the change
- American English, no em dashes

## Release Process

1. Finalize `RELEASE_NOTES_next.md`; `git mv` it to `RELEASE_NOTES_v{version}.md`, update its
   heading, **delete every section heading with no entries under it**, and create a fresh
   `RELEASE_NOTES_next.md` seeded with the four headings
2. Bump `<Version>` in `Directory.Build.props` to `{version}`
3. Commit and push to `main`, and let CI go green
4. Tag: `git tag -a v{version} -m "ChargeState {version}"`
5. Push the tag: `git push origin v{version}`. **Pushing the tag both builds the installer and
   creates the GitHub Release** (`.github/workflows/release.yml`). Do not hand-create the Release.

A tag containing `alpha`, `beta`, or `rc` is published as a GitHub prerelease. A tag containing
`beta` also goes to the `win-beta` update channel, for trying a release on one computer before
everyone gets it (set `"BetaUpdates": true` in `%LOCALAPPDATA%\ChargeState\settings.json` there);
every other tag goes to the stable `win` channel.

If a release changes what the app needs from the quotes repository, raise `min_app_version` in
that repository's `config/app.yaml` after the release is out; older copies then tell their users to
update.

> [!IMPORTANT]
> **This file becomes the GitHub Release description.** `release.yml` publishes
> `release-notes/RELEASE_NOTES_v{version}.md` verbatim as the Release body, so write it for the people
> reading the Releases page. Step 1's rename therefore has to happen **before** tagging: the workflow
> resolves the path from the tag and fails with an explicit message if the file is missing, after the
> tests have run.
>
> To fix an existing Release:
> `gh release edit v<version> --notes-file release-notes/RELEASE_NOTES_v<version>.md`

## What the Release Workflow Produces

Velopack packs the publish folder into:

| Asset | Purpose |
|---|---|
| `MacCossLab.ChargeState-win-Setup.exe` | Per-user installer; no administrator rights needed |
| `MacCossLab.ChargeState-{version}-full.nupkg` | Full package, for first installs and as a delta base |
| `MacCossLab.ChargeState-{version}-delta.nupkg` | Difference from the previous release (270 KB for v26.1.1) |
| `MacCossLab.ChargeState-win-Portable.zip` | Portable copy for computers where installing is not an option |
| `releases.win.json` | The update feed installed copies read |
| `SHA256SUMS.txt` | Checksums for every asset |

Installed copies check the feed at startup and every four hours, download in the background, and
show **Update ready: restart to install**. The app never restarts itself.
