// ---------------------------------------------------------------------------
//  Склад: остатки запчастей и расходников, приход и расход.
//
//  Остаток позиции каталога складывается из движений: приход увеличивает,
//  расход (списание по заявке) уменьшает. Себестоимость берётся из карточки
//  позиции каталога, поэтому маржа считается по каждой заявке.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Движение по складу.</summary>
    internal sealed class StockMove
    {
        public string Article = "";         // артикул позиции каталога
        public string Name = "";            // наименование на момент движения
        public decimal Quantity;            // плюс — приход, минус — расход
        public decimal Cost;                // цена за единицу
        public DateTime Saved = DateTime.Now;
        public string Number = "";          // номер заявки при списании
        public string Note = "";

        public decimal Amount { get { return Quantity * Cost; } }

        public bool IsIncome { get { return Quantity > 0m; } }

        public string Caption
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.Append(IsIncome ? "приход " : "расход ");
                text.Append(Fmt.Qty(Math.Abs(Quantity)));
                text.Append("   ").Append(Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));
                if (Cost > 0m) text.Append("   по ").Append(Fmt.Money(Cost));
                if (Number.Length > 0) text.Append("   заявка № ").Append(Number);
                if (Note.Length > 0) text.Append("   ").Append(Note);
                return text.ToString();
            }
        }
    }

    /// <summary>Склад: движения и остатки.</summary>
    internal sealed class Warehouse
    {
        public List<StockMove> Moves = new List<StockMove>();

        /// <summary>Остаток по артикулу и наименованию.</summary>
        public decimal Stock(string article, string name)
        {
            decimal total = 0m;

            foreach (StockMove move in Moves)
                if (Same(move, article, name)) total += move.Quantity;

            return total;
        }

        /// <summary>Средняя себестоимость по приходам.</summary>
        public decimal AverageCost(string article, string name)
        {
            decimal quantity = 0m;
            decimal amount = 0m;

            foreach (StockMove move in Moves)
            {
                if (!move.IsIncome || !Same(move, article, name)) continue;
                quantity += move.Quantity;
                amount += move.Amount;
            }

            if (quantity <= 0m) return 0m;
            return Math.Round(amount / quantity, 2);
        }

        /// <summary>Последняя закупочная цена.</summary>
        public decimal LastCost(string article, string name)
        {
            decimal cost = 0m;
            DateTime last = DateTime.MinValue;

            foreach (StockMove move in Moves)
            {
                if (!move.IsIncome || !Same(move, article, name)) continue;
                if (move.Saved < last) continue;

                last = move.Saved;
                cost = move.Cost;
            }

            return cost;
        }

        /// <summary>Движения по позиции, новые сверху.</summary>
        public List<StockMove> MovesFor(string article, string name)
        {
            List<StockMove> found = new List<StockMove>();

            foreach (StockMove move in Moves)
                if (Same(move, article, name)) found.Add(move);

            found.Sort(delegate(StockMove left, StockMove right)
            {
                return right.Saved.CompareTo(left.Saved);
            });

            return found;
        }

        /// <summary>Движения по заявке.</summary>
        public List<StockMove> MovesForEstimate(string number)
        {
            List<StockMove> found = new List<StockMove>();

            foreach (StockMove move in Moves)
                if (string.Equals(move.Number, number, StringComparison.CurrentCultureIgnoreCase))
                    found.Add(move);

            return found;
        }

        /// <summary>Списано ли уже что-то по заявке.</summary>
        public bool HasWriting(string number)
        {
            return MovesForEstimate(number).Count > 0;
        }

        /// <summary>Приход на склад.</summary>
        public StockMove Income(string article, string name, decimal quantity, decimal cost, string note)
        {
            if (quantity <= 0m) throw new ArgumentException("Количество прихода должно быть больше нуля.");

            StockMove move = new StockMove();
            move.Article = article ?? "";
            move.Name = name ?? "";
            move.Quantity = quantity;
            move.Cost = cost;
            move.Saved = DateTime.Now;
            move.Note = note ?? "";

            Moves.Add(move);
            return move;
        }

        /// <summary>Изъятие или инвентаризационная правка (количество со знаком минус).</summary>
        public StockMove Expense(string article, string name, decimal quantity, decimal cost, string note)
        {
            if (quantity <= 0m) throw new ArgumentException("Количество расхода должно быть больше нуля.");

            StockMove move = new StockMove();
            move.Article = article ?? "";
            move.Name = name ?? "";
            move.Quantity = -quantity;
            move.Cost = cost;
            move.Saved = DateTime.Now;
            move.Note = note ?? "";

            Moves.Add(move);
            return move;
        }

        /// <summary>Внесение остатка: разница между фактическим и учётным количеством.</summary>
        public StockMove Adjust(string article, string name, decimal actual, decimal cost, string note)
        {
            decimal difference = actual - Stock(article, name);

            StockMove move = new StockMove();
            move.Article = article ?? "";
            move.Name = name ?? "";
            move.Quantity = difference;
            move.Cost = cost;
            move.Saved = DateTime.Now;
            move.Note = note.Length == 0 ? "инвентаризация" : note;

            Moves.Add(move);
            return move;
        }

        /// <summary>
        /// Списание по заявке: одна позиция заявки — одно движение.
        /// Повторное списание по тому же номеру не делается.
        /// </summary>
        public List<StockMove> WriteOff(SavedEstimate estimate)
        {
            List<StockMove> written = new List<StockMove>();
            if (estimate == null) return written;

            foreach (EstimateItem item in estimate.Items)
            {
                StockMove move = new StockMove();
                move.Article = item.Article ?? "";
                move.Name = item.Name ?? "";
                move.Quantity = -Math.Abs(item.Quantity);
                move.Cost = item.Cost;
                move.Saved = DateTime.Now;
                move.Number = estimate.Number;
                move.Note = "списание по заявке";

                Moves.Add(move);
                written.Add(move);
            }

            return written;
        }

        /// <summary>Позиции, по которым остаток ниже минимума.</summary>
        public List<string> BelowMinimum(IList<ServiceItem> catalog)
        {
            List<string> low = new List<string>();
            if (catalog == null) return low;

            foreach (ServiceItem item in catalog)
            {
                if (item.MinStock <= 0m) continue;

                decimal stock = Stock(item.Article, item.Name);
                if (stock >= item.MinStock) continue;

                low.Add(item.Name + " (" + Fmt.Qty(stock) + " из " + Fmt.Qty(item.MinStock) + ")");
            }

            return low;
        }

        private static bool Same(StockMove move, string article, string name)
        {
            if (!string.IsNullOrEmpty(article) && !string.IsNullOrEmpty(move.Article))
                return string.Equals(move.Article, article, StringComparison.CurrentCultureIgnoreCase);

            return string.Equals(move.Name, name ?? "", StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>Итоги по деньгам и себестоимости заявок.</summary>
    internal sealed class MarginInfo
    {
        public decimal Revenue;         // сумма заявок к оплате
        public decimal Cost;            // себестоимость позиций
        public decimal Profit;          // прибыль
        public int Requests;

        public int Percent
        {
            get
            {
                if (Revenue <= 0m) return 0;
                return (int)Math.Round(Profit * 100m / Revenue);
            }
        }
    }

    /// <summary>Расчёт себестоимости и маржи.</summary>
    internal static class Margin
    {
        /// <summary>Себестоимость одной позиции заявки.</summary>
        public static decimal ItemCost(EstimateItem item)
        {
            return Math.Round(item.Cost * item.Quantity, 2);
        }

        /// <summary>Себестоимость заявки целиком.</summary>
        public static decimal EstimateCost(SavedEstimate estimate)
        {
            if (estimate == null) return 0m;

            decimal total = 0m;
            foreach (EstimateItem item in estimate.Items) total += ItemCost(item);

            return total;
        }

        /// <summary>Прибыль по заявке: сумма к оплате минус себестоимость.</summary>
        public static decimal EstimateProfit(SavedEstimate estimate)
        {
            if (estimate == null) return 0m;
            return estimate.Total - EstimateCost(estimate);
        }

        /// <summary>Прибыль по позиции в заявке.</summary>
        public static decimal ItemProfit(EstimateItem item)
        {
            return Math.Round((item.Price - item.Cost) * item.Quantity, 2);
        }

        /// <summary>Итоги по списку заявок.</summary>
        public static MarginInfo Build(IList<SavedEstimate> estimates)
        {
            MarginInfo info = new MarginInfo();
            if (estimates == null) return info;

            foreach (SavedEstimate estimate in estimates)
            {
                info.Requests++;
                info.Revenue += estimate.Total;
                info.Cost += EstimateCost(estimate);
            }

            info.Profit = info.Revenue - info.Cost;
            info.Revenue = Math.Round(info.Revenue, 2);
            info.Cost = Math.Round(info.Cost, 2);
            info.Profit = Math.Round(info.Profit, 2);

            return info;
        }
    }

    /// <summary>Запись и чтение склада.</summary>
    internal static class WarehouseJson
    {
        public const string MoveFormat = "raschet-smeta-stockmove";

        public static string ToJson(StockMove move)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = MoveFormat;
            root["article"] = move.Article;
            root["name"] = move.Name;
            root["quantity"] = move.Quantity;
            root["cost"] = move.Cost;
            root["saved"] = move.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            root["number"] = move.Number;
            root["note"] = move.Note;
            return SimpleJson.Write(root);
        }

        public static StockMove FromJson(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null || SimpleJson.Text(root, "format") != MoveFormat) return null;

            StockMove move = new StockMove();
            move.Article = SimpleJson.Text(root, "article");
            move.Name = SimpleJson.Text(root, "name");
            move.Quantity = SimpleJson.Number(root, "quantity", 0m);
            move.Cost = SimpleJson.Number(root, "cost", 0m);
            move.Number = SimpleJson.Text(root, "number");
            move.Note = SimpleJson.Text(root, "note");

            DateTime saved;
            if (DateTime.TryParse(SimpleJson.Text(root, "saved"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out saved))
                move.Saved = saved;

            if (move.Quantity == 0m) return null;
            if (move.Name.Length == 0 && move.Article.Length == 0) return null;

            return move;
        }
    }

    /// <summary>Склад в файлах: одно движение — одна строка файла.</summary>
    internal sealed class FileWarehouse : IWarehouseStore
    {
        public string Title { get { return "Склад в файлах: " + FileName; } }

        private static string FileName
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, "склад.json"); }
        }

        public Warehouse Load()
        {
            Warehouse warehouse = new Warehouse();
            string file = FileName;
            if (!File.Exists(file)) return warehouse;

            try
            {
                IDictionary<string, object> root =
                    SimpleJson.Parse(File.ReadAllText(file, Encoding.UTF8)) as IDictionary<string, object>;

                if (root != null)
                {
                    foreach (object entry in SimpleJson.Array(root, "moves"))
                    {
                        IDictionary<string, object> map = entry as IDictionary<string, object>;
                        if (map == null) continue;

                        StockMove move = new StockMove();
                        move.Article = SimpleJson.Text(map, "article");
                        move.Name = SimpleJson.Text(map, "name");
                        move.Quantity = SimpleJson.Number(map, "quantity", 0m);
                        move.Cost = SimpleJson.Number(map, "cost", 0m);
                        move.Number = SimpleJson.Text(map, "number");
                        move.Note = SimpleJson.Text(map, "note");

                        DateTime saved;
                        if (DateTime.TryParse(SimpleJson.Text(map, "saved"), CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out saved))
                            move.Saved = saved;

                        if (move.Quantity == 0m) continue;
                        warehouse.Moves.Add(move);
                    }
                }
            }
            catch { /* повреждённый файл: остатки начнутся с нуля */ }

            return warehouse;
        }

        public void Save(Warehouse warehouse)
        {
            if (warehouse == null) return;

            string folder = PriceBook.StoreFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            List<object> moves = new List<object>();

            foreach (StockMove move in warehouse.Moves)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["article"] = move.Article ?? "";
                map["name"] = move.Name ?? "";
                map["quantity"] = move.Quantity;
                map["cost"] = move.Cost;
                map["saved"] = move.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                map["number"] = move.Number ?? "";
                map["note"] = move.Note ?? "";
                moves.Add(map);
            }

            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = "raschet-smeta-warehouse";
            root["moves"] = moves;

            File.WriteAllText(FileName, SimpleJson.Write(root), new UTF8Encoding(true));
        }
    }

    /// <summary>Хранилище склада: файлы или база данных.</summary>
    internal interface IWarehouseStore
    {
        string Title { get; }
        Warehouse Load();
        void Save(Warehouse warehouse);
    }
}
