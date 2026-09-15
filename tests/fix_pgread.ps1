# Переписывает чтение потока в клиенте PostgreSQL: буфер обрезается, а не растёт бесконечно.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }

# 1) заменяем поля буфера
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq 'private readonly List<byte> _buffer = new List<byte>();') {
        $lines[$i] = '        private byte[] _buffer = new byte[0];      // ещё не прочитанные байты'
        $done = 'поля'
        break
    }
}

# 2) заменяем ReadBytes и убираем лишние поля и Slice
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('private byte[] ReadBytes(int count)')) { $start = $i; break }
}
if ($start -lt 0) { throw 'метод ReadBytes не найден' }

# конец метода: строка с закрывающей скобкой метода перед Slice
$end = -1
for ($k = $start; $k -lt $lines.Count; $k++) {
    if ($lines[$k].Contains('private static IEnumerable<byte> Slice')) { $end = $k; break }
}
if ($end -lt 0) { throw 'конец ReadBytes не найден' }
# захватываем и Slice целиком
$sliceEnd = -1
for ($k = $end; $k -lt $lines.Count; $k++) {
    if ($lines[$k].Trim() -eq '}' -and $lines[$k - 1].Trim() -eq '}') { $sliceEnd = $k; break }
}

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

                // прочитанное убираем из буфера, чтобы он не рос
                if (take == _buffer.Length) _buffer = new byte[0];
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

$lines.RemoveRange($start, $sliceEnd - $start + 1)
$lines.InsertRange($start, [string[]]$new)
Write-Host 'чтение потока переписано'

# 3) убираем поля _offset, _chunk, _chunkOffset, _chunkLength
$temp = New-Object 'System.Collections.Generic.List[string]'
foreach ($line in $lines) {
    $t = $line.Trim()
    if ($t -eq 'private int _offset;') { continue }
    if ($t -eq 'private byte[] _chunk;') { continue }
    if ($t -eq 'private int _chunkOffset;') { continue }
    if ($t -eq 'private int _chunkLength;') { continue }
    if ($t -eq '_offset = 0;') { continue }
    if ($t -eq '_chunk = null;') { continue }
    if ($t -eq '_chunkOffset = 0;') { continue }
    if ($t -eq '_chunkLength = 0;') { continue }
    $temp.Add($line)
}
$lines = $temp
Write-Host 'лишние поля убраны'

[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))

# 4) проверяем сборку
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Remove-Item (Join-Path $root 'tests\Harness5.exe') -Force -ErrorAction SilentlyContinue
$out = & $csc /nologo /target:exe /platform:anycpu /utf8output /main:Harness5 "/out:$(Join-Path $root 'tests\Harness5.exe')" `
    /reference:System.dll (Join-Path $root 'src\Kotov.cs') $path (Join-Path $root 'tests\Harness5.cs') 2>&1
$out | Select-String -Pattern 'error CS' | Select-Object -First 6 | ForEach-Object { "  " + $_.Line }
Write-Host ("код сборки: " + $LASTEXITCODE)
