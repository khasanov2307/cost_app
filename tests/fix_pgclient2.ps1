# Точечные правки клиента PostgreSQL.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }
$done = New-Object 'System.Collections.Generic.List[string]'

# 1) поле для тела сообщения авторизации
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq 'private int _passwordRequest;') {
        $lines.Insert($i, '        private byte[] _authPayload;        // тело сообщения с солью или списком механизмов')
        $done.Add('поле')
        break
    }
}

# 2) сохраняем тело в ReadStartupResponse
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('if (code == 0) continue;')) {
        $lines.Insert($i + 1, '                    _authPayload = payload;')
        $done.Add('сохранение тела')
        break
    }
}

# 3) md5 и scram берут тело из поля
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

# 4) убираем ставший лишним метод
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('private byte[] ReadAuthPayload()')) { $start = $i; break }
}
if ($start -ge 0) {
    $end = -1
    for ($k = $start; $k -lt $start + 10; $k++) {
        if ($lines[$k].Trim() -eq '}') { $end = $k; break }
    }
    if ($end -gt $start) {
        $lines.RemoveRange($start, $end - $start + 1)
        $done.Add('лишний метод убран')
    }
}

[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('применено: ' + ($done -join ', '))
