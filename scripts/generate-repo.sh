#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_DIR="$REPO_ROOT/dist"
REPO_DIR="$REPO_ROOT/repo"
ZIP_FILE="$BUILD_DIR/oidc-rbac.zip"

REPO_URL="${1:-}"

if [ ! -f "$ZIP_FILE" ]; then
    echo "Error: $ZIP_FILE not found. Run 'make package' first." >&2
    exit 1
fi

mkdir -p "$REPO_DIR"
cp "$ZIP_FILE" "$REPO_DIR/"

CHECKSUM=$(md5sum "$ZIP_FILE" | cut -d' ' -f1)
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
META_FILE="$REPO_ROOT/Jellyfin.Plugin.OIDC/meta.json"
VERSION=$(jq -r '.versions[0].version' "$META_FILE")
CHANGELOG=$(jq -r '.versions[0].changelog' "$META_FILE")

if [ -n "$REPO_URL" ]; then
    SOURCE_URL="${REPO_URL%/}/oidc-rbac.zip"
else
    SOURCE_URL=""
fi

jq -n \
  --arg version    "$VERSION" \
  --arg changelog  "$CHANGELOG" \
  --arg sourceUrl  "$SOURCE_URL" \
  --arg checksum   "$CHECKSUM" \
  --arg timestamp  "$TIMESTAMP" \
  '[{
    "guid": "e1c020c5-3972-4b7b-9538-ee4934cc902c",
    "name": "SSO-OIDC Authentication",
    "description": "Advanced OIDC authentication with role-based library access control",
    "overview": "OpenID Connect SSO with role-to-library mapping, multi-provider support, and a full admin configuration UI.",
    "owner": "aussierk",
    "category": "Authentication",
    "versions": [
      {
        "version": $version,
        "changelog": $changelog,
        "targetAbi": "10.11.0.0",
        "sourceUrl": $sourceUrl,
        "checksum": $checksum,
        "timestamp": $timestamp
      }
    ]
  }]' > "$REPO_DIR/manifest.json"

echo "Repository generated in $REPO_DIR/"
echo "  manifest.json  - add this URL to Jellyfin > Plugins > Repositories"
echo "  oidc-rbac.zip  - plugin package"
echo ""
if [ -n "$REPO_URL" ]; then
    echo "Repository URL for Jellyfin: ${REPO_URL%/}/manifest.json"
else
    echo "To serve locally:"
    echo "  cd $REPO_DIR && python3 -m http.server 8080"
    echo "  Then add http://YOUR_HOST:8080/manifest.json to Jellyfin"
fi
