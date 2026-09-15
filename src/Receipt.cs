// ---------------------------------------------------------------------------
//  Чек об оплате заявки: текст для печати и сохранение в файл.
//
//  В чеке: номер заявки, дата, заказчик, касса, суммы по способам оплаты,
//  кто принял оплату и остаток к доплате, если заплатили не всё.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Чек об оплате: данные и текст.</summary>
    internal sealed class Receipt
    {
        public string Number = "";
        public DateTime Saved = DateTime.Now;
        public string Customer = "";
        public string Cashier = "";             // кто принял оплату
        public Payment Payment;

        /// <summary>Текст чека: ширина подбирается под чековую ленту 42 знака.</summary>
        public string ToText(int width)
        {
            if (width < 28) width = 28;
            if (width > 80) width = 80;

            StringBuilder text = new StringBuilder();

            Line(text, "ОПЛАТА ПО ЗАЯВКЕ", width);
            Line(text, Repeat('=', width), width);
            Line(text, "Заявка № " + (Number.Length == 0 ? "без номера" : Number), width);
            Line(text, "Дата: " + Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru), width);
            if (Customer.Length > 0) Line(text, "Заказчик: " + Customer, width);

            text.AppendLine();

            if (Payment != null)
            {
                Line(text, "Способ оплаты: " + PaymentKinds.Title(Payment.Kind), width);
                text.AppendLine();

                foreach (PaymentPart part in Payment.Parts)
                {
                    Pair(text, part.Kind, Fmt.Money(part.Amount), width);

                    if (part.Desk.Length > 0)
                        Line(text, "   касса: " + part.Desk, width);
                }

                text.AppendLine();
                Line(text, Repeat('-', width), width);
                Pair(text, "ИТОГО ОПЛАЧЕНО", Fmt.Money(Payment.Total), width);
                Line(text, Repeat('-', width), width);

                Pair(text, "Сумма заявки", Fmt.Money(Payment.Due), width);

                if (Payment.Remaining > 0m)
                    Pair(text, "ОСТАЛОСЬ ДОПЛАТИТЬ", Fmt.Money(Payment.Remaining), width);
                else if (Payment.Due > 0m)
                    Line(text, "Заявка оплачена полностью.", width);

                if (Payment.Note.Length > 0)
                {
                    text.AppendLine();
                    Line(text, "Примечание: " + Payment.Note, width);
                }
            }
            else
            {
                Line(text, "Оплата не зафиксирована.", width);
            }

            text.AppendLine();
            if (Cashier.Length > 0) Line(text, "Оплату принял: " + Cashier, width);
            Line(text, "Спасибо за обращение!", width);

            return text.ToString();
        }

        /// <summary>Имя файла чека.</summary>
        public string FileName
        {
            get
            {
                string safe = FileCashBook.SafeName(Number.Length == 0 ? "без-номера" : Number);
                return "Чек_" + safe + "_" + Saved.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture) + ".txt";
            }
        }

        /// <summary>Сохранение чека текстовым файлом.</summary>
        public string SaveText(string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, FileName);
            File.WriteAllText(path, ToText(42), new UTF8Encoding(true));

            return path;
        }

        private static void Line(StringBuilder text, string value, int width)
        {
            text.AppendLine(Wrap(value, width));
        }

        private static void Pair(StringBuilder text, string left, string right, int width)
        {
            string clean = left ?? "";
            string value = right ?? "";

            if (clean.Length + value.Length + 2 > width)
            {
                text.AppendLine(Wrap(clean, width));
                text.AppendLine(Wrap(new string(' ', Math.Max(0, width - value.Length)) + value, width));
                return;
            }

            text.AppendLine(clean + new string(' ', width - clean.Length - value.Length) + value);
        }

        /// <summary>Переносит длинную строку по словам.</summary>
        private static string Wrap(string value, int width)
        {
            if (value == null) return "";

            List<string> lines = new List<string>();
            StringBuilder current = new StringBuilder();

            foreach (string word in value.Split(' '))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > width)
                {
                    lines.Add(current.ToString());
                    current.Length = 0;
                }

                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }

            if (current.Length > 0) lines.Add(current.ToString());

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string Repeat(char symbol, int count)
        {
            return new string(symbol, count);
        }
    }
}
