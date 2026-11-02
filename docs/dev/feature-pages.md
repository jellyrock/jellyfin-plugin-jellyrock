# Feature pages

Each plugin feature has one page in `docs/features/`. Its frontmatter feeds a catalog that the
jellyrock.app Companion page reads. The same fields fill the README table and the plugin description in
`build.yaml`, so you edit the page and nothing else.

## Add a feature

A `feat` PR that adds a capability adds its page in the same PR, with `since: unreleased`. Start the
file with this block, then write the page as usual:

```markdown
---
title: Cast while JellyRock is closed
pitch: Cast from a Jellyfin app to your Roku while JellyRock is closed.
since: unreleased
jellyrock: 2.24.0
http: needed
https: needed
---
```

Then run `scripts/feature-catalog.sh` and commit what it writes.

## The six fields

Write each field as `key: value` on one line, without quotes. The script rejects any other field.

| Field | What to write |
| --- | --- |
| `title` | The feature's name for a reader. It must not be empty. |
| `pitch` | One sentence on what the reader gets, at most 120 characters. |
| `since` | `unreleased`, or the plugin version (`x.y.z`) that first shipped it. A version needs a dated heading in `CHANGELOG.md`. |
| `jellyrock` | The oldest JellyRock app version that works with it (`x.y.z`). |
| `http` | `needed` if an HTTP server needs the plugin for this feature, else `not-needed`. |
| `https` | The same for an HTTPS server. |

## What the script writes

`scripts/feature-catalog.sh` reads every page and writes three things:

- `docs/feature-catalog.json`, the catalog. The site reads it. Features are sorted by title, and each
  one carries `sinceDate`, the date of its release in `CHANGELOG.md` (`null` while unreleased). The
  server version comes from `targetAbi` in `build.yaml`.
- The table in `README.md`, between the `generated features` markers. A feature that is still
  unreleased shows "(next release)" after its title.
- The `description` in `build.yaml`.

It lists every invalid page before it fails, each message starting with the file name.

## Releases stamp `since`

You do not write a version into `since`. `scripts/set-version.sh` runs the generator with `--release`
on the release branch, which changes `since: unreleased` to the release version in every page that
still has it. The stamps show in the release PR.

## The check

CI's **Feature catalog** job runs the generator's tests, then `scripts/feature-catalog.sh --check`.
The check writes nothing. It fails when a page changed and an output did not, and it names each stale
file. Run `scripts/feature-catalog.sh` and commit the result.

When `docs/feature-catalog.json` changes on `main`, the **Notify homepage** workflow checks the catalog
again and tells jellyrock.app to rebuild.
