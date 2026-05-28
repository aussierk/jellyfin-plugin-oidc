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

if [ -n "$REPO_URL" ]; then
    SOURCE_URL="${REPO_URL%/}/oidc-rbac.zip"
else
    SOURCE_URL=""
fi

cat > "$REPO_DIR/manifest.json" <<EOF
[
  {
    "guid": "d4e5f6a7-b8c9-0d1e-2f3a-4b5c6d7e8f90",
    "name": "SSO-OIDC RBAC",
    "description": "Advanced OIDC authentication with role-based library access control",
    "overview": "OpenID Connect SSO with role-to-library mapping, multi-provider support, and admin UI.",
    "owner": "Ezeqielle",
    "category": "Authentication",
    "versions": [
      {
        "version": "1.0.2.0",
        "changelog": "Fix OidcAuthProvider not registered with Jellyfin DI; fix new user creation crashing on ChangePassword; fix MaxParentalRating defaulting to 0; fix credential storage missing ServerId",
        "targetAbi": "10.11.0.0",
        "sourceUrl": "$SOURCE_URL",
        "checksum": "$CHECKSUM",
        "timestamp": "$TIMESTAMP"
      }
    ]
  }
]
EOF

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
