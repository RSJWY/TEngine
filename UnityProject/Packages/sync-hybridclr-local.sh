#!/usr/bin/env bash
# =============================================================================
# sync-hybridclr-local.sh
#
# Installs/upgrades HybridCLR as a local (embedded) package at
# Packages/com.code-philosophy.hybridclr, and removes its bundled
# Plugins/dnlib.dll to resolve the dnlib conflict with Obfuz.
# (Obfuz's dnlib is a feature superset of upstream dnlib and is shared
#  by the whole project; obfuz4hybridclr depends on its custom types
#  such as PolymorphicWriter.)
#
# Usage:
#   ./sync-hybridclr-local.sh               install/upgrade to the latest stable tag
#   ./sync-hybridclr-local.sh v8.13.0       install/upgrade to a specific tag or branch
#   ./sync-hybridclr-local.sh <full-commit> install/upgrade to a specific commit
#
# Pulls from GitHub by default; set SYNC_HYBRIDCLR_REPO to switch mirror:
#   SYNC_HYBRIDCLR_REPO=https://gitee.com/focus-creative-games/hybridclr_unity.git ./sync-hybridclr-local.sh
#
# Idempotent: every run is a clean re-sync.
# =============================================================================
set -euo pipefail

# This script lives in Packages/; switch to the project root first.
cd "$(dirname "$0")/.."

REPO="${SYNC_HYBRIDCLR_REPO:-https://github.com/focus-creative-games/hybridclr_unity.git}"
TARGET="Packages/com.code-philosophy.hybridclr"
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

echo "==> Fetching hybridclr_unity (ref: $REF) ..."
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

echo "==> Removing bundled dnlib.dll (Obfuz's custom dnlib is kept) ..."
rm -f "$TARGET/Plugins/dnlib.dll" "$TARGET/Plugins/dnlib.dll.meta"

echo "==> Updating $MANIFEST to the local file reference ..."
sed -i 's#"com\.code-philosophy\.hybridclr":[[:space:]]*"[^"]*"#"com.code-philosophy.hybridclr": "file:com.code-philosophy.hybridclr"#' "$MANIFEST"

VERSION="$(sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TARGET/package.json" | head -1)"
echo
echo "Done: HybridCLR ${VERSION:-unknown} (ref: $REF) installed as a local package at $TARGET"
echo "      dnlib.dll removed, manifest.json now points to the local package."
echo "      Switch back to Unity and wait for recompilation."
