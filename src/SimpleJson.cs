// ---------------------------------------------------------------------------
//  Небольшой разбор и запись JSON.
//
//  Нужен для файлов обмена и настроек: .NET Framework 4.8 не имеет
//  встроенного сериализатора JSON, а подключать сторонние библиотеки
//  не хочется — программа остаётся самодостаточной.
//
//  Числа разбираются в decimal (важно для цен), строки — в string,
//  объекты — в SortedDictionary, массивы — в List.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KotovCalc
{
    internal static class SimpleJson
    {
        // ---------------------------------------------------------- запись

        /// <summary>Запись значения: числа, строки, логические, словари и списки.</summary>
        public static string Write(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("null"); return; }

            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }

            if (value is decimal)
            {
                sb.Append(((decimal)value).ToString("0.####", CultureInfo.InvariantCulture));
                return;
            }
            if (value is double || value is float)
            {
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("0.####", CultureInfo.InvariantCulture));
                return;
            }
            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture));
                return;
            }

            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null) { WriteObject(sb, map, depth); return; }

            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list != null) { WriteArray(sb, list, depth); return; }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map, int depth)
        {
            sb.Append('{');
            bool first = true;

            foreach (KeyValuePair<string, object> pair in map)
            {
                if (!first) sb.Append(',');
                first = false;

                sb.Append('\n');
                Indent(sb, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(": ");
                WriteValue(sb, pair.Value, depth + 1);
            }

            if (!first) { sb.Append('\n'); Indent(sb, depth); }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, System.Collections.IEnumerable list, int depth)
        {
            List<object> values = new List<object>();
            foreach (object item in list) values.Add(item);

            sb.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n');
                Indent(sb, depth + 1);
                WriteValue(sb, values[i], depth + 1);
            }

            if (values.Count > 0) { sb.Append('\n'); Indent(sb, depth); }
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------- разбор

        /// <summary>Разбор JSON. При ошибке возвращает null.</summary>
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                int position = 0;
                object value = ParseValue(text, ref position);
                return value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Строка из словаря или пустая строка.</summary>
        public static string Text(IDictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key) || map[key] == null) return "";
            return Convert.ToString(map[key], CultureInfo.InvariantCulture);
        }

        /// <summary>Число из словаря или значение по умолчанию.</summary>
        public static decimal Number(IDictionary<string, object> map, string key, decimal fallback)
        {
            if (map == null || !map.ContainsKey(key) || map[key] == null) return fallback;

            object value = map[key];
            if (value is decimal) return (decimal)value;
            if (value is int) return (int)value;
            if (value is long) return (long)value;

            decimal parsed;
            if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return fallback;
        }

        /// <summary>Список объектов из словаря.</summary>
        public static List<object> Array(IDictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key)) return new List<object>();
            List<object> list = map[key] as List<object>;
            return list ?? new List<object>();
        }

        /// <summary>Словарь по ключу.</summary>
        public static IDictionary<string, object> Object(IDictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key)) return null;
            return map[key] as IDictionary<string, object>;
        }

        private static object ParseValue(string text, ref int position)
        {
            SkipSpace(text, ref position);
            if (position >= text.Length) throw new FormatException("Неожиданный конец JSON");

            char c = text[position];
            if (c == '{') return ParseObject(text, ref position);
            if (c == '[') return ParseArray(text, ref position);
            if (c == '"') return ParseString(text, ref position);
            if (text.Substring(position).StartsWith("true")) { position += 4; return true; }
            if (text.Substring(position).StartsWith("false")) { position += 5; return false; }
            if (text.Substring(position).StartsWith("null")) { position += 4; return null; }

            return ParseNumber(text, ref position);
        }

        private static IDictionary<string, object> ParseObject(string text, ref int position)
        {
            SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
            position++;                                     // {

            while (true)
            {
                SkipSpace(text, ref position);
                if (position >= text.Length) throw new FormatException("Объект не закрыт");
                if (text[position] == '}') { position++; break; }

                string key = ParseString(text, ref position);
                SkipSpace(text, ref position);
                if (position >= text.Length || text[position] != ':') throw new FormatException("Ожидалось двоеточие");
                position++;

                map[key] = ParseValue(text, ref position);

                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == ',') { position++; continue; }
                if (position < text.Length && text[position] == '}') { position++; break; }
                throw new FormatException("Ожидалась запятая или закрывающая скобка");
            }

            return map;
        }

        private static List<object> ParseArray(string text, ref int position)
        {
            List<object> list = new List<object>();
            position++;                                     // [

            while (true)
            {
                SkipSpace(text, ref position);
                if (position >= text.Length) throw new FormatException("Массив не закрыт");
                if (text[position] == ']') { position++; break; }

                list.Add(ParseValue(text, ref position));

                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == ',') { position++; continue; }
                if (position < text.Length && text[position] == ']') { position++; break; }
                throw new FormatException("Ожидалась запятая или закрывающая скобка");
            }

            return list;
        }

        private static string ParseString(string text, ref int position)
        {
            SkipSpace(text, ref position);
            if (position >= text.Length || text[position] != '"') throw new FormatException("Ожидалась строка");
            position++;

            StringBuilder sb = new StringBuilder();
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"') break;

                if (c != '\\') { sb.Append(c); continue; }

                if (position >= text.Length) break;
                char escaped = text[position++];
                switch (escaped)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (position + 4 <= text.Length)
                        {
                            int code = int.Parse(text.Substring(position, 4), NumberStyles.HexNumber,
                                                 CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            position += 4;
                        }
                        break;
                    default: sb.Append(escaped); break;
                }
            }

            return sb.ToString();
        }

        private static decimal ParseNumber(string text, ref int position)
        {
            int start = position;
            while (position < text.Length)
            {
                char c = text[position];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    position++;
                    continue;
                }
                break;
            }

            string number = text.Substring(start, position - start);
            decimal value;
            if (!decimal.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Неверное число: " + number);

            return value;
        }

        private static void SkipSpace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
    }
}