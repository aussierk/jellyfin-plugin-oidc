# Release process

Releases are automated through GitHub Actions and driven by branch merges — no manual
tagging.

## Channels

- **Stable** (`main`'s `manifest.json`) — updated by `.github/workflows/release.yml` when a
  PR is merged to `main`.
- **Testing** (`dev`'s `manifest.json`) — updated by `.github/workflows/dev-prerelease.yml`
  on every push/merge to `dev`.

See the [README](README.md#release-channels) for the repository URLs users add in Jellyfin.

## Testing builds — automatic

Every merge to `dev` runs `dev-prerelease.yml`:

1. Builds and tests.
2. Publishes a GitHub **pre-release** tagged `v<Version>-rc.<run number>` (e.g.
   `v1.0.8.0-rc.42`) with `oidc-rbac.zip`. The `-rc.<n>` suffix is **only** in the git tag
   and release name — never in the manifest.
3. Writes `dev`'s `manifest.json` with `version` = the plain `<Version>` from
   `Jellyfin.Plugin.OIDC/Jellyfin.Plugin.OIDC.csproj` (e.g. `1.0.8.0`), `sourceUrl` pointing
   at the rc pre-release asset, then commits it back to `dev` with `[skip ci]`.

> Jellyfin parses every manifest `version` with `System.Version` (2–4 dotted integers).
> A value like `1.0.8.0-rc.42` throws *"Version string portion was too short or too long"*
> and breaks the entire plugin catalog. That's why the manifest version stays plain and the
> rc counter lives only in the tag.

Because the manifest version is plain, **each dev merge for the same `<Version>` replaces
that one manifest entry in place** (new bits, same number) until `<Version>` is bumped for
the next cycle. Once `v<Version>` has been released to Stable, `dev-prerelease.yml` refuses
to run until `<Version>` is bumped — otherwise it would overwrite a shipped version's entry
with untested code.

Changelog and `targetAbi` come from the `<Version>` entry in `meta.json`; if it doesn't
exist yet the changelog falls back to `Testing build from <sha>`. Add the real `meta.json`
entry as part of your feature work so testers see meaningful notes.

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

Before promotion, `dev`'s manifest legitimately carries a version `main`'s doesn't (the one
being tested). That's expected divergence, not drift to reconcile. Promotion adds that same
version's final entry to both branches, repointing `sourceUrl` at the full-release asset.
