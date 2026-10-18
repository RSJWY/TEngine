#!/usr/bin/env bash
# =============================================================================
# sync-unitask-local.sh
#
# Installs/upgrades UniTask as a local (embedded) package at
# Packages/com.cysharp.unitask.
#
# NOTE: unlike HybridCLR/Obfuz, UniTask's package root is NOT the repository
# root; upstream serves it from the ?path= subdirectory below, so the script
# copies that subdirectory into the target package folder.
#
# Usage:
#   ./sync-unitask-local.sh               install/upgrade to the latest stable tag
#   ./sync-unitask-local.sh 2.5.11        install/upgrade to a specific tag or branch
#   ./sync-unitask-local.sh <full-commit> install/upgrade to a specific commit
#
# Pulls from GitHub by default; set SYNC_UNITASK_REPO to switch mirror:
#   SYNC_UNITASK_REPO=https://gitee.com/cysharp/UniTask.git ./sync-unitask-local.sh
#
# Idempotent: every run is a clean re-sync.
# =============================================================================
set -euo pipefail

# This script lives in Packages/; switch to the project root first.
cd "$(dirname "$0")/.."

REPO="${SYNC_UNITASK_REPO:-https://github.com/Cysharp/UniTask.git}"
# Upstream package location inside the repository (matches the old
# "?path=src/UniTask/Assets/Plugins/UniTask" git reference).
SUBDIR="src/UniTask/Assets/Plugins/UniTask"
TARGET="Packages/com.cysharp.unitask"
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

echo "==> Fetching UniTask (ref: $REF) ..."
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
sed -i 's#"com\.cysharp\.unitask":[[:space:]]*"[^"]*"#"com.cysharp.unitask": "file:com.cysharp.unitask"#' "$MANIFEST"

VERSION="$(sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TARGET/package.json" | head -1)"
echo
echo "Done: UniTask ${VERSION:-unknown} (ref: $REF) installed as a local package at $TARGET"
echo "      manifest.json now points to the local package."
echo "      Switch back to Unity and wait for recompilation."
