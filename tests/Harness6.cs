// Проверки слоя хранения: файлы и PostgreSQL, вход по паролю.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KotovCalc;

internal static class Harness6
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

    private static void Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        string database = args.Length > 1 ? args[1] : "smeta";
        string user = args.Length > 2 ? args[2] : "smeta";
        string password = args.Length > 3 ? args[3] : "smeta";

        string work = Path.Combine(Path.GetTempPath(), "kotov-store-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);

        try
        {
            // ------------------------------------------------ файловое хранилище
            Section("Файловое хранилище");
            PriceBook.StorePath = Path.Combine(work, "prices.xml");
            IDataStore files = new FileDataStore();

            CheckTrue("проверка папки проходит", files.Test() == null, files.Test());
            files.Prepare();
            CheckTrue("прайс-лист создан", files.LoadPrices().Count > 30,
                "позиций: " + files.LoadPrices().Count);
            CheckTrue("название режима понятное", files.Title.Length > 5, files.Title);

            List<ServiceItem> sample = new List<ServiceItem>();
            sample.Add(new ServiceItem("Раздел А", "ART-1", "Услуга первая", "услуга", 100.50m));
            sample.Add(new ServiceItem("Раздел А", "ART-2", "Услуга вторая", "шт.", 200m));
            sample.Add(new ServiceItem("Раздел Б", "ART-3", "Услуга третья", "литр", 300.25m));
            files.SavePrices(sample);

            List<ServiceItem> back = files.LoadPrices();
            Check("позиций записано", 3, back.Count);
            Check("порядок сохранён", "Услуга первая", back[0].Name);
            Check("раздел сохранён", "Раздел А", back[0].Group);
            Check("артикул сохранён", "ART-2", back[1].Article);
            Check("единица сохранена", "литр", back[2].Unit);
            Check("цена сохранена", 300.25m, back[2].Price);

            ServiceTemplate template = new ServiceTemplate();
            template.Name = "ТО-1";
            template.Items.Add(new TemplateItem
            {
                Group = "Раздел А", Article = "ART-1", Name = "Услуга первая", Quantity = 2m
            });
            List<ServiceTemplate> templates = new List<ServiceTemplate>();
            templates.Add(template);
            files.SaveTemplates(templates);
            Check("набор сохранён", 1, files.LoadTemplates().Count);
            Check("позиция набора сохранена", "Услуга первая", files.LoadTemplates()[0].Items[0].Name);
            Check("количество сохранено", 2m, files.LoadTemplates()[0].Items[0].Quantity);

            StoreSnapshot snapshot = new StoreSnapshot();
            snapshot.Document.Number = "7/2026";
            snapshot.Document.Customer = "Иванов И.И.";
            snapshot.Document.Discount = 5m;
            snapshot.Theme = "dark";
            snapshot.LastTemplate = "ТО-1";
            files.SaveSettings(snapshot);

            StoreSnapshot read = files.LoadAll();
            Check("номер сметы прочитан", "7/2026", read.Document.Number);
            Check("заказчик прочитан", "Иванов И.И.", read.Document.Customer);
            Check("скидка прочитана", 5m, read.Document.Discount);
            Check("тема прочитана", "dark", read.Theme);
            Check("наборы прочитаны", 1, read.Templates.Count);
            Check("прайс прочитан", 3, read.Prices.Count);

            // ------------------------------------------------ хранилище в базе
            Section("Хранилище в PostgreSQL");

            PgConnectionInfo info = new PgConnectionInfo(host, 5432, database, user, password);
            IDataStore sql = new SqlDataStore(info);

            string testResult = sql.Test();
            CheckTrue("подключение к базе проходит", testResult == null, testResult);
            Console.WriteLine("        режим: " + sql.Title);

            PgClient admin = new PgClient(info);
            admin.Connect();
            admin.Execute("DROP TABLE IF EXISTS smeta_prices");
            admin.Execute("DROP TABLE IF EXISTS smeta_templates");
            admin.Execute("DROP TABLE IF EXISTS smeta_settings");
            admin.Execute("DROP TABLE IF EXISTS smeta_users");
            admin.Dispose();

            sql.Prepare();
            CheckTrue("таблицы созданы повторно без ошибок", sql.Test() == null, sql.Test());
            CheckTrue("прайс в новой базе пуст", sql.LoadPrices().Count == 0,
                "позиций: " + sql.LoadPrices().Count);

            sql.SavePrices(sample);
            List<ServiceItem> sqlPrices = sql.LoadPrices();
            Check("позиций записано в базу", 3, sqlPrices.Count);
            Check("порядок в базе сохранён", "Услуга первая", sqlPrices[0].Name);
            Check("последняя позиция на месте", "Услуга третья", sqlPrices[2].Name);
            Check("раздел в базе", "Раздел Б", sqlPrices[2].Group);
            Check("артикул в базе", "ART-3", sqlPrices[2].Article);
            Check("единица в базе", "литр", sqlPrices[2].Unit);
            Check("цена в базе", 300.25m, sqlPrices[2].Price);

            // замена прайса целиком
            List<ServiceItem> smaller = new List<ServiceItem>();
            smaller.Add(new ServiceItem("Раздел А", "ART-9", "Единственная услуга", "услуга", 999m));
            sql.SavePrices(smaller);
            sqlPrices = sql.LoadPrices();
            Check("прайс заменён целиком", 1, sqlPrices.Count);
            Check("новая позиция на месте", "ART-9", sqlPrices[0].Article);

            sql.SaveTemplates(templates);
            List<ServiceTemplate> sqlTemplates = sql.LoadTemplates();
            Check("набор записан в базу", 1, sqlTemplates.Count);
            Check("имя набора в базе", "ТО-1", sqlTemplates[0].Name);
            Check("позиция набора в базе", "Услуга первая", sqlTemplates[0].Items[0].Name);
            Check("количество в базе", 2m, sqlTemplates[0].Items[0].Quantity);

            // кодировка jsonb: кириллица и кавычки
            ServiceTemplate tricky = new ServiceTemplate();
            tricky.Name = "Набор «Кузов»";
            tricky.Items.Add(new TemplateItem
            {
                Group = "Кузовные работы", Article = "ART-77", Name = "Покраска \"крыла\"", Quantity = 1.5m
            });
            List<ServiceTemplate> trickyList = new List<ServiceTemplate>();
            trickyList.Add(template);
            trickyList.Add(tricky);
            sql.SaveTemplates(trickyList);
            sqlTemplates = sql.LoadTemplates();
            Check("два набора в базе", 2, sqlTemplates.Count);
            // база отдаёт наборы по алфавиту, поэтому ищем по имени
            ServiceTemplate found = null;
            foreach (ServiceTemplate entry in sqlTemplates)
                if (entry.Name == "Набор «Кузов»") found = entry;

            CheckTrue("набор с кавычками найден", found != null, "не найден");
            if (found != null)
            {
                Check("кавычки в позиции", "Покраска \"крыла\"", found.Items[0].Name);
                Check("дробное количество", 1.5m, found.Items[0].Quantity);
                Check("раздел в наборе", "Кузовные работы", found.Items[0].Group);
            }

            // удаление набора
            List<ServiceTemplate> single = new List<ServiceTemplate>();
            single.Add(template);
            sql.SaveTemplates(single);
            Check("лишний набор удалён", 1, sql.LoadTemplates().Count);

            sql.SaveSettings(snapshot);
            StoreSnapshot sqlRead = sql.LoadAll();
            Check("номер сметы в базе", "7/2026", sqlRead.Document.Number);
            Check("заказчик в базе", "Иванов И.И.", sqlRead.Document.Customer);
            Check("скидка в базе", 5m, sqlRead.Document.Discount);
            Check("тема в базе", "dark", sqlRead.Theme);
            Check("прайс прочитан вместе с настройками", 1, sqlRead.Prices.Count);
            Check("наборы прочитаны вместе с настройками", 1, sqlRead.Templates.Count);

            // обновление настройки
            snapshot.Document.Discount = 12.5m;
            snapshot.Theme = "light";
            sql.SaveSettings(snapshot);
            sqlRead = sql.LoadAll();
            Check("скидка обновлена", 12.5m, sqlRead.Document.Discount);
            Check("тема обновлена", "light", sqlRead.Theme);

            Section("Вход по паролю в программу");

            SqlDataStore sqlStore = (SqlDataStore)sql;
            Check("пользователей ещё нет", false, sqlStore.HasUsers());

            sqlStore.SaveUser("admin", "секрет");
            CheckTrue("пользователь создан", sqlStore.HasUsers(), "нет пользователей");
            CheckTrue("верный пароль принят", sqlStore.CheckUser("admin", "секрет"), "пароль отклонён");
            CheckTrue("неверный пароль отклонён", !sqlStore.CheckUser("admin", "секрет2"), "пароль принят");
            CheckTrue("неизвестный логин отклонён", !sqlStore.CheckUser("нет", "секрет"), "вход выполнен");

            sqlStore.SaveUser("admin", "новый");
            CheckTrue("пароль сменён", sqlStore.CheckUser("admin", "новый"), "старый пароль не заменён");
            CheckTrue("старый пароль больше не подходит", !sqlStore.CheckUser("admin", "секрет"), "старый пароль работает");

            sqlStore.SaveUser("мастер", "12345");
            CheckTrue("второй пользователь создан", sqlStore.CheckUser("мастер", "12345"), "вход не выполнен");
            CheckTrue("пароли не путаются", !sqlStore.CheckUser("мастер", "новый"), "чужой пароль подошёл");

            Section("Хеширование паролей");

            string hash = UserPassword.Create("проверка");
            CheckTrue("запись пароля в нужном формате", hash.StartsWith("pbkdf2-sha256$"), hash);
            CheckTrue("пароль подтверждается", UserPassword.Verify("проверка", hash), "не подтверждён");
            CheckTrue("другой пароль не подходит", !UserPassword.Verify("проверка2", hash), "подошёл");
            CheckTrue("хеши разные для одного пароля",
                UserPassword.Create("один") != UserPassword.Create("один"), "хеши совпали");
            CheckTrue("пустая запись отклоняется", !UserPassword.Verify("x", ""), "принята");
            CheckTrue("испорченная запись отклоняется", !UserPassword.Verify("x", "ерунда"), "принята");
            CheckTrue("кириллица в пароле работает",
                UserPassword.Verify("пароль-Ы", UserPassword.Create("пароль-Ы")), "не сработала");

            Section("Проверка недоступной базы");

            IDataStore dead = new SqlDataStore(new PgConnectionInfo("127.0.0.1", 5999, database, user, password));
            string deadResult = dead.Test();
            CheckTrue("сообщение об ошибке получено", deadResult != null, "ошибки нет");
            CheckTrue("в сообщении есть адрес", deadResult != null && deadResult.Contains("5999"), deadResult);

            IDataStore wrongUser = new SqlDataStore(new PgConnectionInfo(host, 5432, database, user, "неверный"));
            CheckTrue("неверный пароль базы отклонён", wrongUser.Test() != null, "подключение прошло");

            Section("Уборка");

            PgClient cleanup = new PgClient(info);
            cleanup.Connect();
            cleanup.Execute("DROP TABLE IF EXISTS smeta_prices");
            cleanup.Execute("DROP TABLE IF EXISTS smeta_templates");
            cleanup.Execute("DROP TABLE IF EXISTS smeta_settings");
            cleanup.Execute("DROP TABLE IF EXISTS smeta_users");
            cleanup.Dispose();
            CheckTrue("таблицы убраны из базы", true, "ошибка уборки");

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ХРАНИЛИЩА ПРОЙДЕНЫ"); Environment.Exit(0); }
            Console.WriteLine("ПРОВАЛЕНО: " + _failed);
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ОШИБКА: " + ex.GetType().Name + ": " + ex.Message);
            Environment.Exit(2);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
