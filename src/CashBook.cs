// ---------------------------------------------------------------------------
//  Оплата заявок, кассы и сводка для панели показателей.
//
//  Оплата бывает наличными, безналичными или смешанной. Деньги поступают
//  в кассу, у каждой кассы свой баланс: он складывается из оплат
//  по заявкам и ручных пополнений.
//
//  Хранение: файлы в папке «Кассы» либо таблицы smeta_cashdesks
//  и smeta_payments в базе данных.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Способ оплаты заявки.</summary>
    internal enum PaymentKind
    {
        Cash = 0,           // наличные
        Cashless = 1,       // безналичные
        Mixed = 2           // смешанная
    }

    internal static class PaymentKinds
    {
        public static string Title(PaymentKind kind)
        {
            switch (kind)
            {
                case PaymentKind.Cash: return "наличные";
                case PaymentKind.Cashless: return "безналичные";
                default: return "смешанная";
            }
        }

        public static string[] List
        {
            get { return new string[] { "наличные", "безналичные", "смешанная" }; }
        }

        public static PaymentKind Parse(string title)
        {
            if (string.IsNullOrEmpty(title)) return PaymentKind.Cash;
            if (title.StartsWith("безнал", StringComparison.CurrentCultureIgnoreCase)) return PaymentKind.Cashless;
            if (title.StartsWith("смеш", StringComparison.CurrentCultureIgnoreCase)) return PaymentKind.Mixed;
            return PaymentKind.Cash;
        }
    }

    /// <summary>Касса, в которой накапливаются деньги по оплаченным заявкам.</summary>
    internal sealed class CashDesk
    {
        public string Name = "";
        public string Note = "";
        public bool Archive;                // закрытая касса не показывается в списках выбора
        public DateTime Created = DateTime.Now;

        public CashDesk() { }

        public CashDesk(string name)
        {
            Name = name;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>Часть оплаты: сколько денег пришло в конкретную кассу.</summary>
    internal sealed class PaymentPart
    {
        public readonly string Desk;
        public readonly decimal Amount;
        public readonly string Kind;

        public PaymentPart(string desk, decimal amount, string kind)
        {
            Desk = desk ?? "";
            Amount = amount;
            Kind = kind ?? "";
        }
    }

    /// <summary>Оплата по заявке.</summary>

    /// <summary>Оплата по заявке.</summary>
    internal sealed class Payment
    {
        public string Number = "";          // номер заявки
        public DateTime Saved = DateTime.Now;
        public string Customer = "";
        public string Desk = "";            // касса (для смешанной — касса безналичной части)
        public string CashDesk = "";        // касса наличной части (смешанная оплата)
        public PaymentKind Kind = PaymentKind.Cash;
        public decimal Cash;                // наличными
        public decimal Cashless;            // безналичными
        public decimal Due;                 // сумма к оплате по заявке
        public string Note = "";

        public decimal Total { get { return Cash + Cashless; } }

        /// <summary>Заявка оплачена полностью.</summary>
        public bool IsFull
        {
            get { return Due > 0m && Total >= Due - 0.005m; }
        }

        /// <summary>Доплата, если заплатили меньше суммы заявки.</summary>
        public decimal Remaining
        {
            get
            {
                decimal left = Due - Total;
                return left > 0.005m ? left : 0m;
            }
        }

        /// <summary>Касса наличной части: для смешанной оплаты — отдельная.</summary>
        public string CashDeskName
        {
            get { return CashDesk.Length > 0 ? CashDesk : Desk; }
        }

        /// <summary>Касса безналичной части.</summary>
        public string CashlessDeskName
        {
            get { return Desk; }
        }

        /// <summary>Подпись касс для строки состояния.</summary>
        public string DeskTitle
        {
            get
            {
                if (Kind != PaymentKind.Mixed || Cash <= 0m || Cashless <= 0m)
                    return CashDeskName;

                // если обе части пришли в одну кассу, разбивать нечего
                if (string.Equals(CashDeskName, CashlessDeskName, StringComparison.CurrentCultureIgnoreCase))
                    return CashDeskName;

                return "наличные — " + CashDeskName + ", безналичные — " + CashlessDeskName;
            }
        }

        /// <summary>Части оплаты по кассам: касса, сумма, название.</summary>
        public List<PaymentPart> Parts
        {
            get
            {
                List<PaymentPart> parts = new List<PaymentPart>();

                if (Cash > 0m) parts.Add(new PaymentPart(CashDeskName, Cash, "наличные"));
                if (Cashless > 0m) parts.Add(new PaymentPart(CashlessDeskName, Cashless, "безналичные"));

                return parts;
            }
        }

        /// <summary>Подпись для списка.</summary>
        /// <summary>Подпись для списка.</summary>
        public string Caption
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.Append("№ ").Append(Number.Length == 0 ? "без номера" : Number);
                text.Append("   от ").Append(Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));
                if (Customer.Length > 0) text.Append("   ").Append(Customer);
                text.Append("   ").Append(PaymentKinds.Title(Kind));
                text.Append(": ").Append(Fmt.Money(Total));
                if (DeskTitle.Length > 0) text.Append("   касса: ").Append(DeskTitle);
                return text.ToString();
            }
        }
    }

    /// <summary>Пополнение или изъятие денег из кассы вручную.</summary>
    internal sealed class DeskOperation
    {
        public string Desk = "";
        public DateTime Saved = DateTime.Now;
        public decimal Amount;              // плюс — пополнение, минус — изъятие
        public string Note = "";
    }

    /// <summary>Все кассы, оплаты и ручные операции.</summary>
    internal sealed class CashBook
    {
        public List<CashDesk> Desks = new List<CashDesk>();
        public List<Payment> Payments = new List<Payment>();
        public List<DeskOperation> Operations = new List<DeskOperation>();

        /// <summary>Касса по названию или null.</summary>
        public CashDesk Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (CashDesk desk in Desks)
                if (string.Equals(desk.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    return desk;

            return null;
        }

        /// <summary>Создание кассы, если её ещё нет.</summary>
        public CashDesk Ensure(string name)
        {
            string clean = (name ?? "").Trim();
            if (clean.Length == 0) throw new ArgumentException("Название кассы не может быть пустым.");

            CashDesk existing = Find(clean);
            if (existing != null) return existing;

            CashDesk created = new CashDesk(clean);
            Desks.Add(created);
            return created;
        }

        /// <summary>Баланс кассы: оплаты по заявкам плюс ручные операции.</summary>
        public decimal Balance(string desk)
        {
            decimal total = 0m;

            foreach (Payment payment in Payments)
            {
                // смешанная оплата раскладывается по кассам: наличная часть
                // и безналичная могут лежать в разных кассах
                foreach (PaymentPart part in payment.Parts)
                {
                    if (!string.Equals(part.Desk, desk, StringComparison.CurrentCultureIgnoreCase)) continue;
                    total += part.Amount;
                }
            }

            foreach (DeskOperation operation in Operations)
            {
                if (!string.Equals(operation.Desk, desk, StringComparison.CurrentCultureIgnoreCase)) continue;
                total += operation.Amount;
            }

            return total;
        }

        /// <summary>Оплата по номеру заявки или null.</summary>
        public Payment FindPayment(string number)
        {
            if (string.IsNullOrEmpty(number)) return null;

            foreach (Payment payment in Payments)
                if (string.Equals(payment.Number, number.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    return payment;

            return null;
        }

        /// <summary>Оплата по заявке: прежняя запись заменяется.</summary>
        public void AddPayment(Payment payment)
        {
            if (payment == null) throw new ArgumentNullException("payment");

            Payment existing = FindPayment(payment.Number);
            if (existing != null) Payments.Remove(existing);

            Payments.Add(payment);
        }

        /// <summary>Удаление оплаты по номеру заявки.</summary>
        public bool RemovePayment(string number)
        {
            Payment existing = FindPayment(number);
            if (existing == null) return false;

            Payments.Remove(existing);
            return true;
        }
    }

    /// <summary>Сводка для панели показателей.</summary>
    internal sealed class DashboardInfo
    {
        public sealed class DeskRow
        {
            public string Desk = "";
            public decimal Balance;
            public int Payments;            // сколько оплат прошло через кассу
        }

        public sealed class ItemRow
        {
            public string Name = "";
            public string Article = "";
            public decimal Quantity;
            public decimal Amount;
            public int Requests;
        }

        public List<DeskRow> Desks = new List<DeskRow>();
        public List<ItemRow> Popular = new List<ItemRow>();

        public decimal DeskTotal;           // денег во всех кассах
        public int PaidToday;
        public decimal AmountToday;
        public int PaidWeek;
        public decimal AmountWeek;
        public int PaidMonth;
        public decimal AmountMonth;
        public int PaidTotal;
        public decimal AmountTotal;
        public decimal AverageCheck;        // средний чек по оплаченным заявкам

        public DateTime From;
        public DateTime To;
    }

    /// <summary>Расчёт сводки по кассам и заявкам.</summary>
    internal static class Dashboard
    {
        /// <summary>Сборка сводки.</summary>
        public static DashboardInfo Build(CashBook cash, IList<SavedEstimate> estimates, DateTime now, int popularCount)
        {
            DashboardInfo info = new DashboardInfo();
            if (cash == null) cash = new CashBook();

            info.From = new DateTime(now.Year, now.Month, 1);
            info.To = now;

            // --- деньги в кассах ---
            foreach (CashDesk desk in cash.Desks)
            {
                DashboardInfo.DeskRow row = new DashboardInfo.DeskRow();
                row.Desk = desk.Name;
                row.Balance = cash.Balance(desk.Name);

                foreach (Payment payment in cash.Payments)
                    if (string.Equals(payment.Desk, desk.Name, StringComparison.CurrentCultureIgnoreCase))
                        row.Payments++;

                info.Desks.Add(row);
                info.DeskTotal += row.Balance;
            }

            info.Desks.Sort(delegate(DashboardInfo.DeskRow left, DashboardInfo.DeskRow right)
            {
                return right.Balance.CompareTo(left.Balance);
            });

            // --- оплаты за день, неделю и месяц ---
            DateTime startOfDay = now.Date;
            DateTime startOfWeek = now.Date.AddDays(-((int)now.DayOfWeek + 6) % 7);   // с понедельника
            DateTime startOfMonth = new DateTime(now.Year, now.Month, 1);

            foreach (Payment payment in cash.Payments)
            {
                info.PaidTotal++;
                info.AmountTotal += payment.Total;

                if (payment.Saved >= startOfDay)
                {
                    info.PaidToday++;
                    info.AmountToday += payment.Total;
                }

                if (payment.Saved >= startOfWeek)
                {
                    info.PaidWeek++;
                    info.AmountWeek += payment.Total;
                }

                if (payment.Saved >= startOfMonth)
                {
                    info.PaidMonth++;
                    info.AmountMonth += payment.Total;
                }
            }

            if (info.PaidTotal > 0) info.AverageCheck = Math.Round(info.AmountTotal / info.PaidTotal, 2);

            // --- популярные позиции по сохранённым заявкам ---
            Dictionary<string, DashboardInfo.ItemRow> byName =
                new Dictionary<string, DashboardInfo.ItemRow>(StringComparer.CurrentCultureIgnoreCase);

            if (estimates != null)
            {
                foreach (SavedEstimate estimate in estimates)
                {
                    foreach (EstimateItem item in estimate.Items)
                    {
                        DashboardInfo.ItemRow row;
                        if (!byName.TryGetValue(item.Name, out row))
                        {
                            row = new DashboardInfo.ItemRow();
                            row.Name = item.Name;
                            row.Article = item.Article;
                            byName[item.Name] = row;
                        }

                        row.Quantity += item.Quantity;
                        row.Amount += item.Price * item.Quantity;
                        row.Requests++;
                    }
                }
            }

            foreach (KeyValuePair<string, DashboardInfo.ItemRow> pair in byName)
                info.Popular.Add(pair.Value);

            info.Popular.Sort(delegate(DashboardInfo.ItemRow left, DashboardInfo.ItemRow right)
            {
                int byCount = right.Requests.CompareTo(left.Requests);
                if (byCount != 0) return byCount;
                return right.Quantity.CompareTo(left.Quantity);
            });

            if (popularCount > 0 && info.Popular.Count > popularCount)
                info.Popular.RemoveRange(popularCount, info.Popular.Count - popularCount);

            return info;
        }
    }

    /// <summary>Запись и чтение касс, оплат и операций.</summary>
    internal static class CashBookJson
    {
        public const string CashFormat = "raschet-smeta-cashdesk";
        public const string PaymentFormat = "raschet-smeta-payment";
        public const string OperationFormat = "raschet-smeta-cashoperation";

        // ------------------------------------------------------------- касса

        public static string DeskToJson(CashDesk desk)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = CashFormat;
            root["name"] = desk.Name;
            root["note"] = desk.Note;
            root["archive"] = desk.Archive;
            root["created"] = desk.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return SimpleJson.Write(root);
        }

        public static CashDesk DeskFromJson(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null || SimpleJson.Text(root, "format") != CashFormat) return null;

            CashDesk desk = new CashDesk();
            desk.Name = SimpleJson.Text(root, "name");
            desk.Note = SimpleJson.Text(root, "note");
            desk.Archive = SimpleJson.Text(root, "archive") == "True";

            DateTime created;
            if (DateTime.TryParse(SimpleJson.Text(root, "created"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out created))
                desk.Created = created;

            return desk.Name.Length == 0 ? null : desk;
        }

        // ------------------------------------------------------------ оплата

        public static string PaymentToJson(Payment payment)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = PaymentFormat;
            root["number"] = payment.Number;
            root["saved"] = payment.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            root["customer"] = payment.Customer;
            root["desk"] = payment.Desk;
            root["cash_desk"] = payment.CashDesk;
            root["kind"] = PaymentKinds.Title(payment.Kind);
            root["cash"] = payment.Cash;
            root["cashless"] = payment.Cashless;
            root["due"] = payment.Due;
            root["note"] = payment.Note;
            return SimpleJson.Write(root);
        }

        public static Payment PaymentFromJson(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null || SimpleJson.Text(root, "format") != PaymentFormat) return null;

            Payment payment = new Payment();
            payment.Number = SimpleJson.Text(root, "number");
            payment.Customer = SimpleJson.Text(root, "customer");
            payment.Desk = SimpleJson.Text(root, "desk");
            payment.CashDesk = SimpleJson.Text(root, "cash_desk");
            payment.Kind = PaymentKinds.Parse(SimpleJson.Text(root, "kind"));
            payment.Cash = SimpleJson.Number(root, "cash", 0m);
            payment.Cashless = SimpleJson.Number(root, "cashless", 0m);
            payment.Due = SimpleJson.Number(root, "due", 0m);
            payment.Note = SimpleJson.Text(root, "note");

            DateTime saved;
            if (DateTime.TryParse(SimpleJson.Text(root, "saved"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out saved))
                payment.Saved = saved;

            // старая запись могла хранить только общую сумму
            decimal total = SimpleJson.Number(root, "total", 0m);
            if (total > 0m && payment.Cash == 0m && payment.Cashless == 0m)
            {
                if (payment.Kind == PaymentKind.Cashless) payment.Cashless = total;
                else payment.Cash = total;
            }

            return payment.Number.Length == 0 ? null : payment;
        }

        // ---------------------------------------------------------- операция

        public static string OperationToJson(DeskOperation operation)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = OperationFormat;
            root["desk"] = operation.Desk;
            root["saved"] = operation.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            root["amount"] = operation.Amount;
            root["note"] = operation.Note;
            return SimpleJson.Write(root);
        }

        public static DeskOperation OperationFromJson(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null || SimpleJson.Text(root, "format") != OperationFormat) return null;

            DeskOperation operation = new DeskOperation();
            operation.Desk = SimpleJson.Text(root, "desk");
            operation.Amount = SimpleJson.Number(root, "amount", 0m);
            operation.Note = SimpleJson.Text(root, "note");

            DateTime saved;
            if (DateTime.TryParse(SimpleJson.Text(root, "saved"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out saved))
                operation.Saved = saved;

            return operation;
        }
    }

    /// <summary>Кассы и оплаты в файлах.</summary>
    internal sealed class FileCashBook : ICashBookStore
    {
        public string Title { get { return "Кассы в файлах: " + Folder; } }

        public static string Folder
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, "Кассы"); }
        }

        private static string DesksFile { get { return System.IO.Path.Combine(Folder, "кассы.json"); } }

        public CashBook Load()
        {
            CashBook book = new CashBook();
            if (!System.IO.Directory.Exists(Folder)) return book;

            // кассы
            if (File.Exists(DesksFile))
            {
                try
                {
                    IDictionary<string, object> root =
                        SimpleJson.Parse(File.ReadAllText(DesksFile, Encoding.UTF8)) as IDictionary<string, object>;

                    if (root != null)
                    {
                        foreach (object entry in SimpleJson.Array(root, "desks"))
                        {
                            IDictionary<string, object> map = entry as IDictionary<string, object>;
                            if (map == null) continue;

                            CashDesk desk = new CashDesk();
                            desk.Name = SimpleJson.Text(map, "name");
                            desk.Note = SimpleJson.Text(map, "note");
                            desk.Archive = SimpleJson.Text(map, "archive") == "True";
                            if (desk.Name.Length == 0) continue;
                            book.Desks.Add(desk);
                        }
                    }
                }
                catch { /* повреждённый файл — начнём со списка касс по оплатам */ }
            }

            // оплаты
            foreach (string file in Directory.GetFiles(Folder, "оплата-*.json"))
            {
                try
                {
                    Payment payment = CashBookJson.PaymentFromJson(File.ReadAllText(file, Encoding.UTF8));
                    if (payment != null) book.Payments.Add(payment);
                }
                catch { }
            }

            // ручные операции
            foreach (string file in Directory.GetFiles(Folder, "операция-*.json"))
            {
                try
                {
                    DeskOperation operation = CashBookJson.OperationFromJson(File.ReadAllText(file, Encoding.UTF8));
                    if (operation != null) book.Operations.Add(operation);
                }
                catch { }
            }

            // кассы, которых нет в списке, но есть в оплатах
            foreach (Payment payment in book.Payments)
                if (payment.Desk.Length > 0 && book.Find(payment.Desk) == null)
                    book.Ensure(payment.Desk);

            return book;
        }

        public void Save(CashBook book)
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);

            // список касс
            List<object> desks = new List<object>();
            foreach (CashDesk desk in book.Desks)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["name"] = desk.Name;
                map["note"] = desk.Note;
                map["archive"] = desk.Archive;
                desks.Add(map);
            }

            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = "raschet-smeta-cashbook";
            root["desks"] = desks;
            File.WriteAllText(DesksFile, SimpleJson.Write(root), new UTF8Encoding(true));

            // оплаты: по файлу на заявку
            foreach (Payment payment in book.Payments)
            {
                string name = SafeName(payment.Number);
                File.WriteAllText(System.IO.Path.Combine(Folder, "оплата-" + name + ".json"),
                    CashBookJson.PaymentToJson(payment), new UTF8Encoding(true));
            }

            // операции
            for (int i = 0; i < book.Operations.Count; i++)
            {
                DeskOperation operation = book.Operations[i];
                string name = SafeName(operation.Desk) + "-" +
                              operation.Saved.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + i;
                File.WriteAllText(System.IO.Path.Combine(Folder, "операция-" + name + ".json"),
                    CashBookJson.OperationToJson(operation), new UTF8Encoding(true));
            }
        }

        public bool Delete(Payment payment)
        {
            try
            {
                string file = System.IO.Path.Combine(Folder, "оплата-" + SafeName(payment.Number) + ".json");
                if (!File.Exists(file)) return false;

                File.Delete(file);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Имя файла без недопустимых символов.</summary>
        public static string SafeName(string text)
        {
            StringBuilder clean = new StringBuilder();
            foreach (char symbol in text ?? "")
            {
                if (char.IsLetterOrDigit(symbol) || symbol == '-' || symbol == '_') clean.Append(symbol);
                else clean.Append('_');
            }

            return clean.Length == 0 ? "без-номера" : clean.ToString();
        }
    }

    /// <summary>Хранилище касс и оплат: файлы или база данных.</summary>
    internal interface ICashBookStore
    {
        string Title { get; }
        CashBook Load();
        void Save(CashBook book);
        bool Delete(Payment payment);
    }
}
