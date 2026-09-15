# Проверка логотипа компании в веб-версии: печатная форма и документ Word.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$page = Join-Path $root 'bin\Смета.html'
$outDir = Join-Path $root 'tests'
# маленькая картинка-логотип создаётся прямо здесь, без внешних файлов
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(240, 80)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::FromArgb(47, 111, 208))
$graphics.FillRectangle([System.Drawing.Brushes]::White, 12, 20, 70, 40)
$graphics.Dispose()
$stream = New-Object System.IO.MemoryStream
$bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$logoBase64 = [Convert]::ToBase64String($stream.ToArray())
$stream.Dispose()

$yandex = 'C:\Program Files\Yandex\YandexBrowser\Application\browser.exe'
$port = 9471
$profile = Join-Path ([System.IO.Path]::GetTempPath()) ('yandex-logo-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$url = 'file:///' + ($page -replace '\\', '/')

$process = Start-Process -FilePath $yandex -PassThru -ArgumentList @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--allow-file-access-from-files', "--remote-debugging-port=$port", "--user-data-dir=$profile", 'about:blank')

$failed = 0
$msgId = 0

function CheckTrue($what, $value, $details) {
    if (-not $value) { $script:failed++ }
    if ($value) { Write-Host ("  PASS  " + $what) }
    else { Write-Host ("  FAIL  " + $what + "   " + $details) }
}

function Check($what, $expected, $actual) {
    $ok = ($expected -eq $actual)
    if (-not $ok) { $script:failed++ }
    if ($ok) { Write-Host ("  PASS  " + $what) }
    else { Write-Host ("  FAIL  " + $what + "   ожидалось=<" + $expected + "> получено=<" + $actual + ">") }
}

try {
    $targets = $null
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 400
        try { $targets = Invoke-RestMethod "http://127.0.0.1:$port/json/list" -TimeoutSec 3; if ($targets) { break } } catch { }
    }
    if (-not $targets) { throw 'порт отладки не открылся' }
    $target = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1

    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $socket.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()

    function Send-Cdp([string]$method, $params) {
        $script:msgId++
        $payload = @{ id = $script:msgId; method = $method }
        if ($params) { $payload['params'] = $params }
        $bytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 8 -Compress))
        $socket.SendAsync((New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)),
            [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
        return $script:msgId
    }
    function Receive-Cdp {
        $buffer = New-Object byte[] 8388608
        $builder = New-Object System.Text.StringBuilder
        do {
            $r = $socket.ReceiveAsync((New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)),
                [Threading.CancellationToken]::None).Result
            [void]$builder.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $r.Count))
        } while (-not $r.EndOfMessage)
        return $builder.ToString()
    }
    function Wait-Cdp([int]$id) {
        for ($i = 0; $i -lt 600; $i++) {
            $m = (Receive-Cdp) | ConvertFrom-Json
            if ($m.id -eq $id) { return $m }
        }
        return $null
    }
    function Eval([string]$expression) {
        $id = Send-Cdp 'Runtime.evaluate' @{ expression = $expression; returnByValue = $true; awaitPromise = $true }
        $reply = Wait-Cdp $id
        if ($reply.result.exceptionDetails) {
            $d = $reply.result.exceptionDetails
            $text = if ($d.exception.description) { $d.exception.description } else { $d.text }
            Write-Host ("        ошибка скрипта: " + $text)
            return $null
        }
        return $reply.result.result.value
    }

    Send-Cdp 'Page.enable' | Out-Null
    Send-Cdp 'Runtime.enable' | Out-Null
    [void](Wait-Cdp (Send-Cdp 'Page.navigate' @{ url = $url }))
    Start-Sleep -Milliseconds 2000
    [void](Eval 'try { localStorage.removeItem(WebEstimate.storageKey); } catch (e) {}')
    [void](Wait-Cdp (Send-Cdp 'Page.navigate' @{ url = $url }))
    Start-Sleep -Milliseconds 2200
    Get-ChildItem -LiteralPath $outDir -Filter '*.docx' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    [void](Send-Cdp 'Page.setDownloadBehavior' @{ behavior = 'allow'; downloadPath = $outDir })

    Write-Host '[1] логотип не задан'
    Check 'логотипа нет' 'object' (Eval 'typeof WebEstimate.state.logo')
    CheckTrue 'значение пустое' ((Eval 'WebEstimate.state.logo === null') -eq $true) 'не null'
    CheckTrue 'кнопка «Убрать» скрыта' ((Eval 'document.getElementById("logoRemove").style.display') -eq 'none') 'видна'

    Write-Host ''
    Write-Host '[2] установка логотипа'
    $set = Eval ("WebEstimate.state.logo = { data: 'data:image/png;base64," + $logoBase64 + "', name: 'logo-sample.png' }; WebEstimate.save(); WebEstimate.refreshLogoControls(); 'ок'")
    CheckTrue 'логотип присвоен' ((Eval 'WebEstimate.state.logo && WebEstimate.state.logo.data.length > 100') -eq $true) 'нет данных'
    CheckTrue 'кнопка «Убрать» показана' ((Eval 'document.getElementById("logoRemove").style.display') -ne 'none') 'скрыта'

    Write-Host ''
    Write-Host '[3] размеры логотипа читаются из файла'
    Check 'ширина картинки' 240 (Eval 'WebEstimate.imageSize(WebEstimate.state.logo.data).width')
    Check 'высота картинки' 80 (Eval 'WebEstimate.imageSize(WebEstimate.state.logo.data).height')
    Check 'ширина в документе ограничена' 180 (Eval 'WebEstimate.logoBox().width')
    Check 'высота в документе ограничена' 60 (Eval 'WebEstimate.logoBox().height')

    Write-Host ''
    Write-Host '[4] логотип в печатной форме сметы'
    [void](Eval 'WebEstimate.togglePick(WebEstimate.keyOf(WebEstimate.state.items[0]), true)')
    [void](Eval 'WebEstimate.showPreview()')
    Check 'картинка в смете есть' 1 (Eval 'document.querySelectorAll("#sheet img.logo").length')
    CheckTrue 'источник картинки — данные логотипа' ((Eval 'document.querySelector("#sheet img.logo").src').StartsWith('data:image/png')) 'не data:'
    CheckTrue 'заголовок заявки на месте' ((Eval 'document.querySelector("#sheet h1").textContent').StartsWith([string][char]0x0417 + [char]0x0410 + [char]0x042F + [char]0x0412 + [char]0x041A + [char]0x0410)) 'нет заголовка'
    CheckTrue 'подпись к картинке задана' ((Eval 'document.querySelector("#sheet img.logo").alt').Length -gt 5) 'нет alt'

    Write-Host ''
    Write-Host '[5] логотип попадает в документ Word'
    Check 'данные для Word собраны' 'png' (Eval 'WebEstimate.logoForDocx().extension')
    Check 'размер в Word' 180 (Eval 'WebEstimate.logoForDocx().width')
    CheckTrue 'разметка рисунка собрана' ((Eval 'WebEstimate.buildDocx()[2].text').Contains('wordprocessingDrawing')) 'нет разметки'
    CheckTrue 'связь рисунка объявлена' ((Eval 'WebEstimate.buildDocx()[4].text').Contains('rId2')) 'нет связи'
    CheckTrue 'файл картинки в архиве' ((Eval 'WebEstimate.buildDocx().length') -eq 6) 'частей не 6'
    Check 'имя файла картинки' 'word/media/logo.png' (Eval 'WebEstimate.buildDocx()[5].name')
    CheckTrue 'размер архива вырос' ((Eval 'WebEstimate.zipStore(WebEstimate.buildDocx()).size') -gt 3000) 'архив мал'

    [void](Eval 'WebEstimate.exportDocx()')
    Write-Host ''
    Write-Host '[6] ждём файл из браузера'
    $file = $null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $candidate = Get-ChildItem -LiteralPath $outDir -File -Filter '*.docx' -ErrorAction SilentlyContinue |
            Where-Object { $_.Length -gt 1000 } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($candidate) { $file = $candidate; break }
    }
    if ($file) {
        Write-Host ("        файл: " + $file.Name + " (" + $file.Length + " байт)")
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            $parts = @($zip.Entries | ForEach-Object { $_.FullName })
            CheckTrue 'картинка внутри архива' ($parts -contains 'word/media/logo.png') ('части: ' + ($parts -join ', '))
            $media = $zip.GetEntry('word/media/logo.png')
            CheckTrue 'картинка не пустая' ($media.Length -gt 500) ("размер: " + $media.Length)
            $rels = $zip.GetEntry('word/_rels/document.xml.rels')
            $reader = New-Object System.IO.StreamReader($rels.Open(), [System.Text.Encoding]::UTF8)
            $relsText = $reader.ReadToEnd(); $reader.Close()
            CheckTrue 'связь rId2 в файле' ($relsText.Contains('rId2')) 'нет связи'
            CheckTrue 'тип рисунка в отношениях' ($relsText.Contains('relationships/image')) 'нет типа'
        }
        finally { $zip.Dispose() }
        Remove-Item -LiteralPath $file.FullName -Force
    } else {
        CheckTrue 'файл скачан' $false 'не дождались'
    }

    Write-Host ''
    Write-Host '[7] логотип сохраняется и передаётся'
    $bundle = Eval 'JSON.stringify(WebEstimate.buildExchange())'
    CheckTrue 'логотип в файле обмена' ($bundle.Contains('logo-sample.png')) 'нет логотипа'
    [void](Wait-Cdp (Send-Cdp 'Page.navigate' @{ url = $url }))
    Start-Sleep -Milliseconds 2200
    CheckTrue 'логотип пережил перезагрузку' ((Eval 'WebEstimate.state.logo && WebEstimate.state.logo.name') -eq 'logo-sample.png') 'потерян'

    Write-Host ''
    Write-Host '[8] удаление логотипа'
    [void](Eval 'WebEstimate.removeLogo()')
    CheckTrue 'логотип удалён' ((Eval 'WebEstimate.state.logo === null') -eq $true) 'остался'
    CheckTrue 'кнопка «Убрать» скрыта' ((Eval 'document.getElementById("logoRemove").style.display') -eq 'none') 'видна'
    [void](Eval 'WebEstimate.showPreview()')
    Check 'картинки в смете нет' 0 (Eval 'document.querySelectorAll("#sheet img.logo").length')

    $socket.Dispose()
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    Remove-Item -LiteralPath $profile -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:failed -eq 0) { Write-Host 'ВСЕ ПРОВЕРКИ ЛОГОТИПА ПРОЙДЕНЫ'; exit 0 }
Write-Host ("ПРОВАЛЕНО: " + $script:failed)
exit 1
