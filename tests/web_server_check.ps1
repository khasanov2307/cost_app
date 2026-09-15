# Проверка связки: сервис программы и страница «Смета.html» в Яндекс Браузере.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$page = Join-Path $root 'bin\Смета.html'
$outDir = Join-Path $root 'tests'
$port = 8801

# маленькая программа, поднимающая сервис над файловым хранилищем
$starter = Join-Path $root 'tests\start_service.ps1'
$starterCode = @'
param([string]$Folder, [int]$Port)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\khasa\Документы\DeepSeek Workspace\Calculate'
'@
# вместо внешней программы используем уже собранный Harness8 (см. ниже)
if (-not (Test-Path (Join-Path $outDir 'Harness8.exe'))) {
    throw 'Сначала соберите tests\Harness8.exe'
}

$yandex = 'C:\Program Files\Yandex\YandexBrowser\Application\browser.exe'
$profile = Join-Path ([System.IO.Path]::GetTempPath()) ('yandex-srv-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$url = 'file:///' + ($page -replace '\\', '/')

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

# хранилище проверки начинаем с чистого листа
$serviceStore = Join-Path ([System.IO.Path]::GetTempPath()) 'kotov-service'
Remove-Item -LiteralPath $serviceStore -Recurse -Force -ErrorAction SilentlyContinue

# 1) поднимаем сервис отдельным процессом
$service = Start-Process -FilePath (Join-Path $outDir 'Harness8.exe') -ArgumentList @($port) -PassThru -WindowStyle Hidden
$serviceReady = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 400
    try {
        $probe = Invoke-RestMethod "http://127.0.0.1:$port/api/health" -TimeoutSec 2
        if ($probe.ok) { $serviceReady = $true; break }
    } catch { }
}
if (-not $serviceReady) { Stop-Process -Id $service.Id -Force -ErrorAction SilentlyContinue; throw 'сервис не поднялся' }
Write-Host ("сервис поднят: " + $probe.store)

$browser = Start-Process -FilePath $yandex -PassThru -ArgumentList @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--allow-file-access-from-files', '--window-size=1280,1000',
    "--remote-debugging-port=$($port + 100)", "--user-data-dir=$profile", 'about:blank')

