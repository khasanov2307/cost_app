# Проверка сохранения сметы документом Word из веб-версии.
# Открывает страницу в Яндекс Браузере, включает загрузки, нажимает «Сохранить в Word»
# и проверяет скачанный файл настоящим Microsoft Word.
param(
    [string]$Page = '',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'

$yandex = 'C:\Program Files\Yandex\YandexBrowser\Application\browser.exe'
if (-not (Test-Path -LiteralPath $yandex)) { throw 'Yandex Browser not found' }
if (-not (Test-Path -LiteralPath $Page)) { throw "no page: $Page" }
if ($OutDir -eq '') { $OutDir = Split-Path -Parent $Page }

$port = 9455
$profile = Join-Path ([System.IO.Path]::GetTempPath()) ('yandex-docx-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$url = 'file:///' + ($Page -replace '\\', '/')

$process = Start-Process -FilePath $yandex -PassThru -ArgumentList @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--allow-file-access-from-files', "--remote-debugging-port=$port", "--user-data-dir=$profile", 'about:blank'
)

$script:msgId = 0
$failed = 0

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
        if ($reply.result.exceptionDetails) { Write-Host ("        script error: " + $reply.result.exceptionDetails.text); return $null }
        return $reply.result.result.value
    }

    Send-Cdp 'Page.enable' | Out-Null
    Send-Cdp 'Runtime.enable' | Out-Null

    # разрешаем загрузки в отдельную папку
    $downloadDir = Join-Path $OutDir 'download'
    if (Test-Path -LiteralPath $downloadDir) { Remove-Item -LiteralPath $downloadDir -Recurse -Force }
    New-Item -ItemType Directory -Path $downloadDir | Out-Null
    [void](Send-Cdp 'Page.setDownloadBehavior' @{ behavior = 'allow'; downloadPath = $downloadDir })

    $navId = Send-Cdp 'Page.navigate' @{ url = $url }
    [void](Wait-Cdp $navId)
    Start-Sleep -Milliseconds 2000
    [void](Eval 'try { localStorage.removeItem(WebEstimate.storageKey); } catch (e) {}')
    $navId = Send-Cdp 'Page.navigate' @{ url = $url }
    [void](Wait-Cdp $navId)
    Start-Sleep -Milliseconds 2200

    Write-Host '[1] отмечаем позиции и сохраняем документ'
    [void](Eval 'document.getElementById("markAll").click()')
    CheckTrue 'all items marked' ((Eval 'WebEstimate.chosenRows().length') -eq 36) 'marking failed'
    CheckTrue 'docx parts built' ((Eval 'WebEstimate.buildDocx().length') -ge 5) 'parts missing'

    $size = Eval 'WebEstimate.zipStore(WebEstimate.buildDocx()).size'
    Write-Host ("        размер архива: " + $size + " байт")
    CheckTrue 'archive is not empty' ($size -gt 2000) "size: $size"

    [void](Eval 'WebEstimate.exportDocx()')

    Write-Host ''
    Write-Host '[2] ждём скачанный файл'
    $file = $null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $candidate = Get-ChildItem -LiteralPath $downloadDir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '*.docx' } | Select-Object -First 1
        if ($candidate -and $candidate.Length -gt 1000) { $file = $candidate; break }
    }

    if (-not $file) {
        CheckTrue 'file downloaded' $false 'no docx in the download folder'
        throw 'download failed'
    }
    Write-Host ("        файл: " + $file.FullName + "  (" + $file.Length + " байт)")

    $copy = Join-Path $OutDir 'Смета_из_браузера.docx'
    Copy-Item -LiteralPath $file.FullName -Destination $copy -Force

    Write-Host ''
    Write-Host '[3] проверяем структуру архива'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($copy)
    try {
        $parts = @($zip.Entries | ForEach-Object { $_.FullName })
        Write-Host ("        части: " + ($parts -join ', '))
        CheckTrue 'content types present' ($parts -contains '[Content_Types].xml') 'missing'
        CheckTrue 'document.xml present' ($parts -contains 'word/document.xml') 'missing'
        CheckTrue 'styles present' ($parts -contains 'word/styles.xml') 'missing'

        $entry = $zip.GetEntry('word/document.xml')
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        $xml = $reader.ReadToEnd()
        $reader.Close()

        $document = New-Object System.Xml.XmlDocument
        $document.LoadXml($xml)
        CheckTrue 'document.xml is valid XML' $true ''

        $ns = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
        $ns.AddNamespace('w', 'http://schemas.openxmlformats.org/wordprocessingml/2006/main')
        $rows = $document.SelectNodes('//w:tbl[1]/w:tr', $ns)
        $cells = $document.SelectNodes('//w:tbl[1]/w:tr[1]/w:tc', $ns)
        Write-Host ("        строк в таблице: " + $rows.Count + ", колонок: " + $cells.Count)
        CheckTrue 'table has 7 columns' ($cells.Count -eq 7) "columns: $($cells.Count)"
        CheckTrue 'table has 43 rows' ($rows.Count -eq 43) "rows: $($rows.Count)"
    }
    finally { $zip.Dispose() }

    Write-Host ''
    Write-Host '[4] открываем файл настоящим Word'
    $wordScript = Join-Path $OutDir 'word_verify.ps1'
    $wordCode = @'
param([string]$Docx)
$ErrorActionPreference = 'Stop'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $doc = $word.Documents.Open($Docx, $false, $true)
    Write-Host ("WORD_OK tables=" + $doc.Tables.Count + " rows=" + $doc.Tables.Item(1).Rows.Count + " pages=" + $doc.ComputeStatistics(2))
    $doc.Close($false)
} finally {
    $word.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
}
'@
    [System.IO.File]::WriteAllText($wordScript, $wordCode, (New-Object System.Text.UTF8Encoding($false)))
    $wordResult = & powershell -NoProfile -ExecutionPolicy Bypass -File $wordScript -Docx $copy 2>&1
    Write-Host ("        " + ($wordResult -join ' '))
    CheckTrue 'Word opened the document from the browser' (($wordResult -join ' ').Contains('WORD_OK')) 'Word could not open it'
    Remove-Item -LiteralPath $wordScript -Force -ErrorAction SilentlyContinue

    $socket.Dispose()
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    Remove-Item -LiteralPath $profile -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $OutDir 'download') -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:failed -eq 0) { Write-Host 'ALL DOCX CHECKS PASSED'; exit 0 }
Write-Host ("FAILED: " + $script:failed)
exit 1
