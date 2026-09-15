# Исправляет клиент PostgreSQL: сохранение сообщения авторизации и чтение потока.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }
$done = New-Object 'System.Collections.Generic.List[string]'

# 1) поле для сообщения авторизации
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq 'private int _passwordRequest;') {
        $lines.Insert($i + 1, '        private byte[] _authPayload;        // тело сообщения с солью или списком механизмов')
        $done.Add('поле')
        break
    }
}

# 2) ReadStartupResponse: сохраняем тело сообщения авторизации
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('if (code == 3) _passwordRequest = 3;')) {
        # заменяем весь блок разбора кодов
        $end = -1
        for ($k = $i; $k -lt $i + 12; $k++) {
            if ($lines[$k].Contains('return;') -and $lines[$k - 1].Trim() -eq '}') { $end = $k; break }
        }
        $new = @(
'                    _authPayload = payload;',
'                    if (code == 3) _passwordRequest = 3;           // пароль открытым текстом',
'                    else if (code == 5) _passwordRequest = 5;      // md5',
'                    else if (code == 10) _passwordRequest = 10;    // scram-sha-256',
'                    else throw new PgException("Сервер требует незнакомый способ авторизации: " + code);',
'                    return;'
        )
        $lines.RemoveRange($i, $end - $i + 1)
        $lines.InsertRange($i, [string[]]$new)
        $done.Add('сохранение сообщения')
        break
    }
}

# 3) Authenticate: используем сохранённое сообщение вместо повторного чтения
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('byte[] payload = ReadAuthPayload();') -and $lines[$i + 1].Contains('string salt = Hex(payload, 4, 4);')) {
        $lines[$i] = '                byte[] payload = _authPayload;'
        $done.Add('md5')
    }
    if ($lines[$i].Contains('byte[] payload = ReadAuthPayload();') -and $lines[$i + 1].Contains('ScramAuthenticate(payload);')) {
        $lines[$i] = '                ScramAuthenticate(_authPayload);'
        $lines.RemoveAt($i + 1)
        $done.Add('scram')
    }
}

# 4) убираем ставший ненужным метод чтения тела авторизации
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('private byte[] ReadAuthPayload()')) { $start = $i; break }
}
if ($start -ge 0) {
    $end = -1
    for ($k = $start; $k -lt $start + 8; $k++) {
        if ($lines[$k].Trim() -eq '}') { $end = $k; break }
    }
    $lines.RemoveRange($start, $end - $start + 1)
    $done.Add('лишний метод убран')
}

# 5) надёжное чтение потока
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('private byte[] ReadBytes(int count)')) { $start = $i; break }
}
if ($start -ge 0) {
    $end = -1
    for ($k = $start; $k -lt $lines.Count; $k++) {
        if ($lines[$k].Trim() -eq '}' -and $lines[$k - 1].Trim() -eq '}') { $end = $k; break }
    }
    $new = @'
        private byte[] ReadBytes(int count)
        {
            byte[] result = new byte[count];
            int filled = 0;

            while (filled < count)
            {
                int available = _buffer.Count - _offset;

                if (available == 0)
                {
                    if (_chunk == null || _chunkOffset >= _chunkLength)
                    {
                        _chunk = new byte[16384];
                        _chunkLength = _stream.Read(_chunk, 0, _chunk.Length);
                        _chunkOffset = 0;
                        if (_chunkLength <= 0) throw new PgException("Соединение с базой данных закрыто сервером.");
                    }

                    _buffer.AddRange(Slice(_chunk, _chunkOffset, _chunkLength - _chunkOffset));
                    _chunkOffset = _chunkLength;
                    available = _buffer.Count - _offset;
                }

                int take = Math.Min(count - filled, available);
                byte[] data = _buffer.ToArray();
                Array.Copy(data, _offset, result, filled, take);
                _offset += take;
                filled += take;
            }

            return result;
        }

        private static IEnumerable<byte> Slice(byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++) yield return data[offset + i];
        }
'@ -split "`r?`n"
    $lines.RemoveRange($start, $end - $start + 1)
    $lines.InsertRange($start, [string[]]$new)
    $done.Add('чтение потока')
}

# 6) поля для буфера чтения
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq 'private int _offset;') {
        $lines.Insert($i + 1, '        private byte[] _chunk;')
        $lines.Insert($i + 2, '        private int _chunkOffset;')
        $lines.Insert($i + 3, '        private int _chunkLength;')
        $done.Add('поля буфера')
        break
    }
}

# 7) в Connect сбрасываем буфер полностью
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq '_buffer.Clear();') {
        $lines.Insert($i + 1, '            _offset = 0;')
        $lines.Insert($i + 2, '            _chunk = null;')
        $lines.Insert($i + 3, '            _chunkOffset = 0;')
        $lines.Insert($i + 4, '            _chunkLength = 0;')
        $done.Add('сброс буфера')
        break
    }
}

[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('применено: ' + ($done -join ', '))
$text = ($lines -join "`n")
Write-Host ('скобки: open=' + ([regex]::Matches($text,'\{').Count) + ' close=' + ([regex]::Matches($text,'\}').Count))
