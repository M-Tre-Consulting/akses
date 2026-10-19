<#
.SYNOPSIS
    Downloads pandoc into tools\pandoc, for developer builds and tests.

.DESCRIPTION
    The installed app doesn't ship pandoc (225 MB): it downloads this same pinned release on the
    first document conversion, after asking (src/Akses.Core/ToolDownloads.cs has the same URL and
    SHA-256: keep them in sync). This script puts it in tools\pandoc so developer builds and the
    tests have it right away. tools\ is never committed (.gitignore).

    ffmpeg is not downloaded here: it's the slim build made by installer/build-ffmpeg.sh (run it in
    WSL), which is what the installer ships.
#>
param([switch]$Force)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is 10x slower with the progress bar

$PandocUrl    = "https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-windows-x86_64.zip"
$PandocSha256 = "6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587"

$root = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root "tools"
$downloads = Join-Path $tools "_dl"
New-Item -ItemType Directory -Force $downloads | Out-Null

function Get-Verified([string]$url, [string]$sha, [string]$file) {
    $path = Join-Path $downloads $file
    if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $sha) {
        Write-Host "==> Downloading $url" -ForegroundColor Cyan
        Invoke-WebRequest $url -OutFile $path
    }
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($actual -ne $sha) {
        Remove-Item $path -Force
        throw "SHA-256 mismatch for $file`n  expected $sha`n  got      $actual"
    }
    return $path
}

$pandocDir = Join-Path $tools "pandoc"
if ($Force -or -not (Test-Path (Join-Path $pandocDir "pandoc.exe"))) {
    $zip = Get-Verified $PandocUrl $PandocSha256 "pandoc.zip"
    $tmp = Join-Path $downloads "pandoc-x"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip $tmp
    $build = Get-ChildItem $tmp -Directory | Select-Object -First 1
    if (Test-Path $pandocDir) { Remove-Item $pandocDir -Recurse -Force }
    New-Item -ItemType Directory $pandocDir | Out-Null
    Copy-Item (Join-Path $build.FullName "pandoc.exe") $pandocDir
    Get-ChildItem $build.FullName -File | Where-Object { $_.Name -like "COPYING*" -or $_.Name -like "COPYRIGHT*" } |
        Copy-Item -Destination $pandocDir
    Remove-Item $tmp -Recurse -Force
    Write-Host "   pandoc ready in tools\pandoc" -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $tools "ffmpeg\BUILD.txt"))) {
    Write-Host "   tools\ffmpeg missing or not the slim build: run  wsl -d Arch -u root -- bash installer/build-ffmpeg.sh" -ForegroundColor Yellow
}
Write-Host "==> Tools ready" -ForegroundColor Green
