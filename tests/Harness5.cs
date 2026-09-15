// Проверки клиента PostgreSQL: подключение, авторизация, запросы.
using System;
using System.Collections.Generic;
using System.Globalization;
using KotovCalc;

internal static class Harness5
{
    private static int _failed;

    private static void Check(string what, object expected, object actual)
    {
        bool ok = Equals(expected, actual);
        if (!ok) _failed++;
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what +
            (ok ? "" : "   ожидалось=<" + expected + "> получено=<" + actual + ">"));
    }

    private static void CheckTrue(string what, bool value, string details)
    {
        if (!value) _failed++;
        Console.WriteLine((value ? "  PASS  " : "  FAIL  ") + what + (value ? "" : "   " + details));
    }

    private static int _step;

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("[" + (++_step) + "] " + title);
    }

    private static string Text(object value)
    {
        return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static decimal Number(object value)
    {
        if (value == null) return 0m;
        return decimal.Parse(Convert.ToString(value, CultureInfo.InvariantCulture).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture);
    }

    private static void Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        string database = args.Length > 1 ? args[1] : "smeta";
        string user = args.Length > 2 ? args[2] : "smeta";
        string password = args.Length > 3 ? args[3] : "smeta";

        try
        {
            Section("Разбор строки подключения");

            PgConnectionInfo uri = PgConnectionInfo.Parse("postgresql://smeta:пароль@db.example.com:5433/smeta");
            Check("адрес из ссылки", "db.example.com", uri.Host);
            Check("порт из ссылки", 5433, uri.Port);
            Check("база из ссылки", "smeta", uri.Database);
            Check("пользователь из ссылки", "smeta", uri.User);
            Check("пароль из ссылки", "пароль", uri.Password);

            PgConnectionInfo keys = PgConnectionInfo.Parse("host=10.0.0.5 port=5432 dbname=sklad user=ivan password=секрет");
            Check("адрес из строки ключей", "10.0.0.5", keys.Host);
            Check("база из строки ключей", "sklad", keys.Database);
            Check("пользователь из строки ключей", "ivan", keys.User);
            Check("пароль из строки ключей", "секрет", keys.Password);

            PgConnectionInfo brief = PgConnectionInfo.Parse("smeta:smeta@127.0.0.1:5432/smeta");
            Check("адрес из краткой записи", "127.0.0.1", brief.Host);
            Check("порт из краткой записи", 5432, brief.Port);
            Check("база из краткой записи", "smeta", brief.Database);

            PgConnectionInfo plain = PgConnectionInfo.Parse("192.168.1.10");
            Check("только адрес", "192.168.1.10", plain.Host);
            Check("порт по умолчанию", 5432, plain.Port);

            CheckTrue("описание без пароля", !brief.Describe().Contains("smeta:smeta"), brief.Describe());

            Section("Подключение и авторизация");

            PgConnectionInfo info = new PgConnectionInfo(host, 5432, database, user, password);
            using (PgClient client = new PgClient(info))
            {
                client.Connect();
                CheckTrue("подключение выполнено", true, "исключение");
                CheckTrue("версия сервера получена", !string.IsNullOrEmpty(client.ParameterStatus),
                    "нет параметра server_version");
                Console.WriteLine("        сервер: PostgreSQL " + client.ParameterStatus);

                Section("Простые запросы");

                List<PgRow> rows = client.Query("SELECT 1 AS one, 'привет' AS hello, true AS flag");
                CheckTrue("запрос выполнен", rows != null, client.LastError);
                Check("строк в ответе", 1, rows.Count);
                Check("число", "1", Text(rows[0]["one"]));
                Check("строка с кириллицей", "привет", Text(rows[0]["hello"]));
                Check("логическое значение", "t", Text(rows[0]["flag"]));

                Check("скалярный запрос", "42", Text(client.Scalar("SELECT 42")));
                Check("тип числа — integer", "integer", Text(client.Scalar("SELECT pg_typeof(1)")));
                Check("тип строки — text", "text", Text(client.Scalar("SELECT pg_typeof('текст'::text)")));

                Section("Параметры и экранирование");

                Check("подстановка числа", "7", Text(client.Scalar("SELECT $1::int + 2", 5)));
                Check("подстановка строки", "ор'дер", Text(client.Scalar("SELECT $1::text", "ор'дер")));
                Check("кавычка не ломает запрос", "о'к", Text(client.Scalar("SELECT $1::text AS value", "о'к")));
                Check("проверка результата с кавычкой", "о'к!", Text(client.Scalar("SELECT $1::text || '!'", "о'к")));
                Check("обратный слэш", "C:\\temp", Text(client.Scalar("SELECT $1::text", "C:\\temp")));
                Check("подстановка null", null, Text(client.Scalar("SELECT $1::text", null)));
                Check("десятичное число", "1500.75", Text(client.Scalar("SELECT $1::numeric AS value", 1500.75m)));
                Check("две кириллические строки", "Услуга — Компьютерная диагностика", Text(client.Scalar("SELECT ($1::text || ' — ' || $2::text) AS value", "Услуга", "Компьютерная диагностика")));

                Section("SQL-инъекция не проходит");

                string attack = "x'); DROP TABLE smeta_probe; --";
                CheckTrue("вредоносная строка не выполняется как команда",
                    client.Query("SELECT $1::text AS value", attack) != null, client.LastError);
                Check("таблица не пострадала", null, Text(client.Scalar("SELECT to_regclass('smeta_probe')::text AS value")));

                Section("Создание таблиц приложения");

                client.Execute("DROP TABLE IF EXISTS smeta_prices");
                client.Execute("DROP TABLE IF EXISTS smeta_settings");
                client.Execute("DROP TABLE IF EXISTS smeta_templates");
                CheckTrue("таблица удалена, если была", true, "ошибка");

                CheckTrue("таблица прайса создана",
                    client.Execute(@"CREATE TABLE smeta_prices (
                        id serial PRIMARY KEY,
                        grp text NOT NULL,
                        article text NOT NULL DEFAULT '',
                        name text NOT NULL,
                        unit text NOT NULL DEFAULT 'шт.',
                        price numeric(12,2) NOT NULL DEFAULT 0,
                        sort_order int NOT NULL DEFAULT 0)"), client.LastError);

                CheckTrue("таблица настроек создана",
                    client.Execute(@"CREATE TABLE smeta_settings (
                        name text PRIMARY KEY,
                        value text NOT NULL DEFAULT '')"), client.LastError);

                CheckTrue("таблица наборов создана",
                    client.Execute(@"CREATE TABLE smeta_templates (
                        name text PRIMARY KEY,
                        saved timestamp NOT NULL DEFAULT now(),
                        items jsonb NOT NULL DEFAULT '[]'::jsonb)"), client.LastError);

                Section("Запись и чтение данных");

                CheckTrue("позиция добавлена",
                    client.Execute("INSERT INTO smeta_prices (grp, article, name, unit, price, sort_order) " +
                                   "VALUES ($1, $2, $3, $4, $5, $6)",
                        "Диагностика", "ART-101", "Компьютерная диагностика двигателя", "услуга", 1500m, 1),
                    client.LastError);

                CheckTrue("вторая позиция добавлена",
                    client.Execute("INSERT INTO smeta_prices (grp, article, name, unit, price, sort_order) " +
                                   "VALUES ($1, $2, $3, $4, $5, $6)",
                        "Диагностика", "ART-102", "Диагностика ходовой части", "услуга", 900m, 2),
                    client.LastError);

                CheckTrue("настройка записана",
                    client.Execute("INSERT INTO smeta_settings (name, value) VALUES ($1, $2)", "number", "12/2026"),
                    client.LastError);

                CheckTrue("набор записан",
                    client.Execute("INSERT INTO smeta_templates (name, items) VALUES ($1, $2::jsonb)",
                        "ТО-1", "[{\"name\":\"Замена масла\",\"quantity\":1}]"),
                    client.LastError);

                Check("позиций в базе", 2L, Convert.ToInt64(client.Scalar("SELECT count(*) FROM smeta_prices")));

                object price = client.Scalar("SELECT price FROM smeta_prices WHERE article = 'ART-101'");
                Check("цена сохранена", "1500.00", Text(price));

                List<PgRow> list = client.Query(
                    "SELECT grp, article, name, unit, price FROM smeta_prices ORDER BY sort_order");
                Check("порядок позиций сохранён", 2, list.Count);
                Check("первая позиция", "Компьютерная диагностика двигателя", Text(list[0]["name"]));
                Check("вторая позиция", "Диагностика ходовой части", Text(list[1]["name"]));
                Check("единица измерения", "услуга", Text(list[0]["unit"]));
                Check("число как decimal", 1500.00m, Number(list[0]["price"]));

                Check("настройка прочитана", "12/2026", Text(client.Scalar(
                    "SELECT value FROM smeta_settings WHERE name = 'number'")));

                CheckTrue("набор прочитан как jsonb", Text(client.Scalar(
                    "SELECT items->0->>'name' FROM smeta_templates WHERE name = 'ТО-1'")) == "Замена масла",
                    "неверный json");

                Section("Обновление и удаление");

                CheckTrue("цена обновлена",
                    client.Execute("UPDATE smeta_prices SET price = $1 WHERE article = $2", 1800m, "ART-101"),
                    client.LastError);
                Check("новая цена", "1800.00", Text(client.Scalar(
                    "SELECT price FROM smeta_prices WHERE article = 'ART-101'")));

                CheckTrue("позиция удалена",
                    client.Execute("DELETE FROM smeta_prices WHERE article = $1", "ART-102"), client.LastError);
                Check("осталась одна позиция", 1L, Convert.ToInt64(client.Scalar("SELECT count(*) FROM smeta_prices")));

                Section("Ошибки базы данных");

                client.Query("SELECT * FROM нет_такой_таблицы");
                CheckTrue("текст ошибки получен", !string.IsNullOrEmpty(client.LastError), "нет текста");
                Check("код ошибки — нет таблицы", "42P01", client.LastSqlState);
                Console.WriteLine("        сообщение: " + client.LastError);

                client.Query("INSERT INTO smeta_prices (grp, name) VALUES ($1, $2) ON CONFLICT DO NOTHING",
                    "Тест", "Дубль");
                CheckTrue("повторный запрос после ошибки работает",
                    client.Query("SELECT 1 AS ok") != null, client.LastError);
                client.Execute("DELETE FROM smeta_prices WHERE grp = 'Тест'");

                Section("Неверный пароль");

                using (PgClient wrong = new PgClient(new PgConnectionInfo(host, 5432, database, user, "неверный")))
                {
                    bool refused = false;
                    string message = null;
                    try { wrong.Connect(); }
                    catch (PgException ex) { refused = true; message = ex.Message; }

                    CheckTrue("подключение отклонено", refused, "подключение прошло");
                    CheckTrue("сообщение понятное", message != null && message.Length > 5, message);
                    Console.WriteLine("        сообщение: " + message);
                }

                Section("Недоступный сервер");

                using (PgClient dead = new PgClient(new PgConnectionInfo("127.0.0.1", 5999, database, user, password)))
                {
                    bool failed = false;
                    string message = null;
                    try { dead.Connect(); }
                    catch (PgException ex) { failed = true; message = ex.Message; }

                    CheckTrue("соединение не установлено", failed, "соединение прошло");
                    CheckTrue("сообщение содержит адрес", message != null && message.Contains("5999"), message);
                }

                Section("Уборка");

                client.Execute("DROP TABLE IF EXISTS smeta_prices");
                client.Execute("DROP TABLE IF EXISTS smeta_settings");
                client.Execute("DROP TABLE IF EXISTS smeta_templates");
                Check("таблицы удалены", null, Text(client.Scalar("SELECT to_regclass('smeta_prices')::text AS value")));
            }

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ КЛИЕНТА POSTGRESQL ПРОЙДЕНЫ"); Environment.Exit(0); }
            Console.WriteLine("ПРОВАЛЕНО: " + _failed);
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ОШИБКА: " + ex.GetType().Name + ": " + ex.Message);
            Environment.Exit(2);
        }
    }
}
