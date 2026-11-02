# ADR 0002: The feature catalog's shape

- **Status:** Accepted
- **Date:** 2026-11-02
- **Deciders:** maintainer

## Context

The jellyrock.app Companion page builds from `docs/feature-catalog.json` in this repo, the way its app
cards come from the app's own catalog. The site only fails on a catalog it cannot read, so this repo has
to keep the shape stable and the data true.

## Decision

- The frontmatter of `docs/features/*.md` is the source. `scripts/feature-catalog.sh` writes the
  catalog, the README table and `build.yaml`'s `description`, and CI's **Feature catalog** check fails
  when any of them is stale.
- `schema: 1`. A breaking change bumps it; an added field does not.
- `plugin.server` is `targetAbi` from `build.yaml`, in three parts. That's the floor Jellyfin offers a
  version by, not the `.csproj` package version.
- Each feature has `sinceDate` from its `CHANGELOG.md` release heading, `null` while unreleased.
- The release branch stamps `since: unreleased` (`set-version.sh`), so nothing pushes to `main` after
  the tag.
- No `manifest` field: the site links the install steps on docs.jellyrock.app.

## Consequences

A feature page is the one place to edit. A new field is free; renaming or removing one needs a
`schema` bump and a matching site change.

## Alternatives considered

- `plugin.server` from the `Jellyfin.Controller` version: rejected, because Jellyfin offers a version
  by `targetAbi`.
- Stamping `since` after the tag with a push to `main`: rejected, because the stamps should be reviewed
  in the release PR.
- A `manifest` field: rejected, because nothing would read it.
