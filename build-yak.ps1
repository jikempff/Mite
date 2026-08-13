# Build and package Mite as a Yak package for Food4Rhino.
#
# Prerequisites:
#   - .NET SDK (net48 targeting pack)
#   - Yak CLI: https://developer.rhino3d.com/guides/yak/creating-a-rhino-plugin-package/
#     Install: dotnet tool install --global Yak
#
# Usage:
#   .\build-yak.ps1              # build Release then package
#   .\build-yak.ps1 -SkipBuild   # package from existing build output

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ghProj = Join-Path $root 'src\Mite.Grasshopper\Mite.Grasshopper.csproj'
$distDir = Join-Path $root 'dist'

# --- Build ---
if (-not $SkipBuild) {
    Write-Host '==> Building Mite.Grasshopper (Release)...' -ForegroundColor Cyan
    dotnet build $ghProj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

# --- Stage files into dist ---
$buildOut = Join-Path $root 'src\Mite.Grasshopper\bin\Release\net48'
if (-not (Test-Path $buildOut)) {
    throw "Build output not found at $buildOut - run without -SkipBuild first."
}

Write-Host '==> Staging package files...' -ForegroundColor Cyan

# Copy GHA (rename DLL to .gha)
$ghaDll = Join-Path $buildOut 'Mite.Grasshopper.dll'
Copy-Item $ghaDll (Join-Path $distDir 'Mite.Grasshopper.gha') -Force

# Copy Core library
Copy-Item (Join-Path $buildOut 'Mite.Core.dll') $distDir -Force

# Copy MathNet.Numerics if present alongside the build
$mathnet = Join-Path $buildOut 'MathNet.Numerics.dll'
if (Test-Path $mathnet) {
    Copy-Item $mathnet $distDir -Force
}

# Copy Microsoft.Bcl.HashCode (net48 dependency of Mite.Core for HashCode.Combine;
# without it the plugin fails to load on machines lacking the DLL)
$bclHash = Join-Path $buildOut 'Microsoft.Bcl.HashCode.dll'
if (Test-Path $bclHash) {
    Copy-Item $bclHash $distDir -Force
}

# Copy icon if converted to PNG (see README)
$iconPng = Join-Path $root 'icon.png'
if (Test-Path $iconPng) {
    Copy-Item $iconPng $distDir -Force
}

# --- Build Yak package ---
Write-Host '==> Running yak build...' -ForegroundColor Cyan
Push-Location $distDir
try {
    yak build
    if ($LASTEXITCODE -ne 0) { throw 'yak build failed.' }
    $yakFile = Get-ChildItem -Filter '*.yak' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host "==> Package created: $($yakFile.FullName)" -ForegroundColor Green
    Write-Host ''
    Write-Host 'To publish:' -ForegroundColor Yellow
    $yakPath = $yakFile.FullName
    Write-Host ('  yak push "' + $yakPath + '"')
} finally {
    Pop-Location
}
