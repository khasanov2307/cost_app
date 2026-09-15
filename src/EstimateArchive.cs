// ---------------------------------------------------------------------------
//  Архив смет: сохранение, список и открытие.
//
//  Номер сметы присваивается автоматически и нумеруется в рамках года:
//  1/2026, 2/2026 и так далее. Дата — дата сохранения.
//
//  Хранение:
//    * файлы — по одному файлу на смету в папке «Сметы»;
//    * база данных — таблица smeta_estimates, позиции хранятся в jsonb.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Позиция сохранённой сметы.</summary>
    internal sealed class EstimateItem
    {
        public string Group = "";
        public string Article = "";
        public string Name = "";
        public string Unit = "";
        public decimal Quantity = 1m;
        public decimal Price;
    }

    /// <summary>Сохранённая смета.</summary>
    internal sealed class SavedEstimate
    {
        public string Number = "";          // 12/2026
        public int Year;                    // год нумерации
        public int Sequence;                // порядковый номер в году
        public DateTime Saved = DateTime.Now;
        public string Customer = "";
        public decimal Discount;
        public List<EstimateItem> Items = new List<EstimateItem>();

        /// <summary>Сумма к оплате с учётом скидки.</summary>
        public decimal Total
        {
            get
            {
                decimal subtotal = 0m;
                foreach (EstimateItem item in Items) subtotal += item.Price * item.Quantity;

                decimal discount = AppSettings.ClampDiscount(Discount);
                if (discount <= 0m) return subtotal;

                return subtotal - Math.Round(subtotal * discount / 100m, 2, MidpointRounding.AwayFromZero);
            }
        }

        public decimal Subtotal
        {
            get
            {
                decimal subtotal = 0m;
                foreach (EstimateItem item in Items) subtotal += item.Price * item.Quantity;
                return subtotal;
            }
        }

        /// <summary>Подпись для списка: номер, дата, заказчик и сумма.</summary>
        public string Caption
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.Append("№ ").Append(Number.Length == 0 ? "без номера" : Number);
                text.Append("   от ").Append(Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));
                if (Customer.Length > 0) text.Append("   ").Append(Customer);
                text.Append("   позиций: ").Append(Items.Count);
                text.Append("   на сумму: ").Append(Fmt.Money(Total)).Append(" \u20BD");
                return text.ToString();
            }
        }
    }

    /// <summary>Следующий номер сметы в рамках года.</summary>
    internal static class EstimateNumbering
    {
        /// <summary>Следующий номер по уже сохранённым сметам.</summary>
        public static string Next(IList<SavedEstimate> saved, int year)
        {
            int maximum = 0;

            foreach (SavedEstimate estimate in saved)
            {
                if (estimate.Year != year) continue;
                if (estimate.Sequence > maximum) maximum = estimate.Sequence;
            }

            return (maximum + 1).ToString(CultureInfo.InvariantCulture) + "/" +
                   year.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Разбор номера вида «12/2026». Возвращает false, если номер не подходит.</summary>
        public static bool Parse(string number, out int sequence, out int year)
        {
            sequence = 0;
            year = 0;
            if (string.IsNullOrEmpty(number)) return false;

            string[] parts = number.Trim().Split('/');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence))
                return false;
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                return false;

            return sequence > 0 && year >= 2000 && year <= 2200;
        }
    }

    /// <summary>Архив смет: файлы или база данных.</summary>
    internal interface IEstimateArchive
    {
        List<SavedEstimate> Load();
        void Save(SavedEstimate estimate);
        bool Delete(SavedEstimate estimate);
        string Title { get; }
    }

    /// <summary>Архив смет в файлах: по одному файлу на смету.</summary>
    internal sealed class FileEstimateArchive : IEstimateArchive
    {
        /// <summary>Папка архива рядом с остальными данными программы.</summary>
        public static string Folder
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, "Сметы"); }
        }

        public string Title { get { return "Сметы в файлах: " + Folder; } }

        public List<SavedEstimate> Load()
        {
            List<SavedEstimate> result = new List<SavedEstimate>();
            if (!System.IO.Directory.Exists(Folder)) return result;

            string[] files = System.IO.Directory.GetFiles(Folder, "*.json");

            foreach (string file in files)
            {
                SavedEstimate estimate = EstimateJson.Read(file);
                if (estimate != null) result.Add(estimate);
            }

            Sort(result);
            return result;
        }

        public void Save(SavedEstimate estimate)
        {
            if (!System.IO.Directory.Exists(Folder)) System.IO.Directory.CreateDirectory(Folder);
            EstimateJson.Write(FileOf(estimate), estimate);
        }

        public bool Delete(SavedEstimate estimate)
        {
            try
            {
                string file = FileOf(estimate);
                if (!System.IO.File.Exists(file)) return false;

                System.IO.File.Delete(file);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Имя файла сметы: год и порядковый номер.</summary>
        private static string FileOf(SavedEstimate estimate)
        {
            return System.IO.Path.Combine(Folder, "смета-" + estimate.Year.ToString("0000", CultureInfo.InvariantCulture) +
                                                 "-" + estimate.Sequence.ToString("0000", CultureInfo.InvariantCulture) + ".json");
        }

        private static void Sort(List<SavedEstimate> list)
        {
            list.Sort(delegate(SavedEstimate left, SavedEstimate right)
            {
                if (left.Year != right.Year) return right.Year.CompareTo(left.Year);
                return right.Sequence.CompareTo(left.Sequence);
            });
        }
    }

    /// <summary>Запись и чтение сметы в виде JSON.</summary>
    internal static class EstimateJson
    {
        public static void Write(string path, SavedEstimate estimate)
        {
            File.WriteAllText(path, ToJson(estimate), new UTF8Encoding(true));
        }

        public static SavedEstimate Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        public static string ToJson(SavedEstimate estimate)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = "raschet-smeta-estimate";
            root["version"] = 1;
            root["number"] = estimate.Number;
            root["year"] = estimate.Year;
            root["sequence"] = estimate.Sequence;
            root["saved"] = estimate.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            root["customer"] = estimate.Customer;
            root["discount"] = estimate.Discount;

            List<object> items = new List<object>();
            foreach (EstimateItem item in estimate.Items)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["group"] = item.Group;
                map["article"] = item.Article;
                map["name"] = item.Name;
                map["unit"] = item.Unit;
                map["quantity"] = item.Quantity;
                map["price"] = item.Price;
                items.Add(map);
            }
            root["items"] = items;

            return SimpleJson.Write(root);
        }

        public static SavedEstimate FromJson(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null) return null;
            if (SimpleJson.Text(root, "format") != "raschet-smeta-estimate") return null;

            SavedEstimate estimate = new SavedEstimate();
            estimate.Number = SimpleJson.Text(root, "number");
            estimate.Year = (int)SimpleJson.Number(root, "year", 0m);
            estimate.Sequence = (int)SimpleJson.Number(root, "sequence", 0m);
            estimate.Customer = SimpleJson.Text(root, "customer");
            estimate.Discount = AppSettings.ClampDiscount(SimpleJson.Number(root, "discount", 0m));

            DateTime saved;
            if (DateTime.TryParse(SimpleJson.Text(root, "saved"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out saved))
                estimate.Saved = saved;

            // старые записи могли не хранить год и номер — разбираем из подписи
            int sequence, year;
            if ((estimate.Year == 0 || estimate.Sequence == 0) &&
                EstimateNumbering.Parse(estimate.Number, out sequence, out year))
            {
                estimate.Sequence = sequence;
                estimate.Year = year;
            }

            if (estimate.Year == 0) estimate.Year = estimate.Saved.Year;

            foreach (object entry in SimpleJson.Array(root, "items"))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                EstimateItem item = new EstimateItem();
                item.Group = SimpleJson.Text(map, "group");
                item.Article = SimpleJson.Text(map, "article");
                item.Name = SimpleJson.Text(map, "name");
                item.Unit = SimpleJson.Text(map, "unit");
                item.Quantity = SimpleJson.Number(map, "quantity", 1m);
                item.Price = SimpleJson.Number(map, "price", 0m);

                if (item.Name.Length == 0) continue;
                if (item.Quantity <= 0m) item.Quantity = 1m;

                estimate.Items.Add(item);
            }

            return estimate;
        }
    }
}
