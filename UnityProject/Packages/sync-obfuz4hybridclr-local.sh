#!/usr/bin/env bash
# =============================================================================
# sync-obfuz4hybridclr-local.sh
#
# Installs/upgrades Obfuz4HybridCLR as a local (embedded) package at
# Packages/com.code-philosophy.obfuz4hybridclr.
#
# NOTE: obfuz4hybridclr itself does NOT bundle dnlib.dll, so there is no
# dnlib to remove here. It compiles against the custom dnlib kept by
# sync-obfuz-local.sh (removed from HybridCLR by sync-hybridclr-local.sh).
# The package root is the repository root (no ?path= subdirectory).
#
# Usage:
#   ./sync-obfuz4hybridclr-local.sh               install/upgrade to the latest stable tag
#   ./sync-obfuz4hybridclr-local.sh v3.0.1        install/upgrade to a specific tag or branch
#   ./sync-obfuz4hybridclr-local.sh <full-commit> install/upgrade to a specific commit
#
# Pulls from GitHub by default; set SYNC_OBFUZ4HYBRIDCLR_REPO to switch mirror:
#   SYNC_OBFUZ4HYBRIDCLR_REPO=https://gitee.com/focus-creative-games/obfuz4hybridclr.git ./sync-obfuz4hybridclr-local.sh
#
# Idempotent: every run is a clean re-sync.
# =============================================================================
set -euo pipefail

# This script lives in Packages/; switch to the project root first.
cd "$(dirname "$0")/.."

REPO="${SYNC_OBFUZ4HYBRIDCLR_REPO:-https://github.com/focus-creative-games/obfuz4hybridclr.git}"
TARGET="Packages/com.code-philosophy.obfuz4hybridclr"
MANIFEST="Packages/manifest.json"

# Without an argument, resolve the latest stable tag (plain numeric versions
# preferred; falls back to the latest tag of any kind).
if [ $# -ge 1 ]; then
  REF="$1"
else
  echo "==> No version specified, querying the latest tag ..."
  TAGS="$(git ls-remote --tags "$REPO" | sed -n 's#.*refs/tags/##p' | grep -vE '\^\{\}')"
  REF="$(echo "$TAGS" | grep -E '^v?[0-9]+(\.[0-9]+)*$' | sort -V | tail -1)"
  [ -n "$REF" ] || REF="$(echo "$TAGS" | sort -V | tail -1)"
  [ -n "$REF" ] || { echo "[ERROR] No tag found on the remote repo; please specify a version."; exit 1; }
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "==> Fetching obfuz4hybridclr (ref: $REF) ..."
mkdir "$TMP_DIR/repo"
git -C "$TMP_DIR/repo" init -q
git -C "$TMP_DIR/repo" remote add origin "$REPO"
git -C "$TMP_DIR/repo" fetch -q --depth 1 origin "$REF"
git -C "$TMP_DIR/repo" checkout -q FETCH_HEAD

echo "==> Syncing to $TARGET ..."
rm -rf "$TARGET"
mkdir -p "$TARGET"
cp -r "$TMP_DIR/repo/." "$TARGET/"
rm -rf "$TARGET/.git"

echo "==> Updating $MANIFEST to the local file reference ..."
sed -i 's#"com\.code-philosophy\.obfuz4hybridclr":[[:space:]]*"[^"]*"#"com.code-philosophy.obfuz4hybridclr": "file:com.code-philosophy.obfuz4hybridclr"#' "$MANIFEST"

VERSION="$(sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TARGET/package.json" | head -1)"
echo
echo "Done: Obfuz4HybridCLR ${VERSION:-unknown} (ref: $REF) installed as a local package at $TARGET"
echo "      No dnlib.dll bundled here, manifest.json now points to the local package."
echo "      Switch back to Unity and wait for recompilation."
