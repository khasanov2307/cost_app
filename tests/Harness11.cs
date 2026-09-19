// Проверки полной выгрузки и загрузки данных.
//
//  Проверяется главное: данные, выгруженные из одного хранилища и загруженные
//  в пустое, дают тот же отпечаток. Отпечаток считается по каталогу, наборам,
//  реквизитам, заявкам, заказчикам, складу и кассам.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KotovCalc;

internal static class Harness11
{
    private static int _failed;
    private static int _step;

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

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("[" + (++_step) + "] " + title);
    }

    /// <summary>Отпечаток всех данных: если он совпал, данные перенесены верно.</summary>
    /// <summary>Число в одном виде: база хранит numeric, файлы — без лишних нулей.</summary>
    private static string N(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Fingerprint()
    {
        StringBuilder text = new StringBuilder();

        // каталог
        List<ServiceItem> prices = ConnectionSettings.Store.LoadPrices();
        text.Append("каталог:").Append(prices.Count).Append('|');

        foreach (ServiceItem item in prices)
            text.Append(item.Group).Append('/').Append(item.Article).Append('/').Append(item.Name)
                .Append('/').Append(Uom.Normalize(item.Unit)).Append('/').Append(N(item.Price))
                .Append('/').Append(N(item.Cost)).Append('/').Append(N(item.MinStock)).Append(';');

        // наборы
        List<ServiceTemplate> templates = ConnectionSettings.Store.LoadTemplates();
        text.Append("наборы:").Append(templates.Count).Append('|');

        foreach (ServiceTemplate template in templates)
        {
            text.Append(template.Name).Append('{');

            foreach (TemplateItem item in template.Items)
                text.Append(item.Group).Append('/').Append(item.Article).Append('/')
                    .Append(item.Name).Append('/').Append(N(item.Quantity)).Append(';');

            text.Append('}');
        }

        // реквизиты и тема
        StoreSnapshot snapshot = ConnectionSettings.Store.LoadAll();
        text.Append("реквизиты:").Append(snapshot.Document.Number).Append('/')
            .Append(snapshot.Document.Customer).Append('/').Append(snapshot.Document.CustomerPhone)
            .Append('/').Append(snapshot.Document.Car).Append('/').Append(snapshot.Document.Plate)
            .Append('/').Append(N(snapshot.Document.Discount)).Append('/')
            .Append(snapshot.Theme).Append('/').Append(snapshot.Logo).Append('/')
            .Append(snapshot.LastTemplate).Append('|');

        // заявки
        List<SavedEstimate> estimates = ConnectionSettings.Archive.Load();
        text.Append("заявки:").Append(estimates.Count).Append('|');

        foreach (SavedEstimate estimate in estimates)
        {
            text.Append(estimate.Number).Append('/').Append(estimate.Year).Append('/')
                .Append(estimate.Sequence).Append('/')
                .Append(estimate.Saved.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('/')
                .Append(estimate.CustomerId.Length > 0 ? "есть" : "нет").Append('/')
                .Append(estimate.CustomerPhone).Append('/').Append(estimate.Car).Append('/')
                .Append(estimate.Plate).Append('/').Append(EstimateStatuses.Title(estimate.Status))
                .Append('/').Append(N(estimate.Discount)).Append('/').Append(N(estimate.Total)).Append('{');

            foreach (EstimateItem item in estimate.Items)
                text.Append(item.Group).Append('/').Append(item.Article).Append('/')
                    .Append(item.Name).Append('/').Append(item.Unit).Append('/')
                    .Append(N(item.Quantity)).Append('/').Append(N(item.Price)).Append('/')
                    .Append(N(item.Cost)).Append(';');

            text.Append('}');
        }

        // заказчики
        // телефон заказчика программа хранит в едином виде, поэтому в отпечаток
        // попадает уже приведённое значение: это и есть ожидаемое состояние
        CustomerBook customers = ConnectionSettings.Customers.Load();
        text.Append("заказчики:").Append(customers.Customers.Count).Append('|');

        foreach (Customer customer in customers.Customers)
            // идентификатор карточки при загрузке выдаётся заново: сравниваем данные
            text.Append(customer.Name).Append('/')
                .Append(customer.Phone).Append('/').Append(customer.Car).Append('/')
                .Append(customer.Plate).Append('/').Append(customer.Note).Append(';');

        // склад
        Warehouse warehouse = ConnectionSettings.Warehouse.Load();
        text.Append("склад:").Append(warehouse.Moves.Count).Append('|');

        foreach (StockMove move in warehouse.Moves)
            text.Append(move.Article).Append('/').Append(move.Name).Append('/')
                .Append(N(move.Quantity)).Append('/').Append(N(move.Cost)).Append('/')
                .Append(move.Saved.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('/')
                .Append(move.Number).Append('/').Append(move.Note).Append(';');

        // кассы и оплаты
        CashBook cash = ConnectionSettings.CashBook.Load();
        text.Append("кассы:").Append(cash.Desks.Count).Append('|');

        foreach (CashDesk desk in cash.Desks)
            text.Append(desk.Name).Append('/').Append(desk.Note).Append('/')
                .Append(desk.Archive).Append(';');

        text.Append("оплаты:").Append(cash.Payments.Count).Append('|');

        foreach (Payment payment in cash.Payments)
            text.Append(payment.Number).Append('/').Append(payment.Desk).Append('/')
                .Append(payment.CashDesk).Append('/').Append(payment.Kind).Append('/')
                .Append(N(payment.Cash)).Append('/').Append(N(payment.Cashless)).Append('/')
                .Append(N(payment.Due)).Append('/')
                .Append(payment.Saved.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(';');

        text.Append("операции:").Append(cash.Operations.Count).Append('|');

        foreach (DeskOperation operation in cash.Operations)
            text.Append(operation.Desk).Append('/').Append(N(operation.Amount)).Append('/')
                .Append(operation.Note).Append(';');

        return text.ToString();
    }

    private static void FillSampleData()
    {
        DateTime now = new DateTime(2026, 3, 18, 15, 30, 0);

        // каталог с закупкой и минимумом
        List<ServiceItem> prices = new List<ServiceItem>();

        ServiceItem oil = new ServiceItem("Расходники", "M-100", "Масло моторное", "л", 700m);
        oil.Cost = 450m;
        oil.MinStock = 20m;
        prices.Add(oil);

        ServiceItem filter = new ServiceItem("Расходники", "F-5", "Фильтр масляный", "шт.", 600m);
        filter.Cost = 250m;
        filter.MinStock = 4m;
        prices.Add(filter);

        prices.Add(new ServiceItem("Работы", "W-1", "Замена масла", "услуга", 1200m));
        ConnectionSettings.Store.SavePrices(prices);

        // набор услуг
        ServiceTemplate template = new ServiceTemplate();
        template.Name = "ТО-1";
        template.Items.Add(new TemplateItem { Group = "Расходники", Article = "M-100", Name = "Масло моторное", Quantity = 4m });
        template.Items.Add(new TemplateItem { Group = "Работы", Article = "W-1", Name = "Замена масла", Quantity = 1m });

        List<ServiceTemplate> templates = new List<ServiceTemplate>();
        templates.Add(template);
        ConnectionSettings.Store.SaveTemplates(templates);

        // реквизиты, тема и логотип
        StoreSnapshot snapshot = ConnectionSettings.Store.LoadAll();
        snapshot.Document.Number = "5/2026";
        snapshot.Document.Customer = "Иван Петров";
        snapshot.Document.CustomerPhone = "8 (912) 345-67-89";
        snapshot.Document.Car = "Toyota Camry";
        snapshot.Document.Plate = "А123ВС 77";
        snapshot.Document.Discount = 5m;
        snapshot.Theme = "dark";
        snapshot.Logo = "iVBORw0KGgo=";
        snapshot.LastTemplate = "ТО-1";
        snapshot.HasSettings = true;
        ConnectionSettings.Store.SaveSettings(snapshot);

        // заявки
        SavedEstimate first = new SavedEstimate();
        first.Number = "1/2026";
        first.Year = 2026;
        first.Sequence = 1;
        first.Saved = now;
        first.Customer = "Иван Петров";
        first.CustomerPhone = "8 (912) 345-67-89";
        first.Car = "Toyota Camry";
        first.Plate = "А123ВС 77";
        first.Status = EstimateStatus.Paid;
        first.Discount = 5m;
        first.Items.Add(new EstimateItem { Group = "Работы", Article = "W-1", Name = "Замена масла", Unit = "услуга", Quantity = 1m, Price = 1200m, Cost = 0m });

        SavedEstimate second = new SavedEstimate();
        second.Number = "2/2026";
        second.Year = 2026;
        second.Sequence = 2;
        second.Saved = now.AddDays(-2);
        second.Customer = "Мария Сидорова";
        second.Status = EstimateStatus.Work;
        second.Items.Add(new EstimateItem { Group = "Расходники", Article = "M-100", Name = "Масло моторное", Unit = "л", Quantity = 4m, Price = 700m, Cost = 450m });

        ConnectionSettings.Archive.Save(first);
        ConnectionSettings.Archive.Save(second);

        // заказчики
        CustomerBook customers = new CustomerBook();
        Customer ivan = customers.Ensure("Иван Петров", "8 (912) 345-67-89");
        ivan.Car = "Toyota Camry";
        ivan.Plate = "А123ВС 77";
        customers.Ensure("Мария Сидорова", "8 (916) 000-11-22");
        ConnectionSettings.Customers.Save(customers);

        // склад
        Warehouse warehouse = new Warehouse();
        warehouse.Income("M-100", "Масло моторное", 10m, 400m, "накладная 12");
        warehouse.Income("F-5", "Фильтр масляный", 4m, 250m, "накладная 12");
        warehouse.Expense("F-5", "Фильтр масляный", 1m, 250m, "списание");
        ConnectionSettings.Warehouse.Save(warehouse);

        // кассы и оплаты
        CashBook cash = new CashBook();
        cash.Ensure("Основная касса");
        cash.Ensure("Расчётный счёт");

        Payment payment = new Payment();
        payment.Number = "1/2026";
        payment.Customer = "Иван Петров";
        payment.Desk = "Расчётный счёт";
        payment.CashDesk = "Основная касса";
        payment.Kind = PaymentKind.Mixed;
        payment.Cash = 500m;
        payment.Cashless = 640m;
        payment.Due = 1140m;
        payment.Saved = now;
        payment.Note = "смешанная оплата";
        cash.AddPayment(payment);

        DeskOperation income = new DeskOperation();
        income.Desk = "Основная касса";
        income.Amount = 10000m;
        income.Saved = now;
        income.Note = "размен";
        cash.Operations.Add(income);

        ConnectionSettings.CashBook.Save(cash);
    }

    private static void Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        string database = args.Length > 1 ? args[1] : "smeta";
        string user = args.Length > 2 ? args[2] : "smeta";
        string password = args.Length > 3 ? args[3] : "smeta";

        string work = Path.Combine(Path.GetTempPath(), "kotov-backup-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);

        string firstStore = Path.Combine(work, "первое");
        string secondStore = Path.Combine(work, "второе");
        Directory.CreateDirectory(firstStore);
        Directory.CreateDirectory(secondStore);

        PgConnectionInfo dbInfo = new PgConnectionInfo(host, 5432, database, user, password);
        bool dbAvailable = true;

        try
        {
            PgClient probe = new PgClient(dbInfo);
            probe.Connect();
            probe.Dispose();
        }
        catch { dbAvailable = false; }

        try
        {
            // ---------------------------------------------- выгрузка из файлов
            Section("Выгрузка данных из файлового хранилища");

            PriceBook.StorePath = Path.Combine(firstStore, "prices.xml");
            ConnectionSettings.UseFiles();
            FillSampleData();

            string sourcePrint = Fingerprint();
            Console.WriteLine("  данных в источнике: " + sourcePrint.Length + " знаков отпечатка");

            string backup = Path.Combine(work, "данные.zip");
            FullBackup.Save(backup);

            CheckTrue("файл выгрузки создан", File.Exists(backup), backup);
            CheckTrue("это один архив", new FileInfo(backup).Length > 500, "размер " + new FileInfo(backup).Length);

            string error;
            BackupContent content = FullBackup.Inspect(backup, out error);

            CheckTrue("оглавление прочитано", content != null, error ?? "нет оглавления");
            CheckTrue("оглавление: каталог", content.Prices, "нет каталога");
            CheckTrue("оглавление: наборы", content.Templates, "нет наборов");
            CheckTrue("оглавление: реквизиты", content.Settings, "нет реквизитов");
            CheckTrue("оглавление: заявки", content.Estimates, "нет заявок");
            CheckTrue("оглавление: заказчики", content.Customers, "нет заказчиков");
            CheckTrue("оглавление: склад", content.Warehouse, "нет склада");
            CheckTrue("оглавление: кассы", content.Cash, "нет касс");

            Check("позиций каталога в оглавлении", 3, content.PriceCount);
            Check("наборов в оглавлении", 1, content.TemplateCount);
            Check("заявок в оглавлении", 2, content.EstimateCount);
            Check("заказчиков в оглавлении", 2, content.CustomerCount);
            Check("движений склада в оглавлении", 3, content.MoveCount);
            Check("оплат в оглавлении", 1, content.PaymentCount);

            // -------------------------------------------- загрузка в пустое хранилище
            Section("Загрузка данных в пустое файловое хранилище");

            PriceBook.StorePath = Path.Combine(secondStore, "prices.xml");
            ConnectionSettings.UseFiles();

            string emptyPrint = Fingerprint();
            CheckTrue("второе хранилище пустое", emptyPrint != sourcePrint, "данные уже есть");

            BackupContent loaded = FullBackup.Restore(backup);

            CheckTrue("данные загружены", loaded != null, "null");
            Check("позиций загружено", 3, loaded.PriceCount);
            Check("наборов загружено", 1, loaded.TemplateCount);
            Check("заявок загружено", 2, loaded.EstimateCount);
            Check("заказчиков загружено", 2, loaded.CustomerCount);
            Check("движений загружено", 3, loaded.MoveCount);
            Check("оплат загружено", 1, loaded.PaymentCount);

            string restoredPrint = Fingerprint();

            if (sourcePrint != restoredPrint)
            {
                // ищем первое расхождение: так видно, какой раздел потерялся
                int limit = Math.Min(sourcePrint.Length, restoredPrint.Length);
                int at = -1;

                for (int i = 0; i < limit; i++)
                    if (sourcePrint[i] != restoredPrint[i]) { at = i; break; }

                Console.WriteLine("  расхождение на знаке " + at + " из " + limit);
                if (at >= 0)
                {
                    int from = Math.Max(0, at - 90);
                    Console.WriteLine("  было:  ..." + sourcePrint.Substring(from, Math.Min(180, sourcePrint.Length - from)));
                    Console.WriteLine("  стало: ..." + restoredPrint.Substring(from, Math.Min(180, restoredPrint.Length - from)));
                }
            }

            CheckTrue("отпечаток данных совпал", sourcePrint == restoredPrint,
                      "данные отличаются после переноса");

            // -------------------------------------------- отдельные подробности
            Section("Что именно сохранилось");

            List<ServiceItem> prices = ConnectionSettings.Store.LoadPrices();
            Check("закупочная цена позиции", 450m, prices[0].Cost);
            Check("минимум позиции", 20m, prices[0].MinStock);

            StoreSnapshot snapshot = ConnectionSettings.Store.LoadAll();
            Check("тема сохранена", "dark", snapshot.Theme);
            Check("логотип сохранён", "iVBORw0KGgo=", snapshot.Logo);
            Check("телефон заказчика по маске", "8 (912) 345-67-89", snapshot.Document.CustomerPhone);
            Check("скидка сохранена", 5m, snapshot.Document.Discount);

            List<SavedEstimate> estimates = ConnectionSettings.Archive.Load();
            SavedEstimate paid = null;
            foreach (SavedEstimate estimate in estimates)
                if (estimate.Number == "1/2026") paid = estimate;

            CheckTrue("заявка найдена", paid != null, "нет заявки 1/2026");
            Check("статус заявки сохранён", EstimateStatus.Paid, paid.Status);
            Check("заказчик в заявке", "Иван Петров", paid.Customer);
            Check("телефон в заявке", "8 (912) 345-67-89", paid.CustomerPhone);
            Check("сумма заявки со скидкой", 1140m, paid.Total);

            Warehouse warehouse = ConnectionSettings.Warehouse.Load();
            Check("остаток масла после переноса", 10m, warehouse.Stock("M-100", "Масло моторное"));
            Check("остаток фильтров после переноса", 3m, warehouse.Stock("F-5", "Фильтр масляный"));
            Check("движение по накладной сохранено", 3, warehouse.Moves.Count);

            CashBook cash = ConnectionSettings.CashBook.Load();
            Check("касс перенесено", 2, cash.Desks.Count);
            Check("оплат перенесено", 1, cash.Payments.Count);
            Check("операций по кассе перенесено", 1, cash.Operations.Count);
            Check("баланс основной кассы", 10500m, cash.Balance("Основная касса"));
            Check("баланс расчётного счёта", 640m, cash.Balance("Расчётный счёт"));

            Payment payment = cash.FindPayment("1/2026");
            CheckTrue("оплата найдена", payment != null, "нет оплаты");
            Check("способ оплаты сохранён", PaymentKind.Mixed, payment.Kind);
            Check("наличные сохранены", 500m, payment.Cash);
            Check("безналичные сохранены", 640m, payment.Cashless);
            Check("касса наличной части", "Основная касса", payment.CashDeskName);
            Check("касса безналичной части", "Расчётный счёт", payment.CashlessDeskName);

            CustomerBook customers = ConnectionSettings.Customers.Load();
            Check("заказчиков перенесено", 2, customers.Customers.Count);
            Customer ivan = customers.Match("Иван Петров", "");
            CheckTrue("карточка найдена", ivan != null, "не найдена");
            Check("машина в карточке", "Toyota Camry", ivan.Car);
            Check("номер в карточке", "А123ВС 77", ivan.Plate);

            // -------------------------------------------- чужие и испорченные файлы
            Section("Отказ на неподходящем файле");

            string wrong = Path.Combine(work, "чужой.zip");
            File.WriteAllText(wrong, "это не архив", Encoding.UTF8);

            BackupContent bad = FullBackup.Inspect(wrong, out error);
            CheckTrue("чужой файл отклонён", bad == null, "принят");
            CheckTrue("причина названа", !string.IsNullOrEmpty(error), "причина не названа");

            string plain = Path.Combine(work, "просто.txt");
            File.WriteAllText(plain, "текст", Encoding.UTF8);
            CheckTrue("обычный файл отклонён", FullBackup.Inspect(plain, out error) == null, "принят");

            // -------------------------------------------- выгрузка из базы данных
            Section("Выгрузка и загрузка через базу данных");

            if (!dbAvailable)
            {
                Console.WriteLine("  ПРОПУЩЕНО: база данных недоступна");
            }
            else
            {
                PgClient admin = new PgClient(dbInfo);
                admin.Connect();
                admin.Execute("DROP TABLE IF EXISTS smeta_prices");
                admin.Execute("DROP TABLE IF EXISTS smeta_templates");
                admin.Execute("DROP TABLE IF EXISTS smeta_settings");
                admin.Execute("DROP TABLE IF EXISTS smeta_estimates");
                admin.Execute("DROP TABLE IF EXISTS smeta_customers");
                admin.Execute("DROP TABLE IF EXISTS smeta_stockmoves");
                admin.Execute("DROP TABLE IF EXISTS smeta_cashdesks");
                admin.Execute("DROP TABLE IF EXISTS smeta_payments");
                admin.Execute("DROP TABLE IF EXISTS smeta_cashops");
                admin.Dispose();

                // заполняем базу теми же данными
                ConnectionSettings.UseSql(dbInfo);
                FillSampleData();

                string dbSourcePrint = Fingerprint();
                string dbBackup = Path.Combine(work, "из-базы.zip");
                FullBackup.Save(dbBackup);

                BackupContent dbContent = FullBackup.Inspect(dbBackup, out error);
                CheckTrue("оглавление архива из базы", dbContent != null, error ?? "нет оглавления");
                Check("позиций каталога из базы", 3, dbContent.PriceCount);
                Check("заявок из базы", 2, dbContent.EstimateCount);

                // чистим базу и загружаем архив обратно
                PgClient clean = new PgClient(dbInfo);
                clean.Connect();
                clean.Execute("DELETE FROM smeta_prices");
                clean.Execute("DELETE FROM smeta_templates");
                clean.Execute("DELETE FROM smeta_estimates");
                clean.Execute("DELETE FROM smeta_customers");
                clean.Execute("DELETE FROM smeta_stockmoves");
                clean.Execute("DELETE FROM smeta_payments");
                clean.Execute("DELETE FROM smeta_cashops");
                clean.Dispose();

                BackupContent dbLoaded = FullBackup.Restore(dbBackup);
                Check("загружено в базу: позиций", 3, dbLoaded.PriceCount);

                string dbPrint = Fingerprint();
                CheckTrue("отпечаток данных в базе совпал", dbSourcePrint == dbPrint,
                          "данные отличаются после переноса");
                if (sourcePrint != dbPrint)
                {
                    int limit = Math.Min(sourcePrint.Length, dbPrint.Length);
                    int at = -1;

                    for (int i = 0; i < limit; i++)
                        if (sourcePrint[i] != dbPrint[i]) { at = i; break; }

                    Console.WriteLine("  расхождение файлы/база на знаке " + at + " из " + limit);
                    if (at >= 0)
                    {
                        int from = Math.Max(0, at - 90);
                        Console.WriteLine("  файлы: ..." + sourcePrint.Substring(from, Math.Min(180, sourcePrint.Length - from)));
                        Console.WriteLine("  база:  ..." + dbPrint.Substring(from, Math.Min(180, dbPrint.Length - from)));
                    }
                }

                CheckTrue("отпечаток совпал с файловым", sourcePrint == dbPrint,
                          "данные в базе и в файлах отличаются");

                // архив, снятый с файлов, загружается в базу
                BackupContent intoDb = FullBackup.Restore(backup);
                Check("файловый архив загружен в базу", 3, intoDb.PriceCount);
                CheckTrue("данные из файлового архива совпали", sourcePrint == Fingerprint(),
                          "данные отличаются");

                // уборка
                PgClient cleanup = new PgClient(dbInfo);
                cleanup.Connect();
                cleanup.Execute("DROP TABLE IF EXISTS smeta_prices");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_templates");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_settings");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_estimates");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_customers");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_stockmoves");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_cashdesks");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_payments");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_cashops");
                cleanup.Dispose();
            }

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ВЫГРУЗКИ И ЗАГРУЗКИ ПРОЙДЕНЫ"); Environment.Exit(0); }
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
