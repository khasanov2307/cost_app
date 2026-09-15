# =============================================================================
#  Builds and runs the automated checks for the service calculator.
#  Pure ASCII on purpose (Windows PowerShell 5.1, any console code page).
# =============================================================================

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$test = Join-Path $root 'tests'
$bin  = Join-Path $root 'bin'
$out  = Join-Path $test 'Harness.exe'
$out2 = Join-Path $test 'Harness2.exe'
$out3 = Join-Path $test 'DocxCheck.exe'
$out4 = Join-Path $test 'Harness3.exe'
$out5 = Join-Path $test 'Harness4.exe'
$out6 = Join-Path $test 'Harness6.exe'
$webName = -join @(0x0421,0x043C,0x0435,0x0442,0x0430 | ForEach-Object { [char]$_ }) + '.html'

$csc = @(
    (Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'csc.exe not found (.NET Framework 4.x is required).' }

$exeName  = ([char]0x0420 + [char]0x0430 + [char]0x0441 + [char]0x0447 + [char]0x0435 + [char]0x0442 + '_' + [char]0x0441 + [char]0x043C + [char]0x0435 + [char]0x0442 + [char]0x044B + '.exe')
$seedName = ([char]0x0426 + [char]0x0435 + [char]0x043D + [char]0x044B + '_' + [char]0x0443 + [char]0x0441 + [char]0x043B + [char]0x0443 + [char]0x0433 + '.tsv')

# the factory price list is embedded into the binaries as a resource
$seedSrc = Join-Path $root $seedName
$seedText = [System.IO.File]::ReadAllText($seedSrc, [System.Text.Encoding]::UTF8)
if ($seedText.StartsWith([string][char]0xFEFF)) { $seedText = $seedText.Substring(1) }
$seedTemp = Join-Path ([System.IO.Path]::GetTempPath()) 'kotov_seed_tests.tsv'
[System.IO.File]::WriteAllText($seedTemp, $seedText, (New-Object System.Text.UTF8Encoding($true)))

# исходные файлы берём из общего списка src\sources.txt
$manifest = Join-Path $src 'sources.txt'
if (-not (Test-Path -LiteralPath $manifest)) { throw "Source list not found: $manifest" }

$sourceList = @()
foreach ($name in [System.IO.File]::ReadAllLines($manifest, [System.Text.Encoding]::UTF8)) {
    $clean = $name.Trim()
    if ($clean.Length -eq 0 -or $clean.StartsWith('#')) { continue }
    $sourceList += (Join-Path $src $clean)
}
if ($sourceList.Count -eq 0) { throw "Source list is empty: $manifest" }

$commonRefs = @('/reference:System.dll', '/reference:System.Drawing.dll',
                '/reference:System.Windows.Forms.dll', '/reference:System.IO.Compression.dll')
$commonArgs = @('/nologo', '/target:exe', '/platform:anycpu', '/utf8output',
                "/resource:$seedTemp,KotovCalc.Seed.tsv")

Write-Host '==> Compiling the calculator checks' -ForegroundColor Cyan
& $csc $commonArgs $commonRefs /main:Harness "/out:$out" $sourceList `
    (Join-Path $test 'Harness.cs') (Join-Path $test 'Harness2.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Write-Host '==> Compiling the price editor checks' -ForegroundColor Cyan
& $csc $commonArgs $commonRefs /main:Harness2 "/out:$out2" $sourceList `
    (Join-Path $test 'Harness.cs') (Join-Path $test 'Harness2.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Write-Host '==> Compiling the Word checks' -ForegroundColor Cyan
& $csc $commonArgs $commonRefs /main:DocxCheck "/out:$out3" $sourceList `
    (Join-Path $test 'DocxCheck.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
Write-Host '==> Compiling the store checks' -ForegroundColor Cyan
& $csc $commonArgs $commonRefs /main:Harness6 "/out:$out6" $sourceList `
    (Join-Path $test 'Harness6.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Write-Host '==> Compiling the logo checks' -ForegroundColor Cyan
& $csc $commonArgs $commonRefs /main:Harness4 "/out:$out5" $sourceList `
    (Join-Path $test 'Harness4.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Write-Host '==> Compiling the feature checks' -ForegroundColor Cyan
& $csc $commonArgs $commonRefs /main:Harness3 "/out:$out4" $sourceList `
    (Join-Path $test 'Harness3.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

# the price list must sit next to the harness executable (it is embedded as a resource)
Copy-Item -LiteralPath (Join-Path $root $seedName) -Destination (Join-Path $test $seedName) -Force

# test runs use their own price store, so the user profile stays untouched
$storeDir = Join-Path ([System.IO.Path]::GetTempPath()) 'kotov-store-test'
if (Test-Path -LiteralPath $storeDir) { Remove-Item -LiteralPath $storeDir -Recurse -Force }
New-Item -ItemType Directory -Path $storeDir | Out-Null
$storeFile = Join-Path $storeDir 'prices.xml'
Write-Host "    store: $storeFile"

Write-Host '==> Running the calculator checks' -ForegroundColor Cyan
& $out $storeFile
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Checks failed with exit code $code" }

Write-Host ''
Write-Host '==> Running the price editor checks' -ForegroundColor Cyan
& $out2 $storeFile
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Checks failed with exit code $code" }

Write-Host '==> Running the feature checks' -ForegroundColor Cyan
& $out4 $storeFile
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Checks failed with exit code $code" }

Write-Host '==> Running the logo checks' -ForegroundColor Cyan
& $out5
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Checks failed with exit code $code" }

Write-Host '==> Running the store checks' -ForegroundColor Cyan
# проверки базы выполняются, если PostgreSQL отвечает на localhost
$databaseReady = $false
try {
    $probe = New-Object System.Net.Sockets.TcpClient
    $probe.Connect("127.0.0.1", 5432)
    $probe.Close()
    $databaseReady = $true
} catch { $databaseReady = $false }

if ($databaseReady) {
    & $out6 127.0.0.1 smeta smeta smeta
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "Store checks failed with exit code $code" }
}
else {
    Write-Host '    PostgreSQL is not available - database store checks skipped'
}

Write-Host ''
Write-Host '==> Running the Word checks' -ForegroundColor Cyan
# 1) образец документа, собранный проверками
$docxOut = Join-Path $test 'docx-check.docx'
& $out3 $docxOut
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Checks failed with exit code $code" }

# 2) сквозная проверка: сама программа формирует смету, проверки разбирают её документ
$exeOut = Join-Path $bin $exeName
if (Test-Path -LiteralPath $exeOut) {
    Write-Host '==> The program builds an estimate, the checks read it' -ForegroundColor Cyan
    $realDocx = Join-Path $test 'program-estimate.docx'
    Remove-Item -LiteralPath $realDocx -Force -ErrorAction SilentlyContinue

    # файл может оказаться занят антивирусом — пробуем несколько раз
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        & $exeOut --estimate $realDocx
        if (Test-Path -LiteralPath $realDocx) { break }
        Start-Sleep -Milliseconds 700
    }
    if (-not (Test-Path -LiteralPath $realDocx)) { throw "The program did not build the estimate file" }
    Write-Host ('    estimate file: ' + (Get-Item -LiteralPath $realDocx).Length + ' bytes')

    & $out3 --check $realDocx
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "Checks failed with exit code $code" }
}

Write-Host ''

# --- веб-версия: собираем страницу и проверяем её в Яндекс Браузере ---
Write-Host ''
Write-Host '==> Building the web version' -ForegroundColor Cyan
& (Join-Path $root 'build-web.ps1')

$webPage = Join-Path $bin $webName
$webChecks = Join-Path $test 'web_check.ps1'
$docxChecks = Join-Path $test 'web_docx_check.ps1'
$yandex = 'C:\Program Files\Yandex\YandexBrowser\Application\browser.exe'

if ((Test-Path -LiteralPath $yandex) -and (Test-Path -LiteralPath $webChecks)) {
    Write-Host ''
    Write-Host '==> Running the web checks in Yandex Browser' -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File $webChecks -Page $webPage -OutDir $test
    if ($LASTEXITCODE -ne 0) { throw "Web checks failed with exit code $LASTEXITCODE" }

    if (Test-Path -LiteralPath $docxChecks) {
        Write-Host ''
        Write-Host '==> Checking the Word file saved from the browser' -ForegroundColor Cyan
        & powershell -NoProfile -ExecutionPolicy Bypass -File $docxChecks -Page $webPage -OutDir $test
        if ($LASTEXITCODE -ne 0) { throw "Web docx checks failed with exit code $LASTEXITCODE" }

    $logoChecks = Join-Path $test 'web_logo_check.ps1'
    if (Test-Path -LiteralPath $logoChecks) {
        Write-Host ''
        Write-Host '==> Checking the company logo in the browser' -ForegroundColor Cyan
        & powershell -NoProfile -ExecutionPolicy Bypass -File $logoChecks
        if ($LASTEXITCODE -ne 0) { throw "Web logo checks failed with exit code $LASTEXITCODE" }
    }
    }
} else {
    Write-Host '    Yandex Browser not found - web checks skipped'
}
Write-Host 'All checks passed.' -ForegroundColor Green
