# Releasing

The maintainer runbook for cutting a release. Two steps, the same flow as the JellyRock app (see
[issue #3](https://github.com/jellyrock/jellyfin-plugin-jellyrock/issues/3)).

## 1. Push a `release-x.y.z` branch

The **Release Preparation** workflow bumps `Directory.Build.props` + `build.yaml` in lockstep, rolls
`CHANGELOG.md`'s `[Unreleased]` into a dated section on that branch, and opens a PR to `main`.

```bash
git switch main && git pull
git switch -c release-0.3.0 && git push -u origin release-0.3.0
```

## 2. Review and merge that PR

`release.yml` then tags `vx.y.z`, packages the plugin with
[jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager), publishes a GitHub release (zip +
`md5`, notes from `CHANGELOG.md`), and refreshes the `manifest.json` served from GitHub Pages so existing
installs auto-update.

## Changelog

Everyday work adds entries under `## [Unreleased]` in `CHANGELOG.md`; the release rolls them up into the
dated section. Keep the `net8`/10.9 history intact under older versions: the floor bumped to `net9`/10.11
at the cold-launch cast release, and servers on 10.9/10.10 stay on the last `net8` build (0.2.x).
