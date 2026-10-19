<#
.SYNOPSIS
    Downloads the command-line tools Akses drives (ffmpeg/ffprobe and pandoc) into tools\.

.DESCRIPTION
    Pinned builds, checked against their SHA-256 before anything is extracted:
      tools\ffmpeg\  ffmpeg.exe, ffprobe.exe + their DLLs (BtbN GPL shared build, ffmpeg 9.0)
      tools\pandoc\  pandoc.exe (official release)
    The app finds them there in a developer build; build-installer.ps1 copies tools\ into the
    publish folder so the installer ships them. tools\ is never committed (.gitignore).

    BtbN deletes its dated autobuilds after a while: when the pinned URL disappears, pick a new
    "ffmpeg-n9.x-...-win64-gpl-shared-9.x.zip" from https://github.com/BtbN/FFmpeg-Builds/releases,
    update $FfmpegUrl and $FfmpegSha256 below, and note the version in NOTICE.md.
#>
param([switch]$Force)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is 10x slower with the progress bar

$FfmpegUrl    = "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-19-13-06/ffmpeg-n9.0.2-28-g669f5ab083-win64-gpl-shared-9.0.zip"
$FfmpegSha256 = "7b5281d9d93e45645c0aaf3a0372fc68b52bb45f9c8d52cc9574cf8a6a2f45a8"
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

# ---- ffmpeg ------------------------------------------------------------------------------
$ffmpegDir = Join-Path $tools "ffmpeg"
if ($Force -or -not (Test-Path (Join-Path $ffmpegDir "ffmpeg.exe"))) {
    $zip = Get-Verified $FfmpegUrl $FfmpegSha256 "ffmpeg.zip"
    $tmp = Join-Path $downloads "ffmpeg-x"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip $tmp
    $build = Get-ChildItem $tmp -Directory | Select-Object -First 1
    if (Test-Path $ffmpegDir) { Remove-Item $ffmpegDir -Recurse -Force }
    New-Item -ItemType Directory $ffmpegDir | Out-Null
    # ffplay isn't used: leave it out (and its SDL dependency is inside it anyway).
    Get-ChildItem (Join-Path $build.FullName "bin") -File | Where-Object { $_.Name -ne "ffplay.exe" } |
        Copy-Item -Destination $ffmpegDir
    Copy-Item (Join-Path $build.FullName "LICENSE.txt") (Join-Path $ffmpegDir "LICENSE.txt")
    Remove-Item $tmp -Recurse -Force
    Write-Host "   ffmpeg ready in tools\ffmpeg" -ForegroundColor Green
}

# ---- pandoc ------------------------------------------------------------------------------
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

Write-Host "==> Tools ready" -ForegroundColor Green
