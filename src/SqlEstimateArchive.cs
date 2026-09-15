// ---------------------------------------------------------------------------
//  Архив заявок в PostgreSQL: таблица smeta_estimates.
//
//  Номер заявки нумеруется в рамках года, дата — дата сохранения.
//  Позиции заявки хранятся в поле jsonb.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KotovCalc
{
    internal sealed class SqlEstimateArchive : IEstimateArchive
    {
        private readonly PgConnectionInfo _info;

        public SqlEstimateArchive(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
        }

        public string Title { get { return "Заявки в базе данных: " + _info.Describe(); } }

        private PgClient Open()
        {
            PgClient client = new PgClient(_info);
            client.Connect();
            return client;
        }

        /// <summary>Создание таблицы заявок.</summary>
        public static string CreateSchema(PgClient client)
        {
            string command =
                @"CREATE TABLE IF NOT EXISTS smeta_estimates (
                    number text PRIMARY KEY,
                    year int NOT NULL,
                    sequence int NOT NULL,
                    saved timestamp NOT NULL DEFAULT now(),
                    customer text NOT NULL DEFAULT '',
                    discount numeric(5,2) NOT NULL DEFAULT 0,
                    items jsonb NOT NULL DEFAULT '[]'::jsonb)";

            return client.Execute(command) ? null : client.LastError;
        }

        /// <summary>Следующий номер заявки в рамках года берётся из базы.</summary>
        public string NextNumber(int year)
        {
            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                object maximum = client.Scalar(
                    "SELECT coalesce(max(sequence), 0) FROM smeta_estimates WHERE year = $1", year);

                if (maximum == null) throw new PgException(client.LastError);

                decimal value;
                decimal.TryParse(Convert.ToString(maximum, CultureInfo.InvariantCulture),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out value);

                return ((int)value + 1).ToString(CultureInfo.InvariantCulture) + "/" +
                       year.ToString(CultureInfo.InvariantCulture);
            }
        }

        public List<SavedEstimate> Load()
        {
            List<SavedEstimate> result = new List<SavedEstimate>();

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                List<PgRow> rows = client.Query(
                    "SELECT number, year, sequence, to_char(saved, 'YYYY-MM-DD HH24:MI:SS') AS saved_at, " +
                    "customer, discount, items::text AS items FROM smeta_estimates " +
                    "ORDER BY year DESC, sequence DESC");

                if (rows == null) throw new PgException(client.LastError);

                foreach (PgRow row in rows)
                {
                    SavedEstimate estimate = new SavedEstimate();
                    estimate.Number = Text(row["number"]);
                    estimate.Year = (int)Number(row["year"]);
                    estimate.Sequence = (int)Number(row["sequence"]);
                    estimate.Customer = Text(row["customer"]);
                    estimate.Discount = AppSettings.ClampDiscount(Number(row["discount"]));

                    DateTime saved;
                    if (DateTime.TryParse(Text(row["saved_at"]), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out saved))
                        estimate.Saved = saved;

                    foreach (object entry in Entries(Text(row["items"])))
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

                    result.Add(estimate);
                }
            }

            return result;
        }

        public void Save(SavedEstimate estimate)
        {
            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                bool ok = client.Execute(
                    @"INSERT INTO smeta_estimates (number, year, sequence, saved, customer, discount, items)
                      VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb)
                      ON CONFLICT (number) DO UPDATE SET
                        saved = EXCLUDED.saved,
                        customer = EXCLUDED.customer,
                        discount = EXCLUDED.discount,
                        items = EXCLUDED.items",
                    estimate.Number, estimate.Year, estimate.Sequence,
                    estimate.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    estimate.Customer ?? "", estimate.Discount, ItemsJson(estimate));

                if (!ok) throw new PgException(client.LastError ?? "Не удалось сохранить заявку.");
            }
        }

        public bool Delete(SavedEstimate estimate)
        {
            using (PgClient client = Open())
            {
                bool ok = client.Execute("DELETE FROM smeta_estimates WHERE number = $1", estimate.Number);
                if (!ok) throw new PgException(client.LastError ?? "Не удалось удалить заявку.");
                return true;
            }
        }

        private static string ItemsJson(SavedEstimate estimate)
        {
            List<object> items = new List<object>();

            foreach (EstimateItem item in estimate.Items)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["group"] = item.Group ?? "";
                map["article"] = item.Article ?? "";
                map["name"] = item.Name ?? "";
                map["unit"] = item.Unit ?? "";
                map["quantity"] = item.Quantity;
                map["price"] = item.Price;
                items.Add(map);
            }

            return SimpleJson.Write(items);
        }

        private static System.Collections.IEnumerable Entries(string json)
        {
            object parsed = SimpleJson.Parse(json ?? "[]");
            return parsed as System.Collections.IEnumerable ?? new List<object>();
        }

        private static string Text(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static decimal Number(object value)
        {
            decimal number;
            if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                return number;

            return 0m;
        }
    }
}
