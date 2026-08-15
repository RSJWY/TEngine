#!/usr/bin/env bash
# =============================================================================
# sync-obfuz-local.sh
#
# Installs/upgrades Obfuz as a local (embedded) package at
# Packages/com.code-philosophy.obfuz.
# NOTE: unlike HybridCLR, Obfuz's bundled custom dnlib.dll MUST be kept
# (the whole project shares this single copy; the HybridCLR copy is removed
#  by sync-hybridclr-local.sh).
#
# Usage:
#   ./sync-obfuz-local.sh               install/upgrade to the latest stable tag
#   ./sync-obfuz-local.sh v3.1.0        install/upgrade to a specific tag or branch
#   ./sync-obfuz-local.sh <full-commit> install/upgrade to a specific commit
#
# Pulls from GitHub by default; set SYNC_OBFUZ_REPO to switch mirror:
#   SYNC_OBFUZ_REPO=https://gitee.com/focus-creative-games/obfuz.git ./sync-obfuz-local.sh
#
# Idempotent: every run is a clean re-sync.
# =============================================================================
set -euo pipefail

# This script lives in Packages/; switch to the project root first.
cd "$(dirname "$0")/.."

REPO="${SYNC_OBFUZ_REPO:-https://github.com/focus-creative-games/obfuz.git}"
TARGET="Packages/com.code-philosophy.obfuz"
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

echo "==> Fetching obfuz (ref: $REF) ..."
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
sed -i 's#"com\.code-philosophy\.obfuz":[[:space:]]*"[^"]*"#"com.code-philosophy.obfuz": "file:com.code-philosophy.obfuz"#' "$MANIFEST"

VERSION="$(sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TARGET/package.json" | head -1)"
echo
echo "Done: Obfuz ${VERSION:-unknown} (ref: $REF) installed as a local package at $TARGET"
echo "      Custom dnlib.dll kept, manifest.json now points to the local package."
echo "      Switch back to Unity and wait for recompilation."
