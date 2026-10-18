#!/usr/bin/env bash
# =============================================================================
# sync-yooasset-local.sh
#
# Installs/upgrades YooAsset as a local (embedded) package at
# Packages/com.tuyoogame.yooasset.
#
# NOTE: like UniTask, YooAsset's package root is NOT the repository root;
# upstream serves it from the Assets/YooAsset subdirectory, so the script
# copies that subdirectory into the target package folder.
#
# NOTE (GUID stability): upstream ships .meta files with stable asmdef GUIDs
# (YooAsset.asmdef => e34a5702dd353724aa315fb8011f08c3,
#  YooAsset.Editor.asmdef => 4d1926c9df5b052469a1c63448b7609a). This project's
# asmdefs (TEngine.Runtime / TEngine.Editor / GameLogic / RuntimeTools /
# YooAsset.Custom.Editor) reference those assemblies by GUID, so a clean
# re-sync keeps every reference intact.
#
# Usage:
#   ./sync-yooasset-local.sh               install/upgrade to the latest stable tag
#   ./sync-yooasset-local.sh 3.0.6         install/upgrade to a specific tag or branch
#   ./sync-yooasset-local.sh <full-commit> install/upgrade to a specific commit
#
# Pulls from GitHub by default; set SYNC_YOOASSET_REPO to switch mirror:
#   SYNC_YOOASSET_REPO=<mirror-url> ./sync-yooasset-local.sh
#
# Idempotent: every run is a clean re-sync.
# =============================================================================
set -euo pipefail

# This script lives in Packages/; switch to the project root first.
cd "$(dirname "$0")/.."

REPO="${SYNC_YOOASSET_REPO:-https://github.com/tuyoogame/YooAsset.git}"
# Upstream package location inside the repository.
SUBDIR="Assets/YooAsset"
TARGET="Packages/com.tuyoogame.yooasset"
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

echo "==> Fetching YooAsset (ref: $REF) ..."
mkdir "$TMP_DIR/repo"
git -C "$TMP_DIR/repo" init -q
git -C "$TMP_DIR/repo" remote add origin "$REPO"
git -C "$TMP_DIR/repo" fetch -q --depth 1 origin "$REF"
git -C "$TMP_DIR/repo" checkout -q FETCH_HEAD

if [ ! -f "$TMP_DIR/repo/$SUBDIR/package.json" ]; then
  echo "[ERROR] $SUBDIR/package.json not found in the fetched ref '$REF'."
  echo "        The upstream layout may have changed; update SUBDIR in this script."
  exit 1
fi

echo "==> Syncing $SUBDIR to $TARGET ..."
rm -rf "$TARGET"
mkdir -p "$TARGET"
cp -r "$TMP_DIR/repo/$SUBDIR/." "$TARGET/"

echo "==> Updating $MANIFEST to the local file reference ..."
sed -i 's#"com\.tuyoogame\.yooasset":[[:space:]]*"[^"]*"#"com.tuyoogame.yooasset": "file:com.tuyoogame.yooasset"#' "$MANIFEST"

VERSION="$(sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TARGET/package.json" | head -1)"
echo
echo "Done: YooAsset ${VERSION:-unknown} (ref: $REF) installed as a local package at $TARGET"
echo "      manifest.json now points to the local package."
echo "      Switch back to Unity and wait for recompilation."
