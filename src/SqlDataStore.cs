// ---------------------------------------------------------------------------
//  Хранилище данных в PostgreSQL.
//
//  Таблицы создаются при первом подключении, если их ещё нет:
//    smeta_prices   — прайс-лист;
//    smeta_templates— наборы услуг (позиции хранятся в jsonb);
//    smeta_settings — настройки: реквизиты, тема, логотип, номер сметы;
//    smeta_users    — пользователи программы (вход по паролю).
//
//  Работает через PgClient — свой клиент протокола PostgreSQL,
//  без сторонних библиотек и без драйвера ODBC.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KotovCalc;

namespace KotovCalc
{
    /// <summary>Хранилище в PostgreSQL.</summary>
    internal sealed class SqlDataStore : IDataStore
    {
        private readonly PgConnectionInfo _info;

        public SqlDataStore(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
        }

        public PgConnectionInfo Info { get { return _info; } }

        public string Title
        {
            get { return "База данных PostgreSQL: " + _info.Describe(); }
        }

        /// <summary>Новое подключение к базе.</summary>
        private PgClient Open()
        {
            PgClient client = new PgClient(_info);
            client.Connect();
            return client;
        }

        public string Test()
        {
            try
            {
                using (PgClient client = Open())
                {
                    object version = client.Scalar("SHOW server_version");
                    if (version == null) return client.LastError ?? "База не ответила на запрос.";

                    object database = client.Scalar("SELECT current_database()");
                    return null;
                }
            }
            catch (PgException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return "Не удалось подключиться к базе данных: " + ex.Message;
            }
        }

