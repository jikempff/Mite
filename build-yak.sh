#!/usr/bin/env bash
# Build and package Mite as a Yak package for Food4Rhino — the Mac twin of
# build-yak.ps1 (same steps, same dist/ layout, same package name).
#
# Prerequisites:
#   - .NET SDK (builds the net48 plugin through the reference assemblies)
#   - Rhino 8 for Mac, whose Yak CLI is used:
#       "/Applications/Rhino 8.app/Contents/Resources/bin/yak"
#     (a `yak` on PATH, or YAK=/path/to/yak, takes precedence)
#
# Usage:
#   bash build-yak.sh               # build Release, then package
#   bash build-yak.sh --skip-build  # package from the existing build output
#
# Then publish with the command it prints:  "<yak>" push dist/mite-<version>-rh8_0-any.yak
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
gh_proj="$root/src/Mite.Grasshopper/Mite.Grasshopper.csproj"
dist="$root/dist"
build_out="$root/src/Mite.Grasshopper/bin/Release/net48"

skip_build=0
for a in "$@"; do
  case "$a" in
    --skip-build|-SkipBuild) skip_build=1 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "unknown option: $a" >&2; exit 2 ;;
  esac
done

# --- Build ---
if [ "$skip_build" -eq 0 ]; then
  echo "==> Building Mite.Grasshopper (Release)..."
  dotnet build "$gh_proj" -c Release
fi

# --- Stage files into dist ---
if [ ! -d "$build_out" ]; then
  echo "Build output not found at $build_out - run without --skip-build first." >&2
  exit 1
fi
echo "==> Staging package files..."
mkdir -p "$dist"
# Start from an empty dist (manifest.yml kept): yak packs every file in the
# folder, so anything left there — an old zip, a stray DLL — would ship
find "$dist" -mindepth 1 -maxdepth 1 ! -name manifest.yml -exec rm -rf {} +

# GHA = the plugin DLL renamed
cp -f "$build_out/Mite.Grasshopper.dll" "$dist/Mite.Grasshopper.gha"
# Core library
cp -f "$build_out/Mite.Core.dll" "$dist/"
# MathNet.Numerics.dll of older packages (1.2.0 has its own sparse solver)
rm -f "$dist/MathNet.Numerics.dll"
# Microsoft.Bcl.HashCode: net48 dependency of Mite.Core (HashCode.Combine);
# without it the plugin fails to load where the DLL is missing
if [ -f "$build_out/Microsoft.Bcl.HashCode.dll" ]; then
  cp -f "$build_out/Microsoft.Bcl.HashCode.dll" "$dist/"
fi
# Package icon
if [ -f "$root/icon.png" ]; then
  cp -f "$root/icon.png" "$dist/"
fi

# --- Build Yak package ---
yak="${YAK:-}"
if [ -z "$yak" ]; then
  if command -v yak >/dev/null 2>&1; then
    yak="$(command -v yak)"
  elif [ -x "/Applications/Rhino 8.app/Contents/Resources/bin/yak" ]; then
    yak="/Applications/Rhino 8.app/Contents/Resources/bin/yak"
  else
    echo "Yak CLI not found (install Rhino 8, put yak on PATH or set YAK=...)." >&2
    exit 1
  fi
fi

# Remove packages of earlier versions so the newest file is the one just built
rm -f "$dist"/*.yak

echo "==> Running yak build..."
(cd "$dist" && "$yak" build)

# old packages were removed above, so the one .yak in dist is the new one
pkg=""
for f in "$dist"/*.yak; do
  if [ -f "$f" ]; then pkg="$f"; fi
done
if [ -z "$pkg" ]; then
  echo "yak build produced no .yak package." >&2
  exit 1
fi
echo "==> Package created: $pkg"
echo
echo "To publish:"
echo "  \"$yak\" push \"$pkg\""
