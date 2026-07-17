# Release process

There's no branch protection on this repo and no other maintainers, so releases don't go through PRs — just direct pushes/merges.

## Channels

- **Stable** (`main`'s `manifest.json`) — updated only by full-release tags.
- **Testing** (`dev`'s `manifest.json`) — updated by release-candidate tags. See the [README](README.md#release-channels) for the repository URLs users add in Jellyfin.

`.github/workflows/release.yml` resolves whichever branch a tag's commit lives on and updates that branch's `manifest.json` only. A non-hyphenated full-release tag additionally copies the manifest to `main` if it wasn't already on `main` — so as long as full releases are tagged from `main`, that fallback never triggers.

## Cutting a testing (RC) build

1. Do feature work on `dev` (or a short-lived branch merged into `dev` with a plain `git merge`).
2. Bump `<Version>` in `Jellyfin.Plugin.OIDC/Jellyfin.Plugin.OIDC.csproj` and add/update the matching entry in `Jellyfin.Plugin.OIDC/meta.json`.
3. Tag from `dev` with a hyphenated suffix and push the tag:
   ```
   git tag v1.0.6-rc.1
   git push origin v1.0.6-rc.1
   ```
4. CI builds, tests, and updates `dev`'s `manifest.json` only. Testers on the Testing repository URL see it immediately.
5. Iterate (`v1.0.6-rc.2`, ...) as needed. Re-pushing a tag with the same csproj `<Version>` replaces that manifest entry in place.

## Promoting to stable

1. Merge `dev` into `main` directly (no PR):
   ```
   git checkout main
   git merge dev
   git push origin main
   ```
2. Tag the full release from `main` (no hyphen):
   ```
   git tag v1.0.6
   git push origin v1.0.6
   ```
3. CI updates `main`'s `manifest.json` — Stable channel users see the new version.

## Expected state between releases

Once an RC has shipped but hasn't been promoted yet, `dev`'s manifest legitimately has entries `main`'s doesn't. That's expected divergence, not drift to reconcile.
