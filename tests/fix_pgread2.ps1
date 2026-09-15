# Заменяет чтение потока в клиенте PostgreSQL на работу с массивом байтов.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }

# 1) поле буфера
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq 'private readonly List<byte> _buffer = new List<byte>();') {
        $lines[$i] = '        private byte[] _buffer = new byte[0];      // ещё не обработанные байты ответа'
        break
    }
}

# 2) метод ReadBytes: от строки с сигнатурой до закрывающей скобки метода
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('private byte[] ReadBytes(int count)')) { $start = $i; break }
}
if ($start -lt 0) { throw 'метод ReadBytes не найден' }

$end = -1
$depth = 0
for ($k = $start; $k -lt $lines.Count; $k++) {
    foreach ($ch in $lines[$k].ToCharArray()) {
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') { $depth-- }
    }
    if ($k -gt $start -and $depth -eq 0) { $end = $k; break }
}
if ($end -lt 0) { throw 'конец метода ReadBytes не найден' }
Write-Host ("метод ReadBytes: строки " + ($start + 1) + ".." + ($end + 1))

$new = @'
        private byte[] ReadBytes(int count)
        {
            byte[] result = new byte[count];
            int filled = 0;

            while (filled < count)
            {
                if (_buffer.Length == 0)
                {
                    byte[] chunk = new byte[16384];
                    int read = _stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0) throw new PgException("Соединение с базой данных закрыто сервером.");

                    _buffer = new byte[read];
                    Array.Copy(chunk, 0, _buffer, 0, read);
                }

                int take = Math.Min(count - filled, _buffer.Length);
                Array.Copy(_buffer, 0, result, filled, take);
                filled += take;

                // прочитанные байты убираем из буфера, чтобы он не рос
                if (take == _buffer.Length)
                {
                    _buffer = new byte[0];
                }
                else
                {
                    byte[] rest = new byte[_buffer.Length - take];
                    Array.Copy(_buffer, take, rest, 0, rest.Length);
                    _buffer = rest;
                }
            }

            return result;
        }
'@ -split "`r?`n"

$lines.RemoveRange($start, $end - $start + 1)
$lines.InsertRange($start, [string[]]$new)

# 3) убираем поле _offset и его сброс
$temp = New-Object 'System.Collections.Generic.List[string]'
foreach ($line in $lines) {
    $t = $line.Trim()
    if ($t -eq 'private int _offset;') { continue }
    if ($t -eq '_offset = 0;') { continue }
    $temp.Add($line)
}
$lines = $temp

[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'чтение потока заменено'

# 4) сборка и прогон
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $root 'tests\Harness5.exe'
Remove-Item $exe -Force -ErrorAction SilentlyContinue
$out = & $csc /nologo /target:exe /platform:anycpu /utf8output /main:Harness5 "/out:$exe" `
    /reference:System.dll (Join-Path $root 'src\Kotov.cs') $path (Join-Path $root 'tests\Harness5.cs') 2>&1
$out | Select-String -Pattern 'error CS' | Select-Object -First 6 | ForEach-Object { "  " + $_.Line }
Write-Host ("код сборки: " + $LASTEXITCODE)
