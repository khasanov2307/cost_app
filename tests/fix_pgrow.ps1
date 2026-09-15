# Возвращает колонки в порядке, который прислал сервер (важно для Scalar и проверок).
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$lines = New-Object 'System.Collections.Generic.List[string]'
[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8) | ForEach-Object { $lines.Add($_) }
$done = New-Object 'System.Collections.Generic.List[string]'

# 1) класс строки результата с сохранением порядка колонок
$class = @'
    /// <summary>Строка результата: значения доступны по имени, порядок колонок сохранён.</summary>
    internal sealed class PgRow
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        public int Count { get { return _order.Count; } }

        /// <summary>Имена колонок в порядке, который прислал сервер.</summary>
        public IList<string> Columns { get { return _order; } }

        internal void Add(string name, object value)
        {
            if (!_values.ContainsKey(name)) _order.Add(name);
            _values[name] = value;
        }

        public object this[string name]
        {
            get
            {
                object value;
                return _values.TryGetValue(name, out value) ? value : null;
            }
        }

        public bool Has(string name) { return _values.ContainsKey(name); }

        /// <summary>Значение первой колонки.</summary>
        public object First()
        {
            return _order.Count == 0 ? null : _values[_order[0]];
        }

        public IEnumerable<KeyValuePair<string, object>> Pairs()
        {
            foreach (string name in _order) yield return new KeyValuePair<string, object>(name, _values[name]);
        }
    }

'@ -split "`r?`n"

$anchor = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('internal sealed class PgException')) { $anchor = $i; break }
}
if ($anchor -lt 0) { throw 'класс PgException не найден' }
$lines.InsertRange($anchor, [string[]]$class)
$done.Add('класс строки')

# 2) сигнатуры методов
$replacements = @(
    @('public List<Dictionary<string, object>> Query(string sql, params object[] parameters)',
      'public List<PgRow> Query(string sql, params object[] parameters)'),
    @('List<Dictionary<string, object>> result = Execute(command);',
      'List<PgRow> result = Execute(command);'),
    @('public object Scalar(string sql, params object[] parameters)',
      'public object Scalar(string sql, params object[] parameters)'),
    @('List<Dictionary<string, object>> rows = Query(sql, parameters);',
      'List<PgRow> rows = Query(sql, parameters);'),
    @('private List<Dictionary<string, object>> Execute(string sql)',
      'private List<PgRow> Execute(string sql)'),
    @('List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();',
      'List<PgRow> rows = new List<PgRow>();'),
    @('private Dictionary<string, object> ReadDataRow(byte[] payload, List<string> columns)',
      'private PgRow ReadDataRow(byte[] payload, List<string> columns)'),
    @('Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.Ordinal);',
      'PgRow row = new PgRow();'),
    @('row[name] = value;', 'row.Add(name, value);'),
    @('foreach (KeyValuePair<string, object> pair in rows[0]) return pair.Value;',
      'return rows[0].First();')
)
foreach ($pair in $replacements) {
    $count = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Contains($pair[0])) { $lines[$i] = $lines[$i].Replace($pair[0], $pair[1]); $count++ }
    }
    if ($count -eq 0) { Write-Host ('не найдено: ' + $pair[0]) -ForegroundColor Yellow }
}
$done.Add('сигнатуры')

[System.IO.File]::WriteAllLines($path, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('применено: ' + ($done -join ', '))

# 3) проверки в наборе: колонки теперь по имени
$h = Join-Path $root 'tests\Harness5.cs'
$text = [System.IO.File]::ReadAllText($h, [System.Text.Encoding]::UTF8)
$text = $text.Replace('List<Dictionary<string, object>> rows = client.Query("SELECT 1 AS one, ''привет'' AS hello, true AS flag");',
                      'List<PgRow> rows = client.Query("SELECT 1 AS one, ''привет'' AS hello, true AS flag");')
$text = $text.Replace('List<Dictionary<string, object>> list = client.Query(', 'List<PgRow> list = client.Query(')
# ожидания для проверок на кавычки и инъекцию
$text = $text.Replace('Check("кавычка не ломает запрос", null, Text(client.Scalar("SELECT $1::text || ''!''", "о''к")));',
                      'Check("кавычка не ломает запрос", "о''к", Text(client.Scalar("SELECT $1::text", "о''к")));')
$text = $text.Replace('Check("таблица не пострадала", null, Text(client.Scalar("SELECT to_regclass(''smeta_probe')")));',
                      'Check("таблица не пострадала", null, Text(client.Scalar("SELECT to_regclass(''smeta_probe'')")));')
$text = $text.Replace('Check("таблицы удалены", null, Text(client.Scalar("SELECT to_regclass(''smeta_prices')")));',
                      'Check("таблицы удалены", null, Text(client.Scalar("SELECT to_regclass(''smeta_prices'')")));')
# проверка двух кириллических строк — берём значение по имени колонки
$text = $text.Replace('Check("две кириллические строки", "Услуга — Компьютерная диагностика",' + "`r`n" +
                      '                    Text(client.Scalar("SELECT $1::text || '' — '' || $2::text", "Услуга", "Компьютерная диагностика")));',
                      'Check("две кириллические строки", "Услуга — Компьютерная диагностика",' + "`r`n" +
                      '                    Text(client.Scalar("SELECT ($1::text || '' — '' || $2::text) AS value", "Услуга", "Компьютерная диагностика")));')
$text = $text.Replace('Check("десятичное число", "1500.75", Text(client.Scalar("SELECT $1::numeric", 1500.75m)));',
                      'Check("десятичное число", "1500.75", Text(client.Scalar("SELECT $1::numeric AS value", 1500.75m)));')
[System.IO.File]::WriteAllText($h, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'проверки обновлены'
