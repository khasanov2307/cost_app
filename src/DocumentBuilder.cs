// ---------------------------------------------------------------------------
//  Построение документа сметы: заголовок, реквизиты, таблица позиций,
//  скидка и итог, строки для подписей.
//
//  Результат — структура (таблицы и абзацы). Из неё собираются и документ
//  Word, и предпросмотр в программе, поэтому оба вида всегда совпадают.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KotovCalc
{
    /// <summary>Строка таблицы документа.</summary>
    internal sealed class DocRow
    {
        public string Style = DocStyle.Normal;     // Normal | TableHeader | GroupRow | TotalRow
        public string[] Cells = new string[0];

        /// <summary>Объединение ячеек, начиная с первой (для итоговой строки).</summary>
        public int Merge = 0;
    }

    /// <summary>Стили строк документа.</summary>
    internal static class DocStyle
    {
        public const string Normal = "Normal";
        public const string Header = "TableHeader";
        public const string Group = "GroupRow";
        public const string Total = "TotalRow";
    }

    /// <summary>Готовый документ сметы.</summary>
    internal sealed class EstimateDocument
    {
        public string Title = "СМЕТА";
        public string Subtitle = "";

        /// <summary>Таблица позиций.</summary>
        public List<DocRow> Rows = new List<DocRow>();

        /// <summary>Строки после таблицы: подытог, скидка, итог.</summary>
        public List<string> FootLines = new List<string>();

        /// <summary>Подписи сторон.</summary>
        public string LeftSign = "";
        public string RightSign = "";

        public int PositionCount;
        public decimal Subtotal;
        public decimal DiscountPercent;
        public decimal DiscountAmount;
        public decimal Total;

        /// <summary>Ширины колонок в двадцатых долях пункта.</summary>
        public static readonly int[] Grid = new int[] { 500, 1100, 4400, 1000, 800, 1200, 1300 };

        public string[] HeaderCells = new string[]
            { "#", "Артикул", "Наименование", "Ед. изм.", "Кол-во", "Цена", "Всего" };
    }

    internal static class DocumentBuilder
    {
        /// <summary>Сборка документа сметы из отмеченных позиций.</summary>
        public static EstimateDocument Build(IList<EstimateRow> rows, DocumentFields fields,
                                             string executor, DateTime stamp)
        {
            if (fields == null) fields = new DocumentFields();
            fields.Normalize();

            EstimateDocument document = new EstimateDocument();

            // ---- шапка документа ----
            StringBuilder subtitle = new StringBuilder();
            subtitle.Append("от ").Append(stamp.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));

            if (fields.Number.Length > 0)
                subtitle.Append("   •   № ").Append(fields.Number);

            if (fields.Customer.Length > 0)
                subtitle.Append("   •   Заказчик: ").Append(fields.Customer);

            document.Subtitle = subtitle.ToString();

            // ---- таблица позиций ----
            document.Rows.Add(new DocRow { Style = DocStyle.Header, Cells = document.HeaderCells });

            string group = null;
            int number = 0;
            decimal subtotal = 0m;

            foreach (EstimateRow row in rows)
            {
                if (!row.Selected || row.Quantity <= 0m) continue;

                if (!string.Equals(group, row.Item.Group, StringComparison.CurrentCultureIgnoreCase))
                {
                    group = row.Item.Group;
                    document.Rows.Add(new DocRow
                    {
                        Style = DocStyle.Group,
                        Cells = new string[] { "", "", group, "", "", "", "" }
                    });
                }

                number++;
                subtotal += row.Sum;

                document.Rows.Add(new DocRow
                {
                    Style = DocStyle.Normal,
                    Cells = new string[]
                    {
                        number.ToString(CultureInfo.InvariantCulture),
                        row.Item.Article ?? "",
                        row.Item.Name ?? "",
                        row.Item.Unit ?? "",
                        Fmt.Qty(row.Quantity),
                        Fmt.MoneyPlain(row.Price) + (row.PriceChanged ? " *" : ""),
                        Fmt.MoneyPlain(row.Sum)
                    }
                });
            }

            document.PositionCount = number;
            document.Subtotal = subtotal;

            // ---- скидка и итог ----
            decimal discountAmount = 0m;
            if (fields.Discount > 0m && subtotal > 0m)
                discountAmount = Math.Round(subtotal * fields.Discount / 100m, 2, MidpointRounding.AwayFromZero);

            document.DiscountPercent = fields.Discount;
            document.DiscountAmount = discountAmount;
            document.Total = subtotal - discountAmount;

            if (discountAmount > 0m)
                document.FootLines.Add("Подытог: " + Fmt.Money(subtotal));

            if (discountAmount > 0m)
                document.FootLines.Add("Скидка " + Fmt.Qty(fields.Discount) + " %: \u2212" + Fmt.Money(discountAmount));

            document.FootLines.Add("ИТОГО К ОПЛАТЕ: " + Fmt.Money(document.Total));

            // ---- подписи ----
            document.LeftSign = "Исполнитель: " +
                (string.IsNullOrEmpty(executor) ? "____________________" : executor);
            document.RightSign = "Заказчик: " +
                (fields.Customer.Length == 0 ? "____________________" : fields.Customer);

            return document;
        }

        /// <summary>Текстовый вид документа — для предпросмотра и текстового файла.</summary>
        public static string ToText(EstimateDocument document)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(document.Title);
            if (document.Subtitle.Length > 0) sb.AppendLine(document.Subtitle);
            sb.AppendLine(new string('-', 86));

            // ширины колонок для ровных столбцов в тексте
            int[] width = new int[] { 4, 12, 38, 11, 8, 12, 13 };

            foreach (DocRow row in document.Rows)
            {
                if (row.Style == DocStyle.Group)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + row.Cells[2].ToUpper(Fmt.Ru) + "]");
                    continue;
                }

                if (row.Style == DocStyle.Header)
                {
                    sb.AppendLine(PadRow(row.Cells, width, false));
                    sb.AppendLine(new string('-', 86));
                    continue;
                }

                sb.AppendLine(PadRow(row.Cells, width, IsNumeric(row.Cells)));
            }

            sb.AppendLine(new string('-', 86));

            foreach (string line in document.FootLines)
                sb.AppendLine(line);

            if (document.DiscountAmount > 0m)
                sb.AppendLine("* цена изменена в смете");

            sb.AppendLine();
            sb.AppendLine(document.LeftSign + "        " + document.RightSign);
            return sb.ToString();
        }

        private static bool IsNumeric(string[] cells)
        {
            return cells.Length > 4 && cells[4].Length > 0 && cells[6].Length > 0;
        }

        private static string PadRow(string[] cells, int[] width, bool rightAlign)
        {
            StringBuilder line = new StringBuilder(" ");

            for (int i = 0; i < cells.Length && i < width.Length; i++)
            {
                string text = Shorten(cells[i], width[i]);
                bool right = rightAlign && i >= 4;

                if (right) line.Append(text.PadLeft(width[i]));
                else line.Append(text.PadRight(width[i]));

                line.Append(' ');
            }

            return line.ToString().TrimEnd();
        }

        private static string Shorten(string text, int width)
        {
            if (text == null) return "";
            if (text.Length <= width) return text;
            return text.Substring(0, Math.Max(1, width - 1)) + "\u2026";
        }
    }
}