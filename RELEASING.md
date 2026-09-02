# Release process

Releases are automated through GitHub Actions and driven by branch merges — no manual
tagging.

## Channels

- **Stable** (`main`'s `manifest.json`) — updated by `.github/workflows/release.yml` when a
  PR is merged to `main`.
- **Testing** (`dev`'s `manifest.json`) — updated by `.github/workflows/dev-prerelease.yml`
  on every push/merge to `dev`.

See the [README](README.md#release-channels) for the repository URLs users add in Jellyfin.

## Testing (RC) builds — automatic

Every merge to `dev` runs `dev-prerelease.yml`:

1. Builds and tests.
2. Version = `<Version>` from `Jellyfin.Plugin.OIDC/Jellyfin.Plugin.OIDC.csproj` plus
   `-rc.<GitHub run number>` (e.g. `1.0.8.0-rc.42`).
3. Publishes a GitHub **pre-release** tagged `v<version>` with `oidc-rbac.zip`.
4. Prepends the entry to `dev`'s `manifest.json` and commits it back to `dev` with
   `[skip ci]`.

Changelog text and `targetAbi` for the RC come from the `<Version>` (base, non-`-rc`) entry
in `meta.json`; if that entry doesn't exist yet the changelog falls back to
`Development build from <sha>`. Add the real `meta.json` entry as part of your feature work
so RC testers see meaningful notes.

## Full releases — automatic on merge to `main`

`release.yml` runs on every push to `main` but is **version-gated**: it only releases when
the tag `v<Version>` does not already exist. A docs-only or chore merge that doesn't touch
`<Version>` is a no-op.

To cut a release:

1. Open a PR from `dev` (or a branch) into `main` that includes:
   - a bump to `<Version>` in `Jellyfin.Plugin.OIDC/Jellyfin.Plugin.OIDC.csproj`, and
   - a matching entry in `Jellyfin.Plugin.OIDC/meta.json` (version + changelog + targetAbi).
     The workflow **fails** if this entry is missing.
2. Merge the PR. `release.yml` then:
   - builds and tests,
   - creates tag `v<Version>` and a GitHub **release** with `oidc-rbac.zip`,
   - prepends the entry to `main`'s `manifest.json` (Stable) and commits it back with
     `[skip ci]`,
   - syncs the same entry into `dev`'s `manifest.json` so Testing never regresses behind
     Stable.

## Re-running a build

Re-running a workflow with the same version replaces that entry in the target manifest
in place rather than adding a duplicate.

## Expected state between releases

After an RC ships but before promotion, `dev`'s manifest legitimately has `-rc` entries
`main`'s doesn't. That's expected divergence, not drift to reconcile. Promotion adds the
final (non-`-rc`) entry to both branches.
