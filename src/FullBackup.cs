// ---------------------------------------------------------------------------
//  Полная выгрузка и загрузка данных для перехода на новую версию программы.
//
//  В один файл-архив попадает всё, что программа хранит: каталог номенклатуры,
//  наборы услуг, реквизиты, тема и логотип, заявки со статусами, заказчики,
//  склад и кассы с оплатами. Файл переносится на другую машину и загружается
//  там — данные встают на место независимо от того, работала программа
//  с файлами или с базой данных.
//
//  Формат: zip-архив с оглавлением «manifest.json» и отдельным файлом
//  на каждый раздел. Оглавление читается первым: по нему видно, какие
//  разделы есть в архиве, поэтому загрузка старых архивов не ломается.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KotovCalc
{
    /// <summary>Что нашлось в архиве: для отчёта о загрузке.</summary>
    internal sealed class BackupContent
    {
        public string Version = "";
        public string Created = "";
        public string Source = "";

        public bool Prices;
        public bool Templates;
        public bool Settings;
        public bool Estimates;
        public bool Customers;
        public bool Warehouse;
        public bool Cash;

        public int PriceCount;
        public int TemplateCount;
        public int EstimateCount;
        public int CustomerCount;
        public int MoveCount;
        public int PaymentCount;

        /// <summary>Короткое описание содержимого архива.</summary>
        public string Summary()
        {
            StringBuilder text = new StringBuilder();

            if (Prices) text.Append("каталог: ").Append(PriceCount).Append("   ");
            if (Templates) text.Append("наборы: ").Append(TemplateCount).Append("   ");
            if (Estimates) text.Append("заявки: ").Append(EstimateCount).Append("   ");
            if (Customers) text.Append("заказчики: ").Append(CustomerCount).Append("   ");
            if (Warehouse) text.Append("склад: ").Append(MoveCount).Append("   ");
            if (Cash) text.Append("оплаты: ").Append(PaymentCount).Append("   ");
            if (Settings) text.Append("реквизиты и тема");

            return text.ToString().Trim();
        }
    }

    /// <summary>Полная выгрузка и загрузка данных.</summary>
    internal static class FullBackup
    {
        /// <summary>Версия формата архива.</summary>
        public const int FormatVersion = 1;

        /// <summary>Имя файла выгрузки по умолчанию.</summary>
        public static string SuggestedFileName(DateTime now)
        {
            return "Заявки_на_расчет_данные_" + now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture) + ".zip";
        }

        // ------------------------------------------------------------- выгрузка

        /// <summary>Собрать всё в архив и записать файл.</summary>
        public static void Save(string path)
        {
            IDataStore store = ConnectionSettings.Store;
            IEstimateArchive estimates = ConnectionSettings.Archive;
            ICustomerStore customers = ConnectionSettings.Customers;
            IWarehouseStore warehouse = ConnectionSettings.Warehouse;
            ICashBookStore cash = ConnectionSettings.CashBook;

            StoreSnapshot snapshot = store.LoadAll();
            List<SavedEstimate> estimateList = estimates.Load();
            CustomerBook customerBook = customers.Load();
            Warehouse stock = warehouse.Load();
            CashBook money = cash.Load();

            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(archive, "каталог.json", PricesJson(snapshot.Prices));
                Write(archive, "наборы.json", TemplatesJson(snapshot.Templates));
                Write(archive, "реквизиты.json", SettingsJson(snapshot));

                Write(archive, "заявки.json", EstimatesJson(estimateList));
                Write(archive, "заказчики.json", CustomersJson(customerBook));
                Write(archive, "склад.json", WarehouseJson(stock));
                Write(archive, "кассы.json", CashJson(money));

                SortedDictionary<string, object> manifest = new SortedDictionary<string, object>(StringComparer.Ordinal);
                manifest["format"] = "raschet-smeta-backup";
                manifest["version"] = FormatVersion;
                manifest["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                manifest["source"] = store.Title;
                manifest["prices"] = snapshot.Prices.Count;
                manifest["templates"] = snapshot.Templates.Count;
                manifest["estimates"] = estimateList.Count;
                manifest["customers"] = customerBook.Customers.Count;
                manifest["moves"] = stock.Moves.Count;
                manifest["payments"] = money.Payments.Count;
                manifest["operations"] = money.Operations.Count;

                Write(archive, "manifest.json", SimpleJson.Write(manifest));
            }
        }

        // -------------------------------------------------------------- загрузка

        /// <summary>Прочитать, что есть в архиве, не меняя данных.</summary>
        public static BackupContent Inspect(string path, out string error)
        {
            error = null;

            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    string manifest = Read(archive, "manifest.json");

                    if (manifest == null)
                    {
                        error = "Это не файл выгрузки: в архиве нет оглавления.";
                        return null;
                    }

                    IDictionary<string, object> root =
                        SimpleJson.Parse(manifest) as IDictionary<string, object>;

                    if (root == null || SimpleJson.Text(root, "format") != "raschet-smeta-backup")
                    {
                        error = "Это не файл выгрузки программы «Расчет заявки».";
                        return null;
                    }

                    BackupContent content = new BackupContent();
                    content.Version = Convert.ToString(SimpleJson.Number(root, "version", 0m),
                                                       CultureInfo.InvariantCulture);
                    content.Created = SimpleJson.Text(root, "created");
                    content.Source = SimpleJson.Text(root, "source");

                    content.PriceCount = (int)SimpleJson.Number(root, "prices", 0m);
                    content.TemplateCount = (int)SimpleJson.Number(root, "templates", 0m);
                    content.EstimateCount = (int)SimpleJson.Number(root, "estimates", 0m);
                    content.CustomerCount = (int)SimpleJson.Number(root, "customers", 0m);
                    content.MoveCount = (int)SimpleJson.Number(root, "moves", 0m);
                    content.PaymentCount = (int)SimpleJson.Number(root, "payments", 0m);

                    content.Prices = archive.GetEntry("каталог.json") != null;
                    content.Templates = archive.GetEntry("наборы.json") != null;
                    content.Settings = archive.GetEntry("реквизиты.json") != null;
                    content.Estimates = archive.GetEntry("заявки.json") != null;
                    content.Customers = archive.GetEntry("заказчики.json") != null;
                    content.Warehouse = archive.GetEntry("склад.json") != null;
                    content.Cash = archive.GetEntry("кассы.json") != null;

                    return content;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Загрузить данные из архива. Разделы, которых в архиве нет, не трогаются:
        /// поэтому архив от старой версии загружается без потерь.
        /// </summary>
        public static BackupContent Restore(string path)
        {
            IDataStore store = ConnectionSettings.Store;
            IEstimateArchive estimates = ConnectionSettings.Archive;
            ICustomerStore customers = ConnectionSettings.Customers;
            IWarehouseStore warehouse = ConnectionSettings.Warehouse;
            ICashBookStore cash = ConnectionSettings.CashBook;

            BackupContent content = new BackupContent();

            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                // --- каталог
                string prices = Read(archive, "каталог.json");
                if (prices != null)
                {
                    List<ServiceItem> list = PricesFromJson(prices);
                    store.SavePrices(list);
                    content.Prices = true;
                    content.PriceCount = list.Count;
                }

                // --- наборы услуг
                string templates = Read(archive, "наборы.json");
                if (templates != null)
                {
                    List<ServiceTemplate> list = TemplatesFromJson(templates);
                    store.SaveTemplates(list);
                    content.Templates = true;
                    content.TemplateCount = list.Count;
                }

                // --- реквизиты, тема и логотип
                string settings = Read(archive, "реквизиты.json");
                if (settings != null)
                {
                    StoreSnapshot snapshot = SettingsFromJson(settings);
                    store.SaveSettings(snapshot);
                    content.Settings = true;
                }

                // --- заявки со статусами
                string estimateList = Read(archive, "заявки.json");
                if (estimateList != null)
                {
                    List<SavedEstimate> list = EstimatesFromJson(estimateList);

                    foreach (SavedEstimate estimate in list) estimates.Save(estimate);

                    content.Estimates = true;
                    content.EstimateCount = list.Count;
                }

                // --- заказчики
                string customerList = Read(archive, "заказчики.json");
                if (customerList != null)
                {
                    CustomerBook book = CustomersFromJson(customerList);
                    customers.Save(book);
                    content.Customers = true;
                    content.CustomerCount = book.Customers.Count;
                }

                // --- склад
                string stock = Read(archive, "склад.json");
                if (stock != null)
                {
                    Warehouse moves = WarehouseFromJson(stock);
                    warehouse.Save(moves);
                    content.Warehouse = true;
                    content.MoveCount = moves.Moves.Count;
                }

                // --- кассы и оплаты
                string money = Read(archive, "кассы.json");
                if (money != null)
                {
                    CashBook book = CashFromJson(money);
                    cash.Save(book);
                    content.Cash = true;
                    content.PaymentCount = book.Payments.Count;
                }
            }

            return content;
        }

        // ------------------------------------------------------------- архив

        private static void Write(ZipArchive archive, string name, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);

            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        private static string Read(ZipArchive archive, string name)
        {
            ZipArchiveEntry entry = archive.GetEntry(name);
            if (entry == null) return null;

            using (Stream stream = entry.Open())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // ------------------------------------------------- каталог и наборы

        private static string PricesJson(IList<ServiceItem> prices)
        {
            List<object> items = new List<object>();

            foreach (ServiceItem item in prices)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["group"] = item.Group ?? "";
                map["article"] = item.Article ?? "";
                map["name"] = item.Name ?? "";
                map["unit"] = item.Unit ?? "";
                map["price"] = item.Price;
                map["cost"] = item.Cost;
                map["min_stock"] = item.MinStock;
                items.Add(map);
            }

            return SimpleJson.Write(items);
        }

        private static List<ServiceItem> PricesFromJson(string json)
        {
            List<ServiceItem> prices = new List<ServiceItem>();

            foreach (object entry in Entries(json))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                ServiceItem item = new ServiceItem();
                item.Group = SimpleJson.Text(map, "group");
                item.Article = SimpleJson.Text(map, "article");
                item.Name = SimpleJson.Text(map, "name");
                item.Unit = SimpleJson.Text(map, "unit");
                item.Price = SimpleJson.Number(map, "price", 0m);
                item.Cost = SimpleJson.Number(map, "cost", 0m);
                item.MinStock = SimpleJson.Number(map, "min_stock", 0m);

                if (item.Name.Length == 0) continue;
                prices.Add(item);
            }

            return prices;
        }

        private static string TemplatesJson(IList<ServiceTemplate> templates)
        {
            List<object> items = new List<object>();

            foreach (ServiceTemplate template in templates)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["name"] = template.Name ?? "";

                List<object> positions = new List<object>();

                foreach (TemplateItem item in template.Items)
                {
                    SortedDictionary<string, object> position =
                        new SortedDictionary<string, object>(StringComparer.Ordinal);
                    position["group"] = item.Group ?? "";
                    position["article"] = item.Article ?? "";
                    position["name"] = item.Name ?? "";
                    position["quantity"] = item.Quantity;
                    positions.Add(position);
                }

                map["items"] = positions;
                items.Add(map);
            }

            return SimpleJson.Write(items);
        }

        private static List<ServiceTemplate> TemplatesFromJson(string json)
        {
            List<ServiceTemplate> templates = new List<ServiceTemplate>();

            foreach (object entry in Entries(json))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                ServiceTemplate template = new ServiceTemplate();
                template.Name = SimpleJson.Text(map, "name");

                foreach (object position in SimpleJson.Array(map, "items"))
                {
                    IDictionary<string, object> item = position as IDictionary<string, object>;
                    if (item == null) continue;

                    TemplateItem line = new TemplateItem();
                    line.Group = SimpleJson.Text(item, "group");
                    line.Article = SimpleJson.Text(item, "article");
                    line.Name = SimpleJson.Text(item, "name");
                    line.Quantity = SimpleJson.Number(item, "quantity", 1m);

                    if (line.Name.Length == 0) continue;
                    template.Items.Add(line);
                }

                if (template.Name.Length == 0) continue;
                templates.Add(template);
            }

            return templates;
        }

        // -------------------------------------------------------- реквизиты

        private static string SettingsJson(StoreSnapshot snapshot)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["number"] = snapshot.Document.Number;
            root["customer"] = snapshot.Document.Customer;
            root["customer_phone"] = snapshot.Document.CustomerPhone;
            root["car"] = snapshot.Document.Car;
            root["plate"] = snapshot.Document.Plate;
            root["discount"] = snapshot.Document.Discount;
            root["theme"] = snapshot.Theme;
            root["logo"] = snapshot.Logo;
            root["last_template"] = snapshot.LastTemplate;
            return SimpleJson.Write(root);
        }

        private static StoreSnapshot SettingsFromJson(string json)
        {
            StoreSnapshot snapshot = new StoreSnapshot();
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null) return snapshot;

            snapshot.Document.Number = SimpleJson.Text(root, "number");
            snapshot.Document.Customer = SimpleJson.Text(root, "customer");
            snapshot.Document.CustomerPhone = PhoneMask.Format(SimpleJson.Text(root, "customer_phone"));
            snapshot.Document.Car = SimpleJson.Text(root, "car");
            snapshot.Document.Plate = SimpleJson.Text(root, "plate");
            snapshot.Document.Discount = AppSettings.ClampDiscount(SimpleJson.Number(root, "discount", 0m));
            snapshot.Document.Normalize();

            snapshot.Theme = SimpleJson.Text(root, "theme");
            if (snapshot.Theme.Length == 0) snapshot.Theme = "light";

            snapshot.Logo = SimpleJson.Text(root, "logo");
            snapshot.LastTemplate = SimpleJson.Text(root, "last_template");

            snapshot.HasSettings = true;
            return snapshot;
        }

        // ---------------------------------------------------------- заявки

        private static string EstimatesJson(IList<SavedEstimate> estimates)
        {
            List<object> items = new List<object>();

            foreach (SavedEstimate estimate in estimates)
                items.Add(EstimateToMap(estimate));

            return SimpleJson.Write(items);
        }

        private static List<SavedEstimate> EstimatesFromJson(string json)
        {
            List<SavedEstimate> estimates = new List<SavedEstimate>();

            foreach (object entry in Entries(json))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                SavedEstimate estimate = EstimateFromMap(map);
                if (estimate != null) estimates.Add(estimate);
            }

            return estimates;
        }

        /// <summary>Заявка в виде словаря: общий вид с архивом заявок.</summary>
        public static SortedDictionary<string, object> EstimateToMap(SavedEstimate estimate)
        {
            SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
            map["number"] = estimate.Number;
            map["year"] = estimate.Year;
            map["sequence"] = estimate.Sequence;
            map["saved"] = estimate.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            map["customer"] = estimate.Customer;
            map["customer_id"] = estimate.CustomerId;
            map["customer_phone"] = estimate.CustomerPhone;
            map["car"] = estimate.Car;
            map["plate"] = estimate.Plate;
            map["status"] = EstimateStatuses.Title(estimate.Status);
            map["discount"] = estimate.Discount;

            List<object> items = new List<object>();

            foreach (EstimateItem item in estimate.Items)
            {
                SortedDictionary<string, object> line = new SortedDictionary<string, object>(StringComparer.Ordinal);
                line["group"] = item.Group;
                line["article"] = item.Article;
                line["name"] = item.Name;
                line["unit"] = item.Unit;
                line["quantity"] = item.Quantity;
                line["price"] = item.Price;
                line["cost"] = item.Cost;
                items.Add(line);
            }

            map["items"] = items;
            return map;
        }

        /// <summary>Заявка из словаря.</summary>
        public static SavedEstimate EstimateFromMap(IDictionary<string, object> map)
        {
            if (map == null) return null;

            SavedEstimate estimate = new SavedEstimate();
            estimate.Number = SimpleJson.Text(map, "number");
            estimate.Year = (int)SimpleJson.Number(map, "year", 0m);
            estimate.Sequence = (int)SimpleJson.Number(map, "sequence", 0m);
            estimate.Customer = SimpleJson.Text(map, "customer");
            estimate.CustomerId = SimpleJson.Text(map, "customer_id");
            estimate.CustomerPhone = PhoneMask.Format(SimpleJson.Text(map, "customer_phone"));
            estimate.Car = SimpleJson.Text(map, "car");
            estimate.Plate = SimpleJson.Text(map, "plate");
            estimate.Status = EstimateStatuses.Parse(SimpleJson.Text(map, "status"));
            estimate.Discount = AppSettings.ClampDiscount(SimpleJson.Number(map, "discount", 0m));

            DateTime saved;
            if (DateTime.TryParse(SimpleJson.Text(map, "saved"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out saved))
                estimate.Saved = saved;

            int sequence, year;
            if ((estimate.Year == 0 || estimate.Sequence == 0) &&
                EstimateNumbering.Parse(estimate.Number, out sequence, out year))
            {
                estimate.Sequence = sequence;
                estimate.Year = year;
            }

            if (estimate.Year == 0) estimate.Year = estimate.Saved.Year;

            foreach (object entry in SimpleJson.Array(map, "items"))
            {
                IDictionary<string, object> line = entry as IDictionary<string, object>;
                if (line == null) continue;

                EstimateItem item = new EstimateItem();
                item.Group = SimpleJson.Text(line, "group");
                item.Article = SimpleJson.Text(line, "article");
                item.Name = SimpleJson.Text(line, "name");
                item.Unit = SimpleJson.Text(line, "unit");
                item.Quantity = SimpleJson.Number(line, "quantity", 1m);
                item.Price = SimpleJson.Number(line, "price", 0m);
                item.Cost = SimpleJson.Number(line, "cost", 0m);

                if (item.Name.Length == 0) continue;
                if (item.Quantity <= 0m) item.Quantity = 1m;

                estimate.Items.Add(item);
            }

            return estimate;
        }

        // ------------------------------------------------------- заказчики

        private static string CustomersJson(CustomerBook book)
        {
            List<object> items = new List<object>();

            foreach (Customer customer in book.Customers)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["id"] = customer.Id;
                map["name"] = customer.Name;
                map["phone"] = PhoneMask.Format(customer.Phone);
                map["car"] = customer.Car;
                map["plate"] = customer.Plate;
                map["note"] = customer.Note;
                items.Add(map);
            }

            return SimpleJson.Write(items);
        }

        private static CustomerBook CustomersFromJson(string json)
        {
            CustomerBook book = new CustomerBook();

            foreach (object entry in Entries(json))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                Customer customer = new Customer();
                customer.Id = SimpleJson.Text(map, "id");
                customer.Name = SimpleJson.Text(map, "name");
                customer.Phone = PhoneMask.Format(SimpleJson.Text(map, "phone"));
                customer.Car = SimpleJson.Text(map, "car");
                customer.Plate = SimpleJson.Text(map, "plate");
                customer.Note = SimpleJson.Text(map, "note");

                if (customer.Id.Length == 0) customer.Id = Guid.NewGuid().ToString("N");
                if (customer.Name.Length == 0) continue;

                book.Customers.Add(customer);
            }

            return book;
        }

        // --------------------------------------------------- склад и кассы

        private static string WarehouseJson(Warehouse warehouse)
        {
            List<object> items = new List<object>();

            foreach (StockMove move in warehouse.Moves)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["article"] = move.Article;
                map["name"] = move.Name;
                map["quantity"] = move.Quantity;
                map["cost"] = move.Cost;
                map["saved"] = move.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                map["number"] = move.Number;
                map["note"] = move.Note;
                items.Add(map);
            }

            return SimpleJson.Write(items);
        }

        private static Warehouse WarehouseFromJson(string json)
        {
            Warehouse warehouse = new Warehouse();

            foreach (object entry in Entries(json))
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

            return warehouse;
        }

        private static string CashJson(CashBook book)
        {
            List<object> desks = new List<object>();

            foreach (CashDesk desk in book.Desks)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["name"] = desk.Name;
                map["note"] = desk.Note;
                map["archive"] = desk.Archive;
                desks.Add(map);
            }

            List<object> payments = new List<object>();

            foreach (Payment payment in book.Payments)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["number"] = payment.Number;
                map["saved"] = payment.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                map["customer"] = payment.Customer;
                map["desk"] = payment.Desk;
                map["cash_desk"] = payment.CashDesk;
                map["kind"] = PaymentKinds.Title(payment.Kind);
                map["cash"] = payment.Cash;
                map["cashless"] = payment.Cashless;
                map["due"] = payment.Due;
                map["note"] = payment.Note;
                payments.Add(map);
            }

            List<object> operations = new List<object>();

            foreach (DeskOperation operation in book.Operations)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["desk"] = operation.Desk;
                map["saved"] = operation.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                map["amount"] = operation.Amount;
                map["note"] = operation.Note;
                operations.Add(map);
            }

            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["desks"] = desks;
            root["payments"] = payments;
            root["operations"] = operations;
            return SimpleJson.Write(root);
        }

        private static CashBook CashFromJson(string json)
        {
            CashBook book = new CashBook();
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null) return book;

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

            foreach (object entry in SimpleJson.Array(root, "payments"))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                Payment payment = new Payment();
                payment.Number = SimpleJson.Text(map, "number");
                payment.Customer = SimpleJson.Text(map, "customer");
                payment.Desk = SimpleJson.Text(map, "desk");
                payment.CashDesk = SimpleJson.Text(map, "cash_desk");
                payment.Kind = PaymentKinds.Parse(SimpleJson.Text(map, "kind"));
                payment.Cash = SimpleJson.Number(map, "cash", 0m);
                payment.Cashless = SimpleJson.Number(map, "cashless", 0m);
                payment.Due = SimpleJson.Number(map, "due", 0m);
                payment.Note = SimpleJson.Text(map, "note");

                DateTime saved;
                if (DateTime.TryParse(SimpleJson.Text(map, "saved"), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out saved))
                    payment.Saved = saved;

                if (payment.Number.Length == 0) continue;
                book.Payments.Add(payment);
            }

            foreach (object entry in SimpleJson.Array(root, "operations"))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                DeskOperation operation = new DeskOperation();
                operation.Desk = SimpleJson.Text(map, "desk");
                operation.Amount = SimpleJson.Number(map, "amount", 0m);
                operation.Note = SimpleJson.Text(map, "note");

                DateTime saved;
                if (DateTime.TryParse(SimpleJson.Text(map, "saved"), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out saved))
                    operation.Saved = saved;

                book.Operations.Add(operation);
            }

            // кассы, которые упомянуты только в оплатах
            foreach (Payment payment in book.Payments)
                if (payment.Desk.Length > 0 && book.Find(payment.Desk) == null)
                    book.Ensure(payment.Desk);

            return book;
        }

        private static System.Collections.IEnumerable Entries(string json)
        {
            object parsed = SimpleJson.Parse(json ?? "[]");
            return parsed as System.Collections.IEnumerable ?? new List<object>();
        }
    }
}
