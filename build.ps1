# =============================================================================
#  Build script for the "Service cost calculator" Windows program.
#
#  Requires nothing but what Windows already has: the C# compiler shipped with
#  the .NET Framework (C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe).
#  No SDK, no NuGet packages, no installs.
#
#  Usage:  powershell -ExecutionPolicy Bypass -File .\build.ps1
#
#  This file is intentionally pure ASCII so that it runs correctly in Windows
#  PowerShell 5.1 with any console code page. Russian file names are assembled
#  from Unicode code points at run time (see $exeName and $seedSrc below).
# =============================================================================

[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir  = Join-Path $root 'src'
$outDir  = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root 'bin' }
$exeName = ([char]0x0420 + [char]0x0430 + [char]0x0441 + [char]0x0447 + [char]0x0435 + [char]0x0442 + '_' + [char]0x0417 + [char]0x0430 + [char]0x044F + [char]0x0432 + [char]0x043A + [char]0x0438 + '.exe')
$seedName = ([char]0x0426 + [char]0x0435 + [char]0x043D + [char]0x044B + '_' + [char]0x0443 + [char]0x0441 + [char]0x043B + [char]0x0443 + [char]0x0433 + '.tsv')
$exePath  = Join-Path $outDir $exeName
$seedSrc  = Join-Path $root $seedName
$iconPath = Join-Path $root 'app.ico'

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# --- 1. Locate the C# compiler ----------------------------------------------
Write-Step 'Looking for csc.exe'
$candidates = @()
if (${env:WINDIR}) {
    $candidates += Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $candidates += Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw 'csc.exe not found. Please install the .NET Framework 4.x.'
}
Write-Host "    $csc"

# --- 2. Check the sources ----------------------------------------------------
Write-Step 'Checking source files'
$manifest = Join-Path $srcDir 'sources.txt'
if (-not (Test-Path -LiteralPath $manifest)) { throw "Source list not found: $manifest" }

$sources = @()
foreach ($name in [System.IO.File]::ReadAllLines($manifest, [System.Text.Encoding]::UTF8)) {
    $clean = $name.Trim()
    if ($clean.Length -eq 0 -or $clean.StartsWith('#')) { continue }
    $sources += (Join-Path $srcDir $clean)
}
if ($sources.Count -eq 0) { throw "Source list is empty: $manifest" }
foreach ($file in $sources) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Source file not found: $file" }
}

# --- 3. Price list (embedded into the exe as a resource) ---------------------
if (-not (Test-Path -LiteralPath $seedSrc)) {
    throw "Price list file not found: $seedSrc"
}
Write-Step 'Preparing the price list resource'
$seedText = [System.IO.File]::ReadAllText($seedSrc, [System.Text.Encoding]::UTF8)
if ($seedText.StartsWith([string][char]0xFEFF)) { $seedText = $seedText.Substring(1) }
$seedTemp = Join-Path ([System.IO.Path]::GetTempPath()) 'service_prices_utf8bom.tsv'
[System.IO.File]::WriteAllText($seedTemp, $seedText, (New-Object System.Text.UTF8Encoding($true)))

# --- 4. Compile --------------------------------------------------------------
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

Write-Step 'Compiling'
$arguments = New-Object System.Collections.Generic.List[string]
$arguments.Add('/nologo')
$arguments.Add('/target:winexe')
$arguments.Add('/platform:anycpu')
$arguments.Add('/optimize+')
$arguments.Add('/utf8output')
$arguments.Add('/warn:4')
$arguments.Add('/reference:System.dll')
$arguments.Add('/reference:System.Drawing.dll')
$arguments.Add('/reference:System.Windows.Forms.dll')
$arguments.Add('/reference:System.IO.Compression.dll')
$arguments.Add('/reference:System.IO.Compression.FileSystem.dll')
$arguments.Add("/out:$exePath")
$arguments.Add("/resource:$seedTemp,KotovCalc.Seed.tsv")
if (Test-Path -LiteralPath $iconPath) {
    $arguments.Add("/win32icon:$iconPath")
    $arguments.Add("/resource:$iconPath,KotovCalc.App.ico")
    Write-Host "    icon: $iconPath"
}
$arguments.AddRange([string[]]$sources)

& $csc $arguments.ToArray()
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

# --- 5. Result ----------------------------------------------------------------
# The price list is embedded into the executable, so nothing else has to be
# copied next to the program: the .exe is self-contained.

Write-Step 'Done'
$info = Get-Item -LiteralPath $exePath
'{0}  -  {1:N0} KB' -f $info.Name, ($info.Length / 1KB)
Write-Host "Folder: $outDir" -ForegroundColor Green