try {
    $targets = $null
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 400
        try {
            $targets = Invoke-RestMethod "http://127.0.0.1:$($port + 100)/json/list" -TimeoutSec 3
            if ($targets) { break }
        } catch { }
    }
    if (-not $targets) { throw 'браузер не открыл отладочный порт' }

    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $socket.ConnectAsync([Uri](($targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1).webSocketDebuggerUrl),
        [Threading.CancellationToken]::None).Wait()

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
        for ($i = 0; $i -lt 800; $i++) {
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
            Write-Host ("        ошибка: " + $(if ($d.exception.description) { $d.exception.description } else { $d.text }))
            return $null
        }
        return $reply.result.result.value
    }

    Send-Cdp 'Page.enable' | Out-Null
    Send-Cdp 'Runtime.enable' | Out-Null
    [void](Wait-Cdp (Send-Cdp 'Page.navigate' @{ url = $url }))
    Start-Sleep -Milliseconds 2000
    [void](Eval 'try { localStorage.removeItem(WebEstimate.storageKey); localStorage.removeItem("raschet-smeta-server"); } catch (e) {}')
    [void](Wait-Cdp (Send-Cdp 'Page.navigate' @{ url = $url }))
    Start-Sleep -Milliseconds 2200

    Write-Host ''
    Write-Host '[1] страница без сервиса'
    Check 'режим — память браузера' $false (Eval 'WebEstimate.server.online')
    CheckTrue 'подсказка о режиме верна' ((Eval 'document.getElementById("storageHint").textContent').Contains([string][char]0x0431 + [char]0x0440 + [char]0x0430 + [char]0x0443 + [char]0x0437 + [char]0x0435 + [char]0x0440)) 'неверная подпись'

    Write-Host ''
    Write-Host '[2] подключение к сервису программы'
    $address = "http://127.0.0.1:$port"
    [void](Eval ("WebEstimate.connectServer('" + $address + "')"))
    Start-Sleep -Milliseconds 2500
    Check 'сервис подключён' $true (Eval 'WebEstimate.server.online')
    CheckTrue 'адрес запомнен' ((Eval 'WebEstimate.server.url') -eq $address) 'адрес другой'
    CheckTrue 'прайс пришёл от сервиса' ((Eval 'WebEstimate.state.items.length') -gt 30) 'позиций мало'
    CheckTrue 'подсказка сменилась' ((Eval 'document.getElementById("storageHint").textContent').Contains([string][char]0x0431 + [char]0x0430 + [char]0x0437 + [char]0x0435)) 'подпись прежняя'
    CheckTrue 'кнопка «Подключить» скрыта' ((Eval 'document.getElementById("serverConnect").style.display') -eq 'none') 'видна'

    Write-Host ''
    Write-Host '[3] правка прайса уходит в хранилище'
    $before = (Invoke-RestMethod "$address/api/data").prices.Count
    [void](Eval 'WebEstimate.state.items.push({ group: "Проверка сервиса", article: "SRV-1", name: "Услуга из браузера", unit: "услуга", price: 123.45 }); WebEstimate.save(); "ок"')
    Start-Sleep -Milliseconds 1600
    $after = Invoke-RestMethod "$address/api/data"
    Check 'позиция добавилась в хранилище' ($before + 1) $after.prices.Count
    $found = $after.prices | Where-Object { $_.article -eq 'SRV-1' }
    CheckTrue 'позиция найдена по артикулу' ($found -ne $null) 'не найдена'
    Check 'цена округлилась верно' '123.45' ([string]$found.price)

    Write-Host ''
    Write-Host '[4] реквизиты уходят в хранилище'
    [void](Eval 'WebEstimate.state.document.number = "77/2026"; WebEstimate.state.document.customer = "Из браузера"; WebEstimate.save(); "ок"')
    Start-Sleep -Milliseconds 1600
    $after = Invoke-RestMethod "$address/api/data"
    Check 'номер сметы сохранён' '77/2026' $after.document.number
    Check 'заказчик сохранён' 'Из браузера' $after.document.customer

    Write-Host ''
    Write-Host '[5] отметки сметы уходят в хранилище'
    [void](Eval 'WebEstimate.markAll(false); WebEstimate.togglePick(WebEstimate.keyOf(WebEstimate.state.items[0]), true); WebEstimate.setQuantity(WebEstimate.keyOf(WebEstimate.state.items[0]), "5"); WebEstimate.save(); "ок"')
    Start-Sleep -Milliseconds 1600
    $state = Invoke-RestMethod "$address/api/state"
    CheckTrue 'смета сохранена в хранилище' ($state.chosen.PSObject.Properties.Count -ge 1) 'отметок нет'

    Write-Host ''
    Write-Host '[6] данные видны другому клиенту'
    $second = Invoke-RestMethod "$address/api/data"
    CheckTrue 'смета передаётся вместе с данными' ($second.state -ne $null) 'сметы нет'
    Write-Host ("        артикулы: " + (($second.prices | ForEach-Object { $_.article }) -join ", "))
    CheckTrue 'прайс с правкой из браузера' (@($second.prices | Where-Object { $_.article -eq 'SRV-1' }).Count -eq 1) 'правки нет'

    Write-Host ''
    Write-Host '[7] отключение от сервиса'
    [void](Eval 'WebEstimate.disconnectServer()')
    Start-Sleep -Milliseconds 400
    Check 'сервис отключён' $false (Eval 'WebEstimate.server.online')
    CheckTrue 'кнопка «Отключить» скрыта' ((Eval 'document.getElementById("serverDisconnect").style.display') -eq 'none') 'видна'

    $socket.Dispose()
}
finally {
    if ($browser -and -not $browser.HasExited) { Stop-Process -Id $browser.Id -Force -ErrorAction SilentlyContinue }
    if ($service -and -not $service.HasExited) { Stop-Process -Id $service.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    Remove-Item -LiteralPath $profile -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:failed -eq 0) { Write-Host 'ВСЕ ПРОВЕРКИ СВЯЗКИ ПРОЙДЕНЫ'; exit 0 }
Write-Host ("ПРОВАЛЕНО: " + $script:failed)
exit 1
