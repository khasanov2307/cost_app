# Исправляет разбор ответа: при ошибке ответ дочитывается до ReadyForQuery.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }

# 1) убираем временную трассировку
$temp = New-Object 'System.Collections.Generic.List[string]'
foreach ($line in $lines) {
    if ($line.Contains('[trace]')) { continue }
    $temp.Add($line)
}
$lines = $temp
Write-Host 'трассировка убрана'

# 2) заменяем метод Execute целиком
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('private List<PgRow> Execute(string sql)')) { $start = $i; break }
}
if ($start -lt 0) { throw 'метод Execute не найден' }

$depth = 0
$end = -1
for ($k = $start; $k -lt $lines.Count; $k++) {
    foreach ($ch in $lines[$k].ToCharArray()) {
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') { $depth-- }
    }
    if ($k -gt $start -and $depth -eq 0) { $end = $k; break }
}
if ($end -lt 0) { throw 'конец метода Execute не найден' }
Write-Host ("метод Execute: строки " + ($start + 1) + ".." + ($end + 1))

$new = @'
        private List<PgRow> Execute(string sql)
        {
            _lastError = null;
            _lastSqlState = null;

            try
            {
                List<byte> body = new List<byte>();
                AddCString(body, sql);
                WriteMessage((byte)'Q', body);

                List<PgRow> rows = new List<PgRow>();
                List<string> columns = new List<string>();
                bool failed = false;

                // Ответ читаем до сообщения ReadyForQuery: если его не дочитать,
                // следующий запрос получит чужой результат.
                while (true)
                {
                    int type;
                    byte[] payload = ReadMessage(out type);

                    if (type == 'T') columns = ReadRowDescription(payload);
                    else if (type == 'D') { if (!failed) rows.Add(ReadDataRow(payload, columns)); }
                    else if (type == 'C') continue;                       // тег команды
                    else if (type == 'N') continue;                       // замечание сервера
                    else if (type == 'S') { RememberParameter(payload); continue; }
                    else if (type == 'E')
                    {
                        if (_lastError == null)
                        {
                            _lastError = Describe(payload);
                            _lastSqlState = SqlState(payload);
                        }
                        failed = true;
                    }
                    else if (type == 'Z')
                    {
                        if (failed) return null;
                        return rows;
                    }
                }
            }
            catch (PgException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PgException("Ошибка при работе с базой данных: " + ex.Message);
            }
        }
'@ -split "`r?`n"

$lines.RemoveRange($start, $end - $start + 1)
$lines.InsertRange($start, [string[]]$new)
[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'метод Execute заменён'

# 3) проверяем последовательность и набор проверок
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($name in @('PgSeq', 'Harness5')) {
    $exe = Join-Path $root ('tests\' + $name + '.exe')
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    $src = Join-Path $root ('tests\' + $name + '.cs')
    $out = & $csc /nologo /target:exe /platform:anycpu /utf8output "/main:$name" "/out:$exe" `
        /reference:System.dll (Join-Path $root 'src\Kotov.cs') $path $src 2>&1
    $errs = $out | Select-String -Pattern 'error CS'
    if ($errs) { Write-Host ('ошибки сборки ' + $name + ':'); $errs | ForEach-Object { "  " + $_.Line } }
}
Write-Host ''
Write-Host '--- последовательность запросов ---'
& (Join-Path $root 'tests\PgSeq.exe')
