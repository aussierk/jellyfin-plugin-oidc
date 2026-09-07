# Development

## Requirements

- .NET 9.0 SDK

## Build and package

```bash
dotnet publish Jellyfin.Plugin.OIDC -c Release -o publish/

# Zip the output (all DLLs + meta.json)
cd publish && zip -j ../dist/oidc-rbac.zip *.dll meta.json
```

## Tests

```bash
dotnet test
```

### Admin config page (JS)

The plugin's admin config page is written as ES modules under
`Jellyfin.Plugin.OIDC/Configuration/src/` (state, DOM helpers, provider/role-card rendering,
the page controller, etc.) and bundled with [esbuild](https://esbuild.github.io) into the single
`Jellyfin.Plugin.OIDC/Configuration/oidcrbac.js` file Jellyfin actually loads as an embedded
resource. **Edit the files under `src/`, never `oidcrbac.js` directly** - it's generated output.

```bash
npm install
npm test          # unit tests (src/*.test.js) + integration test against the real configPage.html
npm run build      # regenerate oidcrbac.js from src/
npm run verify-build  # build + fail if oidcrbac.js doesn't match what's committed (what CI runs)
```

Requires Node.js. Tests import directly from the small `src/` modules (`state.js`'s
`__setTestState` hook points `cfg`/`libs`/`ratings` at fixture data); `src/index.js` - the page
controller - only exports its default, matching what the Jellyfin admin page actually uses.
CI runs `verify-build` on every PR, so a source change committed without a rebuilt
`oidcrbac.js` fails the build rather than shipping stale JS.

## Project layout

See [ARCHITECTURE.md](ARCHITECTURE.md#project-structure).

## Releasing

Releases are automated from `main`/`dev` merges - see [RELEASING.md](RELEASING.md) for the
CI pipeline and version-bump steps.
