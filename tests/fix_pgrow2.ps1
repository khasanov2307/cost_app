# Заменяет тип строки результата в клиенте PostgreSQL на PgRow.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\PgClient.cs'
$text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)

# сам класс строки результата, если его ещё нет
if (-not $text.Contains('internal sealed class PgRow')) {
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
    for ($i = 0; $i -lt $script:linesCount; $i++) { }
    $marker = '    /// <summary>Ошибка при работе с базой данных.</summary>'
    $at = $text.IndexOf($marker)
    if ($at -lt 0) { throw 'место для класса PgRow не найдено' }
    $text = $text.Substring(0, $at) + ($class -join "`r`n") + $text.Substring($at)
    Write-Host 'класс PgRow добавлен'
}

# замена типов
$pairs = @(
    @('List<Dictionary<string, object>>', 'List<PgRow>'),
    @('Dictionary<string, object> ReadDataRow', 'PgRow ReadDataRow'),
    @('Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.Ordinal);', 'PgRow row = new PgRow();'),
    @('row[name] = value;', 'row.Add(name, value);'),
    @('foreach (KeyValuePair<string, object> pair in rows[0]) return pair.Value;', 'return rows[0].First();')
)
foreach ($pair in $pairs) {
    $before = $text
    $text = $text.Replace($pair[0], $pair[1])
    if ($text -ne $before) { Write-Host ('заменено: ' + $pair[0].Substring(0, [Math]::Min(46, $pair[0].Length))) }
}

[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'готово'
