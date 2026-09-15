// ---------------------------------------------------------------------------
//  Кассы и оплаты заявок в PostgreSQL.
//
//  Таблицы: smeta_cashdesks — справочник касс, smeta_payments — оплаты,
//  smeta_cashops — ручные пополнения и изъятия. Создаются автоматически.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace KotovCalc
{
    internal sealed class SqlCashBook : ICashBookStore
    {
        private readonly PgConnectionInfo _info;

        public SqlCashBook(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
        }

        public string Title { get { return "Кассы в базе данных: " + _info.Describe(); } }

        private PgClient Open()
        {
            PgClient client = new PgClient(_info);
            client.Connect();
            return client;
        }

        /// <summary>Создание таблиц касс и оплат.</summary>
        public static string CreateSchema(PgClient client)
        {
            string[] commands = new string[]
            {
                @"CREATE TABLE IF NOT EXISTS smeta_cashdesks (
                    name text PRIMARY KEY,
                    note text NOT NULL DEFAULT '',
                    archive boolean NOT NULL DEFAULT false,
                    created timestamp NOT NULL DEFAULT now())",

                @"CREATE TABLE IF NOT EXISTS smeta_payments (
                    number text PRIMARY KEY,
                    saved timestamp NOT NULL DEFAULT now(),
                    customer text NOT NULL DEFAULT '',
                    desk text NOT NULL DEFAULT '',
                    kind text NOT NULL DEFAULT 'наличные',
                    cash numeric(12,2) NOT NULL DEFAULT 0,
                    cashless numeric(12,2) NOT NULL DEFAULT 0,
                    due numeric(12,2) NOT NULL DEFAULT 0,
                    note text NOT NULL DEFAULT '')",

                @"CREATE TABLE IF NOT EXISTS smeta_cashops (
                    id serial PRIMARY KEY,
                    desk text NOT NULL DEFAULT '',
                    saved timestamp NOT NULL DEFAULT now(),
                    amount numeric(12,2) NOT NULL DEFAULT 0,
                    note text NOT NULL DEFAULT '')"
            };

            foreach (string command in commands)
                if (!client.Execute(command)) return client.LastError;

            return null;
        }

        public CashBook Load()
        {
            CashBook book = new CashBook();

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                List<PgRow> desks = client.Query("SELECT name, note, archive FROM smeta_cashdesks ORDER BY name");
                if (desks == null) throw new PgException(client.LastError);

                foreach (PgRow row in desks)
                {
                    CashDesk desk = new CashDesk();
                    desk.Name = Text(row["name"]);
                    desk.Note = Text(row["note"]);
                    desk.Archive = Text(row["archive"]) == "t" || Text(row["archive"]) == "True";
                    if (desk.Name.Length == 0) continue;
                    book.Desks.Add(desk);
                }

                List<PgRow> payments = client.Query(
                    "SELECT number, to_char(saved, 'YYYY-MM-DD HH24:MI:SS') AS saved_at, customer, desk, kind, " +
                    "cash, cashless, due, note FROM smeta_payments ORDER BY saved DESC");
                if (payments == null) throw new PgException(client.LastError);

                foreach (PgRow row in payments)
                {
                    Payment payment = new Payment();
                    payment.Number = Text(row["number"]);
                    payment.Customer = Text(row["customer"]);
                    payment.Desk = Text(row["desk"]);
                    payment.Kind = PaymentKinds.Parse(Text(row["kind"]));
                    payment.Cash = Number(row["cash"]);
                    payment.Cashless = Number(row["cashless"]);
                    payment.Due = Number(row["due"]);
                    payment.Note = Text(row["note"]);

                    DateTime saved;
                    if (DateTime.TryParse(Text(row["saved_at"]), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out saved))
                        payment.Saved = saved;

                    if (payment.Number.Length > 0) book.Payments.Add(payment);
                }

                List<PgRow> operations = client.Query(
                    "SELECT desk, to_char(saved, 'YYYY-MM-DD HH24:MI:SS') AS saved_at, amount, note " +
                    "FROM smeta_cashops ORDER BY saved");
                if (operations == null) throw new PgException(client.LastError);

                foreach (PgRow row in operations)
                {
                    DeskOperation operation = new DeskOperation();
                    operation.Desk = Text(row["desk"]);
                    operation.Amount = Number(row["amount"]);
                    operation.Note = Text(row["note"]);

                    DateTime saved;
                    if (DateTime.TryParse(Text(row["saved_at"]), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out saved))
                        operation.Saved = saved;

                    book.Operations.Add(operation);
                }
            }

            // кассы, которые упомянуты только в оплатах
            foreach (Payment payment in book.Payments)
                if (payment.Desk.Length > 0 && book.Find(payment.Desk) == null)
                    book.Ensure(payment.Desk);

            return book;
        }

        public void Save(CashBook book)
        {
            if (book == null) return;

            using (PgClient client = Open())
            {
                string error = CreateSchema(client);
                if (error != null) throw new PgException(error);

                if (!client.Execute("BEGIN")) throw new PgException(client.LastError ?? "Не удалось начать запись.");

                // кассы
                foreach (CashDesk desk in book.Desks)
                {
                    bool ok = client.Execute(
                        @"INSERT INTO smeta_cashdesks (name, note, archive) VALUES ($1, $2, $3)
                          ON CONFLICT (name) DO UPDATE SET note = EXCLUDED.note, archive = EXCLUDED.archive",
                        desk.Name, desk.Note ?? "", desk.Archive);

                    if (!ok) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }
                }

                // оплаты
                List<string> keep = new List<string>();
                foreach (Payment payment in book.Payments)
                {
                    keep.Add(payment.Number);

                    bool ok = client.Execute(
                        @"INSERT INTO smeta_payments (number, saved, customer, desk, kind, cash, cashless, due, note)
                          VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                          ON CONFLICT (number) DO UPDATE SET
                            saved = EXCLUDED.saved, customer = EXCLUDED.customer, desk = EXCLUDED.desk,
                            kind = EXCLUDED.kind, cash = EXCLUDED.cash, cashless = EXCLUDED.cashless,
                            due = EXCLUDED.due, note = EXCLUDED.note",
                        payment.Number, payment.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        payment.Customer ?? "", payment.Desk ?? "", PaymentKinds.Title(payment.Kind),
                        payment.Cash, payment.Cashless, payment.Due, payment.Note ?? "");

                    if (!ok) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }
                }

                // оплаты, которых больше нет в списке, убираем
                if (keep.Count == 0)
                {
                    if (!client.Execute("DELETE FROM smeta_payments")) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }
                }
                else
                {
                    System.Text.StringBuilder list = new System.Text.StringBuilder();
                    foreach (string number in keep)
                    {
                        if (list.Length > 0) list.Append(", ");
                        list.Append("'").Append(number.Replace("'", "''")).Append("'");
                    }

                    if (!client.Execute("DELETE FROM smeta_payments WHERE number NOT IN (" + list + ")"))
                    {
                        client.Execute("ROLLBACK");
                        throw new PgException(client.LastError);
                    }
                }

                // ручные операции: заменяем целиком
                if (!client.Execute("DELETE FROM smeta_cashops")) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }

                foreach (DeskOperation operation in book.Operations)
                {
                    bool ok = client.Execute(
                        "INSERT INTO smeta_cashops (desk, saved, amount, note) VALUES ($1, $2, $3, $4)",
                        operation.Desk ?? "",
                        operation.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        operation.Amount, operation.Note ?? "");

                    if (!ok) { client.Execute("ROLLBACK"); throw new PgException(client.LastError); }
                }

                if (!client.Execute("COMMIT")) throw new PgException(client.LastError);
            }
        }

        public bool Delete(Payment payment)
        {
            if (payment == null) return false;

            using (PgClient client = Open())
            {
                bool ok = client.Execute("DELETE FROM smeta_payments WHERE number = $1", payment.Number);
                if (!ok) throw new PgException(client.LastError ?? "Не удалось удалить оплату.");
                return true;
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
