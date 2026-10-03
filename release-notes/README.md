# Release notes

One file per version of Services Quotes, following the PanoramaBridge process.

## Versioning

`YY.feature.patch`, the same CalVer as PanoramaBridge: `26.10.0` is the first release, `26.10.1`
a bug-fix release, `26.11.0` the next feature release. The version lives only in
`Directory.Build.props`; `release.yml` refuses to publish when the tag disagrees with it.

## Files

```text
release-notes/
  README.md                  this file
  RELEASE_NOTES_next.md      working draft for the next release
  RELEASE_NOTES_v26.10.0.md  one per published version
```

## Writing them

Add to `RELEASE_NOTES_next.md` in the same commit as any change a user could notice: behavior, a
message someone reads, a setting, or performance. Refactors, tests and documentation get no entry;
say so in the commit message instead.

```markdown
# Services Quotes v{version}

One-sentence summary.

## New features
## Bug fixes
## Changes that need action
```

Omit empty sections. Write in the past tense, lead with what the user sees, and name buttons and
settings as they appear in the app. Use American spelling.

## Releasing

**The notes file becomes the GitHub Release description,** published verbatim, so write it for the
people reading the Releases page.

1. Finalize `RELEASE_NOTES_next.md`, rename it to `RELEASE_NOTES_v{version}.md`, update its heading,
   and create a fresh empty draft.
2. Set `<Version>` in `Directory.Build.props` to `{version}`.
3. Commit and push to `main`.
4. `git tag v{version}` and `git push origin v{version}`. Pushing the tag builds the installer and
   creates the Release; do not create it by hand.

A tag containing `alpha`, `beta` or `rc` is marked a prerelease. A tag containing `beta` also goes
to the `win-beta` update channel, for trying a release on one computer before everyone gets it
(set `"BetaUpdates": true` in `%LOCALAPPDATA%\ServicesQuotes\settings.json` on that computer).

If a release changes what the app needs from the quotes repository, raise `min_app_version` in
that repository's `config/app.yaml` after the release is out; older copies then tell their users
to update.

## What a release contains

| Asset | Purpose |
|---|---|
| `MacCossLab.ServicesQuotes-win-Setup.exe` | Per-user installer; no administrator rights needed |
| `MacCossLab.ServicesQuotes-{version}-full.nupkg` | Full package, for first installs and as a delta base |
| `MacCossLab.ServicesQuotes-{version}-delta.nupkg` | Difference from the previous release |
| `releases.win.json` | The update feed installed copies read |
| `SHA256SUMS.txt` | Checksums for every asset |

Installed copies check the feed at startup, download in the background, and show
"Update ready: restart to install". The app never restarts itself.
