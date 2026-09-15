// ---------------------------------------------------------------------------
//  Справочник заказчиков и склад в PostgreSQL.
//
//  Заказчики: таблица smeta_customers.
//  Склад: таблица smeta_stockmoves (приход и расход запчастями).
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace KotovCalc
{
    /// <summary>Заказчики в базе данных.</summary>
    internal sealed class SqlCustomerBook : ICustomerStore
    {
        private readonly PgConnectionInfo _info;

        public SqlCustomerBook(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
        }

        public string Title { get { return "Заказчики в базе данных: " + _info.Describe(); } }

        private PgClient Open()
        {
            PgClient client = new PgClient(_info);
            client.Connect();
            return client;
        }

        /// <summary>Создание таблицы заказчиков.</summary>
        public static string CreateSchema(PgClient client)
        {
            string command =
                @"CREATE TABLE IF NOT EXISTS smeta_customers (
                    id text PRIMARY KEY,
                    name text NOT NULL DEFAULT '',
                    phone text NOT NULL DEFAULT '',
                    car text NOT NULL DEFAULT '',
                    plate text NOT NULL DEFAULT '',
                    note text NOT NULL DEFAULT '',
                    created timestamp NOT NULL DEFAULT now())";

            return client.Execute(command) ? null : client.LastError;
        }

        public CustomerBook Load()
        {
            CustomerBook book = new CustomerBook();

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                List<PgRow> rows = client.Query(
                    "SELECT id, name, phone, car, plate, note FROM smeta_customers ORDER BY name");
                if (rows == null) throw new PgException(client.LastError);

                foreach (PgRow row in rows)
                {
                    Customer customer = new Customer();
                    customer.Id = Text(row["id"]);
                    customer.Name = Text(row["name"]);
                    customer.Phone = Text(row["phone"]);
                    customer.Car = Text(row["car"]);
                    customer.Plate = Text(row["plate"]);
                    customer.Note = Text(row["note"]);

                    if (customer.Id.Length == 0) customer.Id = Guid.NewGuid().ToString("N");
                    if (customer.Name.Length == 0) continue;

                    book.Customers.Add(customer);
                }
            }

            return book;
        }

        public void Save(CustomerBook book)
        {
            if (book == null) return;

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                if (!client.Execute("BEGIN")) throw new PgException(client.LastError ?? "Не удалось начать запись.");

                List<string> keep = new List<string>();

                foreach (Customer customer in book.Customers)
                {
                    keep.Add(customer.Id);

                    bool ok = client.Execute(
                        @"INSERT INTO smeta_customers (id, name, phone, car, plate, note)
                          VALUES ($1, $2, $3, $4, $5, $6)
                          ON CONFLICT (id) DO UPDATE SET
                            name = EXCLUDED.name, phone = EXCLUDED.phone, car = EXCLUDED.car,
                            plate = EXCLUDED.plate, note = EXCLUDED.note",
                        customer.Id, customer.Name ?? "", customer.Phone ?? "",
                        customer.Car ?? "", customer.Plate ?? "", customer.Note ?? "");

                    if (!ok) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }
                }

                // карточки, которых больше нет в списке, убираем
                if (keep.Count == 0)
                {
                    if (!client.Execute("DELETE FROM smeta_customers"))
                    {
                        client.Execute("ROLLBACK");
                        throw new PgException(client.LastError);
                    }
                }
                else
                {
                    System.Text.StringBuilder list = new System.Text.StringBuilder();
                    foreach (string id in keep)
                    {
                        if (list.Length > 0) list.Append(", ");
                        list.Append("'").Append(id.Replace("'", "''")).Append("'");
                    }

                    if (!client.Execute("DELETE FROM smeta_customers WHERE id NOT IN (" + list + ")"))
                    {
                        client.Execute("ROLLBACK");
                        throw new PgException(client.LastError);
                    }
                }

                if (!client.Execute("COMMIT")) throw new PgException(client.LastError);
            }
        }

        private static string Text(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Склад в базе данных.</summary>
    internal sealed class SqlWarehouse : IWarehouseStore
    {
        private readonly PgConnectionInfo _info;

        public SqlWarehouse(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
        }

        public string Title { get { return "Склад в базе данных: " + _info.Describe(); } }

        private PgClient Open()
        {
            PgClient client = new PgClient(_info);
            client.Connect();
            return client;
        }

        /// <summary>Создание таблицы движений по складу.</summary>
        public static string CreateSchema(PgClient client)
        {
            string command =
                @"CREATE TABLE IF NOT EXISTS smeta_stockmoves (
                    id serial PRIMARY KEY,
                    article text NOT NULL DEFAULT '',
                    name text NOT NULL DEFAULT '',
                    quantity numeric(12,3) NOT NULL DEFAULT 0,
                    cost numeric(12,2) NOT NULL DEFAULT 0,
                    saved timestamp NOT NULL DEFAULT now(),
                    number text NOT NULL DEFAULT '',
                    note text NOT NULL DEFAULT '')";

            return client.Execute(command) ? null : client.LastError;
        }

        public Warehouse Load()
        {
            Warehouse warehouse = new Warehouse();

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                List<PgRow> rows = client.Query(
                    "SELECT article, name, quantity, cost, to_char(saved, 'YYYY-MM-DD HH24:MI:SS') AS saved_at, " +
                    "number, note FROM smeta_stockmoves ORDER BY id");
                if (rows == null) throw new PgException(client.LastError);

                foreach (PgRow row in rows)
                {
                    StockMove move = new StockMove();
                    move.Article = Text(row["article"]);
                    move.Name = Text(row["name"]);
                    move.Quantity = Number(row["quantity"]);
                    move.Cost = Number(row["cost"]);
                    move.Number = Text(row["number"]);
                    move.Note = Text(row["note"]);

                    DateTime saved;
                    if (DateTime.TryParse(Text(row["saved_at"]), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out saved))
                        move.Saved = saved;

                    if (move.Quantity == 0m) continue;
                    warehouse.Moves.Add(move);
                }
            }

            return warehouse;
        }

        public void Save(Warehouse warehouse)
        {
            if (warehouse == null) return;

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                if (!client.Execute("BEGIN")) throw new PgException(client.LastError ?? "Не удалось начать запись.");

                // движения — только добавление: прошлые записи не меняются
                if (!client.Execute("DELETE FROM smeta_stockmoves"))
                {
                    client.Execute("ROLLBACK");
                    throw new PgException(client.LastError);
                }

                foreach (StockMove move in warehouse.Moves)
                {
                    bool ok = client.Execute(
                        "INSERT INTO smeta_stockmoves (article, name, quantity, cost, saved, number, note) " +
                        "VALUES ($1, $2, $3, $4, $5, $6, $7)",
                        move.Article ?? "", move.Name ?? "", move.Quantity, move.Cost,
                        move.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        move.Number ?? "", move.Note ?? "");

                    if (!ok) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }
                }

                if (!client.Execute("COMMIT")) throw new PgException(client.LastError);
            }
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
