<#
.SYNOPSIS
    Builds a self-contained release of Akses and packages it into a single Setup.exe with Inno Setup.

.DESCRIPTION
    One command, same shape as WinPlay Mahogany's build-installer.ps1:
      1. fetch-tools.ps1: pinned, SHA-256-checked ffmpeg and pandoc in tools\.
      2. `dotnet publish` the WinUI 3 app, self-contained win-x64 (no .NET runtime needed).
      3. Copy the app's .pri/.xbf (dotnet publish drops them for unpackaged WinUI 3 apps), copy
         tools\ffmpeg and tools\pandoc in, strip WPF/WinForms dead weight.
      4. Run installer\Akses.iss through ISCC.exe.

    Output: installer\output\Akses-Setup-<version>.exe

.PARAMETER InnoCompiler
    Path to ISCC.exe. Defaults to the per-user winget install location.
#>
param(
    [string]$InnoCompiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "src\Akses.App\Akses.App.csproj"

Write-Host "==> Tools (ffmpeg, pandoc)" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "fetch-tools.ps1")

Write-Host "==> dotnet publish (Release, win-x64, self-contained)" -ForegroundColor Cyan
dotnet publish $appProject -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# `dotnet publish` for an unpackaged WinUI 3 app drops the compiled XAML (Akses.pri + *.xbf) that
# `dotnet build` produces next door; without them the app crashes at start. Copy them over.
$buildOutDir = Join-Path $root "src\Akses.App\bin\Release\net9.0-windows10.0.19041.0\win-x64"
$publishDir = Join-Path $buildOutDir "publish"
Write-Host "==> Copying app .pri/.xbf" -ForegroundColor Cyan
$xamlOutputFiles = Get-ChildItem $buildOutDir -File | Where-Object { $_.Name -eq "Akses.pri" -or $_.Extension -eq ".xbf" }
if ($xamlOutputFiles.Count -eq 0) { throw "No .pri/.xbf files found in $buildOutDir - did the build actually run?" }
foreach ($f in $xamlOutputFiles) { Copy-Item $f.FullName -Destination $publishDir -Force }

Write-Host "==> Copying tools\ffmpeg and tools\pandoc" -ForegroundColor Cyan
$toolsOut = Join-Path $publishDir "tools"
if (Test-Path $toolsOut) { Remove-Item $toolsOut -Recurse -Force }
New-Item -ItemType Directory $toolsOut | Out-Null
foreach ($t in @("ffmpeg", "pandoc")) {
    $src = Join-Path $root "tools\$t"
    if (-not (Test-Path $src)) { throw "tools\$t missing - fetch-tools.ps1 should have created it" }
    Copy-Item $src -Destination $toolsOut -Recurse
}

# Self-contained publish for net9.0-windows + UseWinUI drags in the whole Windows Desktop runtime
# (WPF + WinForms, ~64 MB) that a WinUI 3 app never loads. Same list as WinPlay Mahogany.
$deadWeightDlls = @(
    "PresentationCore.dll", "PresentationFramework.dll", "PresentationFramework.Aero.dll",
    "PresentationFramework.Aero2.dll", "PresentationFramework.AeroLite.dll", "PresentationFramework.Classic.dll",
    "PresentationFramework.Fluent.dll", "PresentationFramework.Luna.dll", "PresentationFramework.Royale.dll",
    "PresentationFramework-SystemCore.dll", "PresentationFramework-SystemData.dll",
    "PresentationFramework-SystemDrawing.dll", "PresentationFramework-SystemXml.dll",
    "PresentationFramework-SystemXmlLinq.dll", "PresentationNative_cor3.dll", "PresentationUI.dll",
    "ReachFramework.dll", "System.Printing.dll", "System.Windows.Controls.Ribbon.dll",
    "System.Windows.Extensions.dll", "System.Windows.Forms.dll", "System.Windows.Forms.Design.dll",
    "System.Windows.Forms.Design.Editors.dll", "System.Windows.Forms.Primitives.dll",
    "System.Windows.Input.Manipulations.dll", "System.Windows.Presentation.dll", "System.Windows.dll",
    "System.Xaml.dll", "WindowsFormsIntegration.dll", "Microsoft.VisualBasic.dll",
    "Microsoft.VisualBasic.Core.dll", "Microsoft.VisualBasic.Forms.dll", "PenImc_cor3.dll",
    "wpfgfx_cor3.dll", "System.Design.dll", "System.Drawing.Design.dll"
)
Write-Host "==> Stripping unused WPF/WinForms assemblies" -ForegroundColor Cyan
$removedBytes = 0
foreach ($name in $deadWeightDlls) {
    $path = Join-Path $publishDir $name
    if (Test-Path $path) { $removedBytes += (Get-Item $path).Length; Remove-Item $path -Force }
}
# Magick.NET ships native builds for every platform; only x64 is installed.
Get-ChildItem $publishDir -Recurse -Directory -Filter "runtimes" | ForEach-Object {
    Get-ChildItem $_.FullName -Directory | Where-Object { $_.Name -ne "win-x64" } | ForEach-Object {
        $removedBytes += (Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum
        Remove-Item $_.FullName -Recurse -Force
    }
}
Write-Host ("   removed {0:N1} MB" -f ($removedBytes / 1MB))

if (-not (Test-Path $InnoCompiler)) {
    throw "Inno Setup compiler not found at '$InnoCompiler'. Install it (winget install JRSoftware.InnoSetup) or pass -InnoCompiler <path>."
}

$appVersion = ([xml](Get-Content $appProject -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $appVersion) { throw "No <Version> found in $appProject" }

Write-Host "==> ISCC.exe (Inno Setup), version $appVersion" -ForegroundColor Cyan
& $InnoCompiler "/DMyAppVersion=$appVersion" (Join-Path $PSScriptRoot "Akses.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed with exit code $LASTEXITCODE" }

Write-Host "==> Done. Installer in installer\output\" -ForegroundColor Green
Get-ChildItem (Join-Path $PSScriptRoot "output") -Filter "*.exe" | Select-Object Name, Length, LastWriteTime
