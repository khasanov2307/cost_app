# Открывает веб-версию в Яндекс Браузере, выполняет проверки и снимает снимки.
# Только ASCII, чтобы файл не зависел от кодировки консоли.
param(
    [string]$Page = '',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'

$yandex = 'C:\Program Files\Yandex\YandexBrowser\Application\browser.exe'
if (-not (Test-Path -LiteralPath $yandex)) { throw 'Yandex Browser not found' }
if (-not (Test-Path -LiteralPath $Page)) { throw "no page: $Page" }

if ($OutDir -eq '') { $OutDir = Split-Path -Parent $Page }
$port = 9444
$profile = Join-Path ([System.IO.Path]::GetTempPath()) ('yandex-web-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$url = 'file:///' + ($Page -replace '\\', '/')

$process = Start-Process -FilePath $yandex -PassThru -ArgumentList @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--allow-file-access-from-files', '--window-size=1280,1000',
    "--remote-debugging-port=$port", "--user-data-dir=$profile", 'about:blank'
)

$script:failed = 0
$script:msgId = 0

function Check($what, $expected, $actual) {
    $ok = ($expected -eq $actual)
    if (-not $ok) { $script:failed++ }
    if ($ok) { Write-Host ("  PASS  " + $what) }
    else { Write-Host ("  FAIL  " + $what + "   expected=<" + $expected + "> actual=<" + $actual + ">") }
}

function CheckTrue($what, $value, $details) {
    if (-not $value) { $script:failed++ }
    if ($value) { Write-Host ("  PASS  " + $what) }
    else { Write-Host ("  FAIL  " + $what + "   " + $details) }
}

try {
    $targets = $null
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 400
        try {
            $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$port/json/list" -TimeoutSec 3
            if ($targets) { break }
        } catch { }
    }
    if (-not $targets) { throw 'debug port did not start' }

    $target = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
    if (-not $target) { throw 'no page target' }

    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $socket.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()

    function Send-Cdp([string]$method, $params) {
        $script:msgId++
        $payload = @{ id = $script:msgId; method = $method }
        if ($params) { $payload['params'] = $params }
        $json = $payload | ConvertTo-Json -Depth 8 -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
        $socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true,
            [Threading.CancellationToken]::None).Wait()
        return $script:msgId
    }

    function Receive-Cdp() {
        $buffer = New-Object byte[] 8388608
        $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
        $builder = New-Object System.Text.StringBuilder
        do {
            $result = $socket.ReceiveAsync($segment, [Threading.CancellationToken]::None).Result
            [void]$builder.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
        } while (-not $result.EndOfMessage)
        return $builder.ToString()
    }

    function Wait-Cdp([int]$id, [int]$timeoutMs = 30000) {
        $deadline = (Get-Date).AddMilliseconds($timeoutMs)
        while ((Get-Date) -lt $deadline) {
            $text = Receive-Cdp
            if (-not $text) { continue }
            $message = $text | ConvertFrom-Json
            if ($message.id -eq $id) { return $message }
        }
        return $null
    }

    function Eval([string]$expression) {
        $id = Send-Cdp 'Runtime.evaluate' @{ expression = $expression; returnByValue = $true; awaitPromise = $true }
        $reply = Wait-Cdp $id
        if (-not $reply) { return $null }
        if ($reply.result.exceptionDetails) {
            Write-Host ("        script error: " + $reply.result.exceptionDetails.text)
            return $null
        }
        return $reply.result.result.value
    }

    Send-Cdp 'Page.enable' | Out-Null
    Send-Cdp 'Runtime.enable' | Out-Null
    Start-Sleep -Milliseconds 300

    $navId = Send-Cdp 'Page.navigate' @{ url = $url }
    [void](Wait-Cdp $navId)
    Start-Sleep -Milliseconds 2500

    # каждый прогон начинается с чистого состояния: убираем данные прошлого запуска
    [void](Eval 'try { localStorage.removeItem(WebEstimate.storageKey); } catch (e) {}')
    $navId = Send-Cdp 'Page.navigate' @{ url = $url }
    [void](Wait-Cdp $navId)
    Start-Sleep -Milliseconds 2000

    Write-Host ("title: " + (Eval 'document.title'))
    Write-Host ("url: " + (Eval 'location.href'))

    Write-Host ''
    Write-Host '[1] page loaded and price list is present'
    CheckTrue 'api available' ((Eval 'typeof window.WebEstimate') -eq 'object') 'WebEstimate is missing'
    Check 'factory prices loaded' 36 (Eval 'WebEstimate.state.items.length')
    Check 'groups in the list' 5 (Eval 'WebEstimate.groups().length')
    CheckTrue 'table rendered' ((Eval 'document.querySelectorAll("#list tbody tr").length') -ge 30) 'rows missing'
    CheckTrue 'initial total' ((Eval 'document.getElementById("total").textContent').StartsWith('ИТОГО: 0,00')) 'total is not zero'

    Write-Host ''
    Write-Host '[2] checking items recalculates the total'
    CheckTrue 'mark first item' ((Eval 'var it=WebEstimate.state.items[0]; WebEstimate.togglePick(WebEstimate.keyOf(it), true); !!WebEstimate.state.chosen[WebEstimate.keyOf(it)]') -eq $true) 'mark was not set'
    Check 'total after one item' 1500 (Eval 'WebEstimate.totals().payable')
    Check 'checkbox is checked' $true (Eval 'document.querySelector("#list tbody input[type=checkbox]").checked')
    Check 'row is highlighted' $true (Eval 'document.querySelector("#list tbody tr").classList.contains("picked")')

    Write-Host ''
    Write-Host '[3] quantity changes the sum'
    CheckTrue 'quantity 2 accepted' ((Eval 'WebEstimate.setQuantity(WebEstimate.keyOf(WebEstimate.state.items[0]), "2")') -eq $true) 'setQuantity failed'
    Check 'total for quantity 2' 3000 (Eval 'WebEstimate.totals().payable')
    Check 'sum shown in the row' '3 000,00' (Eval 'document.querySelector("#list tbody tr td.sum").textContent')
    Check 'bad quantity rejected' $false (Eval 'WebEstimate.setQuantity(WebEstimate.keyOf(WebEstimate.state.items[0]), "abc")')

    Write-Host ''
    Write-Host '[4] mark all and reset'
    [void](Eval 'document.getElementById("clearMarks").click()')
    [void](Eval 'document.getElementById("markAll").click()')
    Check 'all 36 items chosen' 36 (Eval 'WebEstimate.chosenRows().length')
    Check 'unit count' 36 (Eval 'var n=0; var r=WebEstimate.chosenRows(); for (var i=0;i<r.length;i++) n+=r[i].quantity; n')
    CheckTrue 'total is the sum of all prices' ((Eval 'Math.round(WebEstimate.totals().payable*100)/100') -gt 50000) 'total looks wrong'
    [void](Eval 'document.getElementById("clearMarks").click()')
    Check 'marks cleared' 0 (Eval 'WebEstimate.chosenRows().length')

    Write-Host ''
    Write-Host '[5] search filters the list'
    [void](Eval 'var s=document.getElementById("search"); s.value="масл"; s.dispatchEvent(new Event("input"))')
    $filtered = Eval 'var n=0; var rows=document.querySelectorAll("#list .group-head"); for (var i=0;i<rows.length;i++) n++; n'
    Write-Host ("        visible groups: " + $filtered)
    CheckTrue 'search narrows the list' ($filtered -lt 5 -and $filtered -ge 1) "groups: $filtered"
    [void](Eval 'var s=document.getElementById("search"); s.value=""; s.dispatchEvent(new Event("input"))')
    Check 'search reset shows all groups' 5 (Eval 'document.querySelectorAll("#list .group-head").length')

    Write-Host ''
    Write-Host '[6] sections collapse by click'
    [void](Eval 'document.querySelector("#list .group-head").click()')
    Check 'first section collapsed' 4 (Eval 'document.querySelectorAll("#list table.items").length')
    Check 'marker changed to plus' '+' (Eval 'document.querySelector("#list .group-head .marker").textContent')
    [void](Eval 'document.querySelector("#list .group-head").click()')
    Check 'first section expanded again' 5 (Eval 'document.querySelectorAll("#list table.items").length')

    Write-Host ''
    Write-Host '[7] estimate sheet'
    [void](Eval 'WebEstimate.togglePick(WebEstimate.keyOf(WebEstimate.state.items[0]), true)')
    [void](Eval 'WebEstimate.setQuantity(WebEstimate.keyOf(WebEstimate.state.items[0]), "3")')
    [void](Eval 'document.getElementById("estimate").click()')
    Check 'sheet is open' $true (Eval 'document.getElementById("overlay").classList.contains("open")')
    CheckTrue 'sheet has the title' ((Eval 'document.querySelector("#sheet h1").textContent').StartsWith([string][char]0x0417 + [char]0x0410 + [char]0x042F + [char]0x0412 + [char]0x041A + [char]0x0410)) 'title missing'
    Check 'estimate rows' 1 (Eval 'document.querySelectorAll("#sheet table.estimate tbody tr").length - 2')
    CheckTrue 'total row present' ((Eval 'document.querySelector("#sheet .total-row").textContent').Contains('4')) 'total row missing'
    CheckTrue 'signature rows present' ((Eval 'document.querySelector("#sheet table.sign").textContent').Length -gt 20) 'signatures missing'

    Write-Host ''
    Write-Host '[8] price list editor'
    [void](Eval 'WebEstimate.showTab("price")')
    Check 'editor rows' 36 (Eval 'document.querySelectorAll("#editor tbody tr").length')
    Check 'unit dropdown available' 4 (Eval 'var s=document.querySelector("#editor select"); s.options.length >= 4 ? 4 : s.options.length')
    [void](Eval 'document.getElementById("addItem").click()')
    Check 'item added' 37 (Eval 'WebEstimate.state.items.length')

    Write-Host ''
    Write-Host '[9] data survives a reload (localStorage)'
    [void](Eval 'WebEstimate.save()')
    $navId = Send-Cdp 'Page.navigate' @{ url = $url }
    [void](Wait-Cdp $navId)
    Start-Sleep -Milliseconds 2200
    Check 'items restored from storage' 37 (Eval 'WebEstimate.state.items.length')
    Check 'checked item restored' 1 (Eval 'WebEstimate.chosenRows().length')
    Check 'quantity restored' 3 (Eval 'WebEstimate.chosenRows()[0].quantity')

    Write-Host ''
    Write-Host '[10] discount and document fields'
    [void](Eval 'WebEstimate.markAll(false)')
    [void](Eval 'WebEstimate.togglePick(WebEstimate.keyOf(WebEstimate.state.items[0]), true)')
    [void](Eval 'WebEstimate.setQuantity(WebEstimate.keyOf(WebEstimate.state.items[0]), "2")')
    Check 'subtotal without discount' 3000 (Eval 'WebEstimate.totals().subtotal')
    Check 'discount applied' 300 (Eval 'WebEstimate.state.document.discount = 10; WebEstimate.totals().discount')
    Check 'payable after discount' 2700 (Eval 'WebEstimate.totals().payable')
    Check 'discount limited to 90' 90 (Eval 'WebEstimate.state.document.discount = 150; WebEstimate.totals().percent')
    [void](Eval 'WebEstimate.state.document.discount = 0; WebEstimate.state.document.number = "12/2026"; WebEstimate.state.document.customer = "Иванов Иван Иванович"; WebEstimate.save(); WebEstimate.render()')
    CheckTrue 'total shows discount in the bar' ((Eval 'document.getElementById("counter").textContent').Length -gt 20) 'counter text missing'

    Write-Host ''
    Write-Host '[11] price changed right in the estimate'
    [void](Eval 'WebEstimate.setPrice(WebEstimate.keyOf(WebEstimate.state.items[0]), "1000")')
    Check 'new price applied' 1000 (Eval 'WebEstimate.chosenRows()[0].price')
    Check 'sum follows the price' 2000 (Eval 'WebEstimate.totals().subtotal')
    CheckTrue 'row is marked as changed' ((Eval 'WebEstimate.chosenRows()[0].changed') -eq $true) 'not marked'
    Check 'catalog price untouched' 1500 (Eval 'WebEstimate.state.items[0].price')
    [void](Eval 'WebEstimate.setPrice(WebEstimate.keyOf(WebEstimate.state.items[0]), "1500")')
    CheckTrue 'price reset removes the mark' ((Eval 'WebEstimate.chosenRows()[0].changed') -eq $false) 'still marked'

    Write-Host ''
    Write-Host '[12] templates of service sets'
    [void](Eval 'WebEstimate.markAll(false)')
    [void](Eval 'WebEstimate.togglePick(WebEstimate.keyOf(WebEstimate.state.items[0]), true)')
    [void](Eval 'WebEstimate.togglePick(WebEstimate.keyOf(WebEstimate.state.items[1]), true)')
    [void](Eval 'WebEstimate.setQuantity(WebEstimate.keyOf(WebEstimate.state.items[1]), "4")')
    [void](Eval 'WebEstimate.saveTemplate("ТО-1", function () { return true; })')
    Check 'template saved' 1 (Eval 'WebEstimate.state.templates.length')
    Check 'template items' 2 (Eval 'WebEstimate.state.templates[0].items.length')
    Check 'quantity kept in template' 4 (Eval 'WebEstimate.state.templates[0].items[1].quantity')
    [void](Eval 'WebEstimate.markAll(false)')
    Check 'marks cleared before applying' 0 (Eval 'WebEstimate.chosenRows().length')
    [void](Eval 'document.getElementById("templateBox").value = "ТО-1"; WebEstimate.applyTemplate()')
    Check 'template applied' 2 (Eval 'WebEstimate.chosenRows().length')
    Check 'quantity restored from template' 4 (Eval 'WebEstimate.chosenRows()[1].quantity')
    Check 'template search finds it' 1 (Eval 'WebEstimate.findTemplates("ТО").length')
    Check 'template search by service name' 1 (Eval 'WebEstimate.findTemplates("диагностика").length')
    Check 'template search without match' 0 (Eval 'WebEstimate.findTemplates("тормоз").length')

    Write-Host ''
    Write-Host '[13] dark theme'
    Check 'theme is light at start' 'light' (Eval 'WebEstimate.state.theme')
    [void](Eval 'document.getElementById("themeButton").click()')
    Check 'theme switched to dark' 'dark' (Eval 'WebEstimate.state.theme')
    CheckTrue 'body has dark class' ((Eval 'document.body.className') -eq 'dark') 'no dark class'
    CheckTrue 'button label changed' ((Eval 'document.getElementById("themeButton").textContent').Contains([string][char]0x0421 + [char]0x0432 + [char]0x0435 + [char]0x0442)) 'wrong label'
    [void](Eval 'document.getElementById("themeButton").click()')
    Check 'theme returned to light' 'light' (Eval 'WebEstimate.state.theme')

    Write-Host ''
    Write-Host '[14] exchange file for both versions'
    $bundle = Eval 'JSON.stringify(WebEstimate.buildExchange())'
    CheckTrue 'exchange file built' ($bundle -ne $null -and $bundle.Length -gt 500) 'too small'
    CheckTrue 'format is raschet-smeta' ($bundle.Contains('raschet-smeta')) 'wrong format'
    CheckTrue 'prices are inside' ($bundle.Contains('Компьютерная диагностика')) 'no prices'
    CheckTrue 'templates are inside' ($bundle.Contains('ТО-1')) 'no templates'
    CheckTrue 'document fields are inside' ($bundle.Contains('12/2026') -and $bundle.Contains('Иванов')) 'no fields'

    Write-Host ''
    Write-Host '[15] preview sheet with discount'
    [void](Eval 'WebEstimate.state.document.discount = 10; WebEstimate.save(); WebEstimate.showPreview()')
    Check 'sheet is open again' $true (Eval 'document.getElementById("overlay").classList.contains("open")')
    CheckTrue 'sheet has discount row' ((Eval 'document.querySelector("#sheet").textContent').Contains([string][char]0x0421 + [char]0x043A + [char]0x0438 + [char]0x0434 + [char]0x043A + [char]0x0430)) 'no discount row'
    CheckTrue 'sheet has customer' ((Eval 'document.querySelector("#sheet").textContent').Contains('Иванов')) 'no customer'
    CheckTrue 'sheet has number' ((Eval 'document.querySelector("#sheet").textContent').Contains('12/2026')) 'no number'
    [void](Eval 'WebEstimate.state.document.discount = 0; WebEstimate.save(); WebEstimate.render()')
    Write-Host ''
    Write-Host ''
    Write-Host '[16] clearing the whole price list'
    Write-Host ('        тип функции: ' + (Eval 'typeof WebEstimate.clearPrices'))
    [void](Eval 'WebEstimate.showTab("price")')
    CheckTrue 'wrong word does not clear' ((Eval 'WebEstimate.clearPrices(String.fromCharCode(1091,1076,1072,1083,1080,1090,1100))') -eq $false) 'cleared anyway'
    Check 'price list untouched' 37 (Eval 'WebEstimate.state.items.length')
    CheckTrue 'right word clears' ((Eval 'WebEstimate.clearPrices(WebEstimate.clearWord)') -eq $true) 'not cleared'
    Check 'price list is empty' 0 (Eval 'WebEstimate.state.items.length')
    Check 'marks are cleared too' 0 (Eval 'WebEstimate.chosenRows().length')
    CheckTrue 'undo button is shown' ((Eval 'document.getElementById("undoClear").style.display') -ne 'none') 'button hidden'
    CheckTrue 'empty list is saved' ((Eval 'String(localStorage.getItem(WebEstimate.storageKey)).indexOf(String.fromCharCode(34) + "items" + String.fromCharCode(34) + ":[]") > 0') -eq $true) 'not saved'
    CheckTrue 'clearing again reports empty' ((Eval 'WebEstimate.clearPrices(WebEstimate.clearWord)') -eq $false) 'cleared twice'

    Write-Host ''
    Write-Host '[17] undoing the clear'
    CheckTrue 'undo restores prices' ((Eval 'WebEstimate.undoClear()') -eq $true) 'not restored'
    Check 'prices are back' 37 (Eval 'WebEstimate.state.items.length')
    CheckTrue 'undo button hidden again' ((Eval 'document.getElementById("undoClear").style.display') -eq 'none') 'still visible'
    Check 'undo once only' $false (Eval 'WebEstimate.undoClear()')
    [void](Eval 'WebEstimate.showTab("calc"); WebEstimate.render()')

    Write-Host '[18] screenshots'
    # светлая тема: страница расчёта без открытого окна сметы
    [void](Eval 'document.getElementById("overlay").classList.remove("open"); WebEstimate.state.theme = "light"; WebEstimate.save(); WebEstimate.showTab("calc"); WebEstimate.render()')
    Start-Sleep -Milliseconds 400
    $shotId = Send-Cdp 'Page.captureScreenshot' @{ format = 'png' }
    $shot = Wait-Cdp $shotId
    if ($shot -and $shot.result.data) {
        $file = Join-Path $OutDir 'web_page.png'
        [System.IO.File]::WriteAllBytes($file, [Convert]::FromBase64String($shot.result.data))
        Write-Host ("        saved: " + $file + " (" + (Get-Item -LiteralPath $file).Length + " bytes)")
    }

    # тёмная тема: контуры и текст должны оставаться читаемыми
    [void](Eval 'WebEstimate.toggleTheme(); WebEstimate.render()')
    Start-Sleep -Milliseconds 400
    $shotId = Send-Cdp 'Page.captureScreenshot' @{ format = 'png' }
    $shot = Wait-Cdp $shotId
    if ($shot -and $shot.result.data) {
        $file = Join-Path $OutDir 'web_dark.png'
        [System.IO.File]::WriteAllBytes($file, [Convert]::FromBase64String($shot.result.data))
        Write-Host ("        saved: " + $file + " (" + (Get-Item -LiteralPath $file).Length + " bytes)")
    }

    # окно сметы со скидкой
    [void](Eval 'WebEstimate.toggleTheme(); WebEstimate.state.document.discount = 10; WebEstimate.save(); WebEstimate.showPreview()')
    Start-Sleep -Milliseconds 400
    $shotId = Send-Cdp 'Page.captureScreenshot' @{ format = 'png' }
    $shot = Wait-Cdp $shotId
    if ($shot -and $shot.result.data) {
        $file = Join-Path $OutDir 'web_estimate.png'
        [System.IO.File]::WriteAllBytes($file, [Convert]::FromBase64String($shot.result.data))
        Write-Host ("        saved: " + $file + " (" + (Get-Item -LiteralPath $file).Length + " bytes)")
    }

    $socket.Dispose()
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    Remove-Item -LiteralPath $profile -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:failed -eq 0) { Write-Host 'ALL WEB CHECKS PASSED'; exit 0 }
Write-Host ("FAILED: " + $script:failed)
exit 1
