#!/usr/bin/env bash
# Builds the browser app (src/Mite.Web, Mite.Core compiled to WebAssembly)
# and copies a static, self-contained site into a target folder — e.g. the
# `mite/` folder of a GitHub Pages repo.
#
#   tools/build-web.sh ../kempffseleme/mite        # Mac / Linux / Git Bash
#   .\tools\build-web.ps1 ..\kempffseleme\mite     # Windows PowerShell (tools/build-web.ps1)
#
# Requirements: .NET 10 SDK with the wasm-tools workload
#   dotnet workload install wasm-tools
#
# The output keeps the .gz companions of the large runtime files and drops
# the uncompressed .wasm / .br ones: the page's loader (js/runtime.js)
# fetches the .gz files and inflates them in the browser, so static hosts
# that do not compress WebAssembly still deliver ~2 MB instead of ~6 MB.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
target="${1:?target folder}"
out="$(mktemp -d)"

dotnet publish "$root/src/Mite.Web/Mite.Web.csproj" -c Release -o "$out" -v q --nologo
src="$out/wwwroot"

mkdir -p "$target"
find "$target" -mindepth 1 -maxdepth 1 ! -name '.gitkeep' -exec rm -rf {} +
cp -r "$src"/. "$target"/

# drop brotli variants everywhere, and the raw .wasm where a .gz exists
find "$target" -name '*.br' -delete
find "$target/_framework" -name '*.wasm' -print0 | while IFS= read -r -d '' f; do
  [ -f "$f.gz" ] && rm -f "$f"
done
# small text assets: keep raw only (hosts gzip those themselves)
find "$target" \( -name '*.js.gz' -o -name '*.css.gz' -o -name '*.html.gz' -o -name '*.svg.gz' -o -name '*.json.gz' \) -delete
# but the runtime's own js modules are fetched through the loader too: keep them raw (already are)

rm -rf "$out"
echo "site written to $target"
du -sh "$target"
