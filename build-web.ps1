# Сборка веб-версии «Расчет сметы».
#
# Собирает один самодостаточный файл bin\Смета.html: внутрь встраиваются
# стили страницы, скрипт и заводской прайс-лист из Цены_услуг.tsv.
# Готовый файл открывается в любом браузере двойным щелчком.
#
# Запуск:  powershell -ExecutionPolicy Bypass -File .\build-web.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$webDir   = Join-Path $root 'web'
$outDir   = Join-Path $root 'bin'
$template = Join-Path $webDir 'index.html'
$appJs    = Join-Path $webDir 'app.js'
$seedTsv  = Join-Path $root ([char]0x0426 + [char]0x0435 + [char]0x043D + [char]0x044B + '_' + [char]0x0443 + [char]0x0441 + [char]0x043B + [char]0x0443 + [char]0x0433 + '.tsv')
$outName  = [string][char]0x0421 + [char]0x043C + [char]0x0435 + [char]0x0442 + [char]0x0430 + '.html'
$outFile  = Join-Path $outDir $outName

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# --- 1. Проверка исходных файлов ---------------------------------------------
Write-Step 'Проверка исходных файлов'
foreach ($file in @($template, $appJs, $seedTsv)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Не найден файл: $file" }
}

# --- 2. Заводской прайс-лист в виде JSON -------------------------------------
Write-Step 'Чтение прайс-листа'
$lines = [System.IO.File]::ReadAllLines($seedTsv, [System.Text.Encoding]::UTF8)
$items = New-Object System.Collections.Generic.List[object]
$number = 0

foreach ($line in $lines) {
    $text = $line.Trim()
    if ($text.Length -eq 0) { continue }
    if ($text.StartsWith('#')) { continue }

    $separator = if ($line.Contains("`t")) { "`t" } else { ';' }
    $parts = $line.Split($separator)
    if ($parts.Length -lt 2) { continue }

    $number++
    $group = $parts[0].Trim()
    $article = ''
    $name = ''
    $unit = ''
    $price = 0.0

    if ($parts.Length -ge 5) {
        $article = $parts[1].Trim()
        $name = $parts[2].Trim()
        $unit = $parts[3].Trim()
        [void][double]::TryParse($parts[4].Trim().Replace(',', '.'), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$price)
    } else {
        $name = $parts[1].Trim()
        if ($parts.Length -gt 2) { $unit = $parts[2].Trim() }
        if ($parts.Length -gt 3) {
            [void][double]::TryParse($parts[3].Trim().Replace(',', '.'), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$price)
        }
    }

    if ($group.Length -eq 0) { $group = 'Прочее' }
    if ($name.Length -eq 0) { continue }
    if ($unit.Length -eq 0) { $unit = 'шт.' }
    if ($article.Length -eq 0) { $article = 'ART-{0:d3}' -f (100 + $number) }

    $items.Add([ordered]@{
        group   = $group
        article = $article
        name    = $name
        unit    = $unit
        price   = [Math]::Round($price, 2)
    })
}

if ($items.Count -eq 0) { throw 'В прайс-листе не найдено ни одной позиции' }
Write-Host ("    позиций: " + $items.Count)

$json = ConvertTo-Json -InputObject $items.ToArray() -Depth 4 -Compress
# </script> внутри данных недопустим — экранируем
$json = $json.Replace('</', '<\/')

# --- 3. Сборка страницы -------------------------------------------------------
Write-Step 'Сборка страницы'
$html = [System.IO.File]::ReadAllText($template, [System.Text.Encoding]::UTF8)
$script = [System.IO.File]::ReadAllText($appJs, [System.Text.Encoding]::UTF8)

$html = $html.Replace('/*__PRICES__*/[]/*__END_PRICES__*/', $json)
$html = $html.Replace('/*__APP__*/', $script)

if ($html.Contains('/*__PRICES__*/') -or $html.Contains('/*__APP__*/')) {
    throw 'Не удалось подставить данные в шаблон страницы'
}

if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
[System.IO.File]::WriteAllText($outFile, $html, (New-Object System.Text.UTF8Encoding($false)))

# --- 4. Итог ------------------------------------------------------------------
Write-Step 'Готово'
$info = Get-Item -LiteralPath $outFile
'{0}  -  {1:N0} КБ' -f $info.Name, ($info.Length / 1KB)
Write-Host "Откройте файл двойным щелчком: $outFile" -ForegroundColor Green
Write-Host ("Заводских позиций в файле: " + $items.Count)