        public void Prepare()
        {
            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);
            }
        }

        /// <summary>Создание таблиц. Возвращает текст ошибки или null.</summary>
        public static string CreateSchema(PgClient client)
        {
            string[] commands = new string[]
            {
                @"CREATE TABLE IF NOT EXISTS smeta_prices (
                    id serial PRIMARY KEY,
                    grp text NOT NULL DEFAULT '',
                    article text NOT NULL DEFAULT '',
                    name text NOT NULL,
                    unit text NOT NULL DEFAULT 'шт.',
                    price numeric(12,2) NOT NULL DEFAULT 0,
                    sort_order int NOT NULL DEFAULT 0)",

                @"CREATE TABLE IF NOT EXISTS smeta_templates (
                    name text PRIMARY KEY,
                    saved timestamp NOT NULL DEFAULT now(),
                    items jsonb NOT NULL DEFAULT '[]'::jsonb)",

                @"CREATE TABLE IF NOT EXISTS smeta_settings (
                    name text PRIMARY KEY,
                    value text NOT NULL DEFAULT '')",

                @"CREATE TABLE IF NOT EXISTS smeta_users (
                    login text PRIMARY KEY,
                    password_hash text NOT NULL,
                    created timestamp NOT NULL DEFAULT now())"
            };

            foreach (string command in commands)
            {
                if (!client.Execute(command)) return client.LastError;
            }

            return null;
        }

        // ------------------------------------------------------- прайс-лист

        public List<ServiceItem> LoadPrices()
        {
            List<ServiceItem> items = new List<ServiceItem>();

            using (PgClient client = Open())
            {
                List<PgRow> rows = client.Query(
                    "SELECT grp, article, name, unit, price FROM smeta_prices ORDER BY sort_order, id");

                if (rows == null) throw new PgException(client.LastError);

                foreach (PgRow row in rows)
                {
                    ServiceItem item = new ServiceItem();
                    item.Group = Text(row["grp"]);
                    item.Article = Text(row["article"]);
                    item.Name = Text(row["name"]);
                    item.Unit = Text(row["unit"]);
                    if (item.Unit.Length == 0) item.Unit = Uom.List[0];
                    item.Price = Number(row["price"]);
                    items.Add(item);
                }
            }

            return items;
        }

        public void SavePrices(IList<ServiceItem> prices)
        {
            using (PgClient client = Open())
            {
                // прайс-лист заменяется целиком: так порядок разделов сохраняется
                if (!client.Execute("BEGIN")) throw new PgException(client.LastError ?? "Не удалось начать запись.");
                if (!client.Execute("DELETE FROM smeta_prices")) throw Fail(client);

                int order = 0;
                foreach (ServiceItem item in prices)
                {
                    order++;
                    bool ok = client.Execute(
                        "INSERT INTO smeta_prices (grp, article, name, unit, price, sort_order) " +
                        "VALUES ($1, $2, $3, $4, $5, $6)",
                        item.Group ?? "", item.Article ?? "", item.Name ?? "",
                        item.Unit ?? Uom.List[0], item.Price, order);

                    if (!ok)
                    {
                        client.Execute("ROLLBACK");
                        throw Fail(client);
                    }
                }

                if (!client.Execute("COMMIT")) throw Fail(client);
            }
        }

        // ------------------------------------------------------ наборы услуг

        public List<ServiceTemplate> LoadTemplates()
        {
            List<ServiceTemplate> templates = new List<ServiceTemplate>();

            using (PgClient client = Open())
            {
                List<PgRow> rows = client.Query(
                    "SELECT name, to_char(saved, 'YYYY-MM-DD HH24:MI:SS') AS saved_at, items::text AS items " +
                    "FROM smeta_templates ORDER BY name");

                if (rows == null) throw new PgException(client.LastError);

                foreach (PgRow row in rows)
                {
                    ServiceTemplate template = new ServiceTemplate();
                    template.Name = Text(row["name"]);

                    DateTime saved;
                    if (DateTime.TryParse(Text(row["saved_at"]), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out saved))
                        template.Saved = saved;

                    foreach (TemplateItem item in ParseItems(Text(row["items"])))
                        template.Items.Add(item);

                    templates.Add(template);
                }
            }

            return templates;
        }

        public void SaveTemplates(IList<ServiceTemplate> templates)
        {
            using (PgClient client = Open())
            {
                if (!client.Execute("BEGIN")) throw new PgException(client.LastError ?? "Не удалось начать запись.");

                // наборы, которых больше нет, удаляем
                StringBuilder keep = new StringBuilder();
                foreach (ServiceTemplate template in templates)
                {
                    if (keep.Length > 0) keep.Append(", ");
                    keep.Append("'").Append(template.Name.Replace("'", "''")).Append("'");
                }

                string delete = keep.Length == 0
                    ? "DELETE FROM smeta_templates"
                    : "DELETE FROM smeta_templates WHERE name NOT IN (" + keep + ")";

                if (!client.Execute(delete)) { client.Execute("ROLLBACK"); throw Fail(client); }

                foreach (ServiceTemplate template in templates)
                {
                    bool ok = client.Execute(
                        @"INSERT INTO smeta_templates (name, saved, items) VALUES ($1, now(), $2::jsonb)
                          ON CONFLICT (name) DO UPDATE SET saved = now(), items = EXCLUDED.items",
                        template.Name, ItemsToJson(template));

                    if (!ok) { client.Execute("ROLLBACK"); throw Fail(client); }
                }

                if (!client.Execute("COMMIT")) throw Fail(client);
            }
        }

        /// <summary>Позиции набора в виде jsonb-массива.</summary>
        public static string ItemsToJson(ServiceTemplate template)
        {
            List<object> items = new List<object>();

            foreach (TemplateItem item in template.Items)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["group"] = item.Group ?? "";
                map["article"] = item.Article ?? "";
                map["name"] = item.Name ?? "";
                map["quantity"] = item.Quantity;
                items.Add(map);
            }

            return SimpleJson.Write(items);
        }

        /// <summary>Разбор jsonb-массива позиций.</summary>
        public static List<TemplateItem> ParseItems(string json)
        {
            List<TemplateItem> items = new List<TemplateItem>();

            object parsed = SimpleJson.Parse(json ?? "[]");
            System.Collections.IEnumerable list = parsed as System.Collections.IEnumerable;
            if (list == null) return items;

            foreach (object entry in list)
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                TemplateItem item = new TemplateItem();
                item.Group = SimpleJson.Text(map, "group");
                item.Article = SimpleJson.Text(map, "article");
                item.Name = SimpleJson.Text(map, "name");
                item.Quantity = SimpleJson.Number(map, "quantity", 1m);
                if (item.Quantity <= 0m) item.Quantity = 1m;
                if (item.Name.Length == 0) continue;

                items.Add(item);
            }

            return items;
        }

        // --------------------------------------------------------- настройки

        public StoreSnapshot LoadAll()
        {
            StoreSnapshot snapshot = new StoreSnapshot();
            snapshot.Prices = LoadPrices();
            snapshot.Templates = LoadTemplates();
            snapshot.HasPrices = true;
            snapshot.HasTemplates = true;

            Dictionary<string, string> settings = LoadSettings();

            snapshot.Document.Number = Get(settings, "number");
            snapshot.Document.Customer = Get(settings, "customer");

            decimal discount;
            if (decimal.TryParse(Get(settings, "discount"), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out discount))
                snapshot.Document.Discount = AppSettings.ClampDiscount(discount);

            snapshot.Document.Normalize();

            snapshot.Theme = Get(settings, "theme") == "dark" ? "dark" : "light";
            snapshot.Logo = Get(settings, "logo");
            snapshot.LastTemplate = Get(settings, "template");
            snapshot.HasSettings = true;

            return snapshot;
        }

        public void SaveSettings(StoreSnapshot snapshot)
        {
            Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.Ordinal);
            settings["number"] = snapshot.Document.Number ?? "";
            settings["customer"] = snapshot.Document.Customer ?? "";
            settings["discount"] = snapshot.Document.Discount.ToString("0.##", CultureInfo.InvariantCulture);
            settings["theme"] = snapshot.Theme == "dark" ? "dark" : "light";
            settings["logo"] = snapshot.Logo ?? "";
            settings["template"] = snapshot.LastTemplate ?? "";

            using (PgClient client = Open())
            {
                if (!client.Execute("BEGIN")) throw new PgException(client.LastError ?? "Не удалось начать запись.");

                foreach (KeyValuePair<string, string> pair in settings)
                {
                    bool ok = client.Execute(
                        @"INSERT INTO smeta_settings (name, value) VALUES ($1, $2)
                          ON CONFLICT (name) DO UPDATE SET value = EXCLUDED.value",
                        pair.Key, pair.Value);

                    if (!ok) { client.Execute("ROLLBACK"); throw Fail(client); }
                }

                if (!client.Execute("COMMIT")) throw Fail(client);
            }
        }

        private Dictionary<string, string> LoadSettings()
        {
            Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.Ordinal);

            using (PgClient client = Open())
            {
                List<PgRow> rows = client.Query("SELECT name, value FROM smeta_settings");
                if (rows == null) throw new PgException(client.LastError);

                foreach (PgRow row in rows)
                    settings[Text(row["name"])] = Text(row["value"]);
            }

            return settings;
        }

        // ------------------------------------------- пользователи программы

        /// <summary>Есть ли в базе хотя бы один пользователь программы.</summary>
        public bool HasUsers()
        {
            using (PgClient client = Open())
            {
                object count = client.Scalar("SELECT count(*) FROM smeta_users");
                if (count == null) throw new PgException(client.LastError);
                return Number(count) > 0m;
            }
        }

        /// <summary>Создание или смена пользователя программы.</summary>
        public void SaveUser(string login, string password)
        {
            using (PgClient client = Open())
            {
                bool ok = client.Execute(
                    @"INSERT INTO smeta_users (login, password_hash) VALUES ($1, $2)
                      ON CONFLICT (login) DO UPDATE SET password_hash = EXCLUDED.password_hash",
                    login, UserPassword.Create(password));

                if (!ok) throw Fail(client);
            }
        }

        /// <summary>Проверка логина и пароля пользователя программы.</summary>
        public bool CheckUser(string login, string password)
        {
            using (PgClient client = Open())
            {
                object hash = client.Scalar("SELECT password_hash FROM smeta_users WHERE login = $1", login);
                if (hash == null) return false;
                return UserPassword.Verify(password, Text(hash));
            }
        }

        /// <summary>
        /// Отпечаток данных: количество записей в таблицах. По нему программа
        /// замечает правки, сделанные другими пользователями.
        /// </summary>
        public string Stamp()
        {
            using (PgClient client = Open())
            {
                object prices = client.Scalar("SELECT count(*) FROM smeta_prices");
                object templates = client.Scalar("SELECT count(*) FROM smeta_templates");
                object settings = client.Scalar("SELECT count(*) FROM smeta_settings");

                if (prices == null) throw new PgException(client.LastError);

                return "p" + prices + "t" + templates + "s" + settings;
            }
        }
        // -------------------------------------------------------- помощники

        private static PgException Fail(PgClient client)
        {
            return new PgException(client.LastError ?? "Не удалось записать данные в базу.");
        }

        private static string Text(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static decimal Number(object value)
        {
            if (value == null) return 0m;

            decimal number;
            if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                return number;

            return 0m;
        }

        private static string Get(Dictionary<string, string> map, string key)
        {
            string value;
            return map.TryGetValue(key, out value) ? value : "";
        }
    }
}
