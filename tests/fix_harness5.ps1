# Правки проверок клиента PostgreSQL: построчно, без длинных строк с кавычками.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'tests\Harness5.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }

$changed = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]

    # кавычка внутри значения не должна ломать запрос: ожидаем корректный результат
    if ($line.Contains('кавычка не ломает запрос')) {
        $lines[$i] = '                Check("кавычка не ломает запрос", "о''к", Text(client.Scalar("SELECT $1::text AS value", "о''к")));'
        $changed++
        continue
    }

    # десятичное число — берём значение по имени колонки
    if ($line.Contains('десятичное число')) {
        $lines[$i] = '                Check("десятичное число", "1500.75", Text(client.Scalar("SELECT $1::numeric AS value", 1500.75m)));'
        $changed++
        continue
    }

    # две кириллические строки — тоже по имени колонки
    if ($line.Contains('две кириллические строки')) {
        $lines[$i] = '                Check("две кириллические строки", "Услуга — Компьютерная диагностика", Text(client.Scalar("SELECT ($1::text || '' — '' || $2::text) AS value", "Услуга", "Компьютерная диагностика")));'
        $changed++
        continue
    }

    # to_regclass возвращает regclass: приводим к тексту
    if ($line.Contains('SELECT to_regclass(''smeta_probe'')')) {
        $lines[$i] = '                Check("таблица не пострадала", null, Text(client.Scalar("SELECT to_regclass(''smeta_probe'')::text AS value")));'
        $changed++
        continue
    }
    if ($line.Contains('SELECT to_regclass(''smeta_prices'')')) {
        $lines[$i] = '                Check("таблицы удалены", null, Text(client.Scalar("SELECT to_regclass(''smeta_prices'')::text AS value")));'
        $changed++
        continue
    }

    # типы данных
    if ($line.Contains('Dictionary<string, object>')) {
        $lines[$i] = $line.Replace('Dictionary<string, object>', 'PgRow')
        $changed++
    }
}

# перед созданием таблиц наводим порядок в базе
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('client.Execute("DROP TABLE IF EXISTS smeta_prices");')) {
        $block = @(
'                client.Execute("DROP TABLE IF EXISTS smeta_prices");',
'                client.Execute("DROP TABLE IF EXISTS smeta_settings");',
'                client.Execute("DROP TABLE IF EXISTS smeta_templates");'
        )
        $lines[$i] = $block[0]
        $lines.Insert($i + 1, $block[1])
        $lines.Insert($i + 2, $block[2])
        $changed++
        break
    }
}

[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("изменено строк: " + $changed)

# чистим базу от прошлых запусков
$bin = 'C:\Program Files\PostgreSQL\18\bin'
$env:PGPASSWORD = 'smeta'
& (Join-Path $bin 'psql.exe') -U smeta -h 127.0.0.1 -p 5432 -d smeta -q -c "DROP TABLE IF EXISTS smeta_prices, smeta_settings, smeta_templates;" 2>&1 |
    ForEach-Object { "  " + $_ }
Write-Host 'база очищена'
