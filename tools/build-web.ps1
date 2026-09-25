# Builds the browser app (src/Mite.Web, Mite.Core compiled to WebAssembly)
# and copies a static, self-contained site into a target folder — e.g. the
# `mite/` folder of the kempffseleme GitHub Pages repo. PowerShell twin of
# tools/build-web.sh (on Windows `bash` is usually WSL, which has neither
# dotnet nor LF line endings).
#
#   .\tools\build-web.ps1 ..\kempffseleme\mite
#
# Requirements: .NET 10 SDK with the wasm-tools workload
#   dotnet workload install wasm-tools
#
# Like the bash script it keeps the .gz companions of the large runtime
# files and drops the uncompressed .wasm / .br ones: js/runtime.js fetches
# the .gz files and inflates them in the browser, so static hosts that do
# not compress WebAssembly still deliver ~2 MB instead of ~6 MB.

param(
    [Parameter(Mandatory = $true)][string]$Target
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\Mite.Web\Mite.Web.csproj'
$out = Join-Path ([IO.Path]::GetTempPath()) ('mite-web-' + [guid]::NewGuid().ToString('N'))

Write-Host '==> Publishing Mite.Web (Release, WebAssembly AOT — a few minutes)...' -ForegroundColor Cyan
dotnet publish $proj -c Release -o $out -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed (is the wasm-tools workload installed?).' }
$src = Join-Path $out 'wwwroot'

Write-Host "==> Writing site to $Target" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $Target | Out-Null
Get-ChildItem -Path $Target -Force | Where-Object { $_.Name -ne '.gitkeep' } | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $src '*') -Destination $Target -Recurse -Force

# drop brotli variants everywhere, and the raw .wasm where a .gz exists
Get-ChildItem -Path $Target -Recurse -File -Filter '*.br' | Remove-Item -Force
$fw = Join-Path $Target '_framework'
if (Test-Path $fw) {
    Get-ChildItem -Path $fw -Recurse -File -Filter '*.wasm' | ForEach-Object {
        if (Test-Path ($_.FullName + '.gz')) { Remove-Item $_.FullName -Force }
    }
}
# small text assets: keep raw only (hosts gzip those themselves)
Get-ChildItem -Path $Target -Recurse -File | Where-Object { $_.Name -match '\.(js|css|html|svg|json)\.gz$' } | Remove-Item -Force

Remove-Item -Recurse -Force $out
$mb = (Get-ChildItem -Path $Target -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("site written to {0} ({1:N1} MB)" -f $Target, $mb) -ForegroundColor Green
