<#
.SYNOPSIS
    Builds the Linux version of Akses and packages it into a .deb (Linux Mint, Ubuntu, Debian).

.DESCRIPTION
    Same idea as build-installer.ps1, for src/Akses.Linux:
      1. `dotnet publish` the Avalonia app, self-contained linux-x64 and trimmed (no .NET runtime
         needed on the target). Cross-publishing from Windows works: nothing native is compiled.
      2. installer/linux/build-deb.sh in WSL (needs dpkg: `pacman -S dpkg` on Arch) packs it with
         the .desktop file, the icons and the dependencies (ffmpeg, poppler-utils, libvlc).

    Output: installer\output\akses_<version>_amd64.deb (the version is the Windows csproj's).

.PARAMETER Distro
    The WSL distribution that runs dpkg-deb. Defaults to Arch, like build-ffmpeg.sh.
#>
param(
    [string]$Distro = "Arch"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$linuxProject = Join-Path $root "src\Akses.Linux\Akses.Linux.csproj"
$windowsProject = Join-Path $root "src\Akses.App\Akses.App.csproj"
$publishDir = Join-Path $root "src\Akses.Linux\bin\Release\net9.0\linux-x64\publish"

$appVersion = ([xml](Get-Content $windowsProject -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $appVersion) { throw "No <Version> found in $windowsProject" }

Write-Host "==> dotnet publish (Release, linux-x64, self-contained), version $appVersion" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $linuxProject -c Release -r linux-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

function ConvertTo-WslPath([string]$path) {
    $full = (Resolve-Path $path).Path
    "/mnt/" + $full.Substring(0, 1).ToLower() + ($full.Substring(2) -replace "\\", "/")
}

$outDir = Join-Path $PSScriptRoot "output"
New-Item -ItemType Directory $outDir -Force | Out-Null
Write-Host "==> build-deb.sh in WSL ($Distro)" -ForegroundColor Cyan
wsl -d $Distro -e bash (ConvertTo-WslPath (Join-Path $PSScriptRoot "linux\build-deb.sh")) (ConvertTo-WslPath $publishDir) $appVersion (ConvertTo-WslPath $outDir)
if ($LASTEXITCODE -ne 0) { throw "build-deb.sh failed with exit code $LASTEXITCODE" }

Write-Host "==> Done. Package in installer\output\" -ForegroundColor Green
Get-ChildItem $outDir -Filter "*.deb" | Select-Object Name, Length, LastWriteTime
