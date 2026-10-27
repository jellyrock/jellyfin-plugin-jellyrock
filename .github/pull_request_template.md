<!-- Title: "type(scope): summary", for example "fix: stop the reaper from advancing the saved resume position".
     The type places the change in CHANGELOG.md: feat is Added, fix is Fixed, perf, refactor and revert are
     Changed, and chore, ci, build, test, style and docs are left out. scripts/lint-commit-subject.sh checks
     it, and so does CI. On a one-commit squash GitHub uses the commit's subject instead, so make the two match.
     This description does not reach git log: write it for the reviewer. -->

## Overview
<!-- What changed and why, in 1 to 5 sentences, the problem first. -->

## Changes
<!-- One bullet per logical change, naming the files or symbols a reviewer would search for. -->
-

## Testing
<!-- What you ran and what it showed: the test project in the SDK container (docs/dev/README.md),
     scripts/check-server-abi.sh when the server types or members the plugin uses changed, and the Jellyfin
     server and JellyRock app versions you tried. When nothing ran, write "Not tested" and why. -->

## Issues
<!-- Optional. "Fixes #123" closes an issue on merge; "Ref #123" links one. A change that needs a matching
     JellyRock app release links that app PR here. -->
