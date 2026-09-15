// Проверки справочника заказчиков, склада, себестоимости и чека.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KotovCalc;

internal static class Harness10
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

    private static void Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        string database = args.Length > 1 ? args[1] : "smeta";
        string user = args.Length > 2 ? args[2] : "smeta";
        string password = args.Length > 3 ? args[3] : "smeta";

        string work = Path.Combine(Path.GetTempPath(), "kotov-new-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);
        PriceBook.StorePath = Path.Combine(work, "prices.xml");

        try
        {
            Section("Статусы заявок");

            Check("статусов всего", 4, EstimateStatuses.List.Length);
            Check("название новой", "новая", EstimateStatuses.Title(EstimateStatus.New));
            Check("название в работе", "в работе", EstimateStatuses.Title(EstimateStatus.Work));
            Check("разбор «выполнена»", EstimateStatus.Done, EstimateStatuses.Parse("выполнена"));
            Check("разбор «оплачена»", EstimateStatus.Paid, EstimateStatuses.Parse("оплачена"));
            Check("неизвестный статус — новая", EstimateStatus.New, EstimateStatuses.Parse("ерунда"));

            SavedEstimate estimate = MakeEstimate("1/2026", DateTime.Now, "Иван Петров", 2);
            Check("статус по умолчанию", EstimateStatus.New, estimate.Status);

            estimate.Status = EstimateStatus.Done;
            string json = EstimateJson.ToJson(estimate);
            SavedEstimate back = EstimateJson.FromJson(json);
            CheckTrue("статус пережил запись и чтение", back != null, "null");
            Check("прочитанный статус", EstimateStatus.Done, back.Status);

            Section("Справочник заказчиков");

            CustomerBook book = new CustomerBook();
            Customer ivan = book.Ensure("Иван Петров", "+7 (912) 345-67-89");
            ivan.Car = "Toyota Camry";
            ivan.Plate = "А123ВС 77";

            Customer maria = book.Ensure("Мария Сидорова", "8 916 000-11-22");
            maria.Car = "Kia Rio";

            Check("заказчиков", 2, book.Customers.Count);
            CheckTrue("повторное добавление не создаёт дубль",
                book.Ensure("Иван Петров", "89123456789") == ivan, "создана новая карточка");
            Check("заказчиков по-прежнему", 2, book.Customers.Count);

            Check("поиск по телефону в другом формате", ivan, book.Match("", "+79123456789"));
            Check("поиск по имени", ivan, book.Match("иван петров", ""));
            Check("поиск по имени и телефону", maria, book.Match("Мария Сидорова", "89160001122"));
            CheckTrue("неизвестный заказчик не найден", book.Match("Никто", "") == null, "нашёлся");

            Check("поиск по машине", 1, book.Search("camry").Count);
            Check("поиск по номеру", 1, book.Search("А123").Count);
            Check("поиск по части телефона", 1, book.Search("345").Count);
            Check("поиск по пустой строке — все", 2, book.Search("").Count);

            Section("Заявка и заказчик");

            SavedEstimate first = MakeEstimate("2/2026", DateTime.Now, ivan.Name, 1);
            first.CustomerId = ivan.Id;
            first.CustomerPhone = ivan.Phone;
            first.Car = ivan.Car;
            first.Plate = ivan.Plate;

            List<SavedEstimate> estimates = new List<SavedEstimate>();
            estimates.Add(first);
            estimates.Add(MakeEstimate("3/2026", DateTime.Now, "Иван Петров", 2));      // введён вручную
            estimates.Add(MakeEstimate("4/2026", DateTime.Now, "Кто-то Другой", 1));

            Check("заявок у Ивана: карточка и ручной ввод", 2, CustomerBook.CountEstimates(ivan, estimates));
            Check("заявок у Марии", 0, CustomerBook.CountEstimates(maria, estimates));

            CashBook cash = new CashBook();
            cash.Ensure("Основная касса");
            cash.AddPayment(MakePayment("2/2026", "Основная касса", 3000m));
            Check("оплачено по заказчику", 3000m, CustomerBook.PaidTotal(ivan, estimates, cash));

            Section("Склад: приход, расход, остаток");

            Warehouse warehouse = new Warehouse();
            warehouse.Income("M-100", "Масло моторное", 10m, 400m, "накладная 12");
            warehouse.Income("M-100", "Масло моторное", 10m, 500m, "накладная 27");
            warehouse.Income("F-5", "Фильтр масляный", 4m, 250m, "накладная 12");

            Check("остаток масла", 20m, warehouse.Stock("M-100", "Масло моторное"));
            Check("остаток фильтров", 4m, warehouse.Stock("F-5", "Фильтр масляный"));
            Check("остаток неизвестной позиции", 0m, warehouse.Stock("ZZ", "Нет такой"));
            Check("средняя себестоимость масла", 450m, warehouse.AverageCost("M-100", "Масло моторное"));
            Check("последняя цена масла", 500m, warehouse.LastCost("M-100", "Масло моторное"));

            warehouse.Expense("F-5", "Фильтр масляный", 2m, 250m, "списание");
            Check("остаток после расхода", 2m, warehouse.Stock("F-5", "Фильтр масляный"));

            warehouse.Adjust("M-100", "Масло моторное", 18m, 450m, "");
            Check("после инвентаризации", 18m, warehouse.Stock("M-100", "Масло моторное"));

            Section("Списание по заявке");

            SavedEstimate order = MakeEstimate("5/2026", DateTime.Now, "Сергей", 1);
            order.Items[0].Article = "M-100";
            order.Items[0].Name = "Масло моторное";
            order.Items[0].Quantity = 4m;
            order.Items[0].Cost = 450m;

            CheckTrue("до списания заявки нет", !warehouse.HasWriting("5/2026"), "уже есть");

            List<StockMove> written = warehouse.WriteOff(order);
            Check("движений при списании", 1, written.Count);
            Check("остаток после списания по заявке", 14m, warehouse.Stock("M-100", "Масло моторное"));
            CheckTrue("списание отмечено по заявке", warehouse.HasWriting("5/2026"), "не отмечено");
            Check("движений по заявке", 1, warehouse.MovesForEstimate("5/2026").Count);
            Check("движение расхода", -4m, written[0].Quantity);
            Check("стоимость движения", -1800m, written[0].Amount);
            Check("в подписи движения есть номер заявки",
                true, written[0].Caption.Contains("5/2026"));

            Section("Остаток ниже минимума");

            List<ServiceItem> catalog = new List<ServiceItem>();
            ServiceItem oil = new ServiceItem("Расходники", "M-100", "Масло моторное", "л", 700m);
            oil.MinStock = 20m;
            catalog.Add(oil);

            ServiceItem filter = new ServiceItem("Расходники", "F-5", "Фильтр масляный", "шт.", 600m);
            filter.MinStock = 1m;
            catalog.Add(filter);

            List<string> low = warehouse.BelowMinimum(catalog);
            // масло: 14 из 20 — ниже минимума; фильтр: 2 из 1 — в норме
            Check("позиций ниже минимума", 1, low.Count);
            CheckTrue("в списке именно масло", low.Count > 0 && low[0].Contains("Масло"), "другая позиция");

            Section("Себестоимость и маржа");

            SavedEstimate margin = MakeEstimate("6/2026", DateTime.Now, "Анна", 2);
            margin.Items[0].Price = 1000m;
            margin.Items[0].Cost = 400m;
            margin.Items[0].Quantity = 2m;      // 2000 выручки, 800 себестоимости
            margin.Items[1].Price = 500m;
            margin.Items[1].Cost = 100m;
            margin.Items[1].Quantity = 1m;      // 500 выручки, 100 себестоимости

            Check("себестоимость позиции", 800m, Margin.ItemCost(margin.Items[0]));
            Check("себестоимость заявки", 900m, Margin.EstimateCost(margin));
            Check("прибыль по позиции", 1200m, Margin.ItemProfit(margin.Items[0]));
            Check("прибыль по заявке", 1600m, Margin.EstimateProfit(margin));

            List<SavedEstimate> marginList = new List<SavedEstimate>();
            marginList.Add(margin);
            MarginInfo info = Margin.Build(marginList);

            Check("заявок в итогах", 1, info.Requests);
            Check("выручка", 2500m, info.Revenue);
            Check("себестоимость", 900m, info.Cost);
            Check("прибыль", 1600m, info.Profit);
            Check("процент прибыли", 64, info.Percent);

            MarginInfo empty = Margin.Build(new List<SavedEstimate>());
            Check("пустые итоги: процент", 0, empty.Percent);

            Section("Чек об оплате");

            Payment payment = MakePayment("6/2026", "Основная касса", 2500m);
            payment.Kind = PaymentKind.Mixed;
            payment.Cash = 1000m;
            payment.Cashless = 1500m;
            payment.CashDesk = "Касса наличных";
            payment.Due = 2500m;

            Receipt receipt = new Receipt();
            receipt.Number = "6/2026";
            receipt.Saved = new DateTime(2026, 3, 18, 14, 30, 0);
            receipt.Customer = "Анна";
            receipt.Cashier = "Администратор";
            receipt.Payment = payment;

            string text = receipt.ToText(42);
            CheckTrue("в чеке есть заголовок", text.Contains("ОПЛАТА ПО ЗАЯВКЕ"), "нет заголовка");
            CheckTrue("в чеке есть номер", text.Contains("6/2026"), "нет номера");
            CheckTrue("в чеке есть дата", text.Contains("18.03.2026"), "нет даты");
            CheckTrue("в чеке есть заказчик", text.Contains("Анна"), "нет заказчика");
            CheckTrue("в чеке есть наличные", text.Contains("наличные"), "нет наличных");
            CheckTrue("в чеке есть безналичные", text.Contains("безналичные"), "нет безналичных");
            CheckTrue("в чеке есть касса наличных", text.Contains("Касса наличных"), "нет кассы");
            CheckTrue("в чеке есть итог", text.Contains("ИТОГО ОПЛАЧЕНО"), "нет итога");
            CheckTrue("в чеке указано, что оплачено полностью",
                text.Contains("оплачена полностью"), "нет отметки");
            CheckTrue("в чеке указан принявший оплату", text.Contains("Администратор"), "нет подписи");

            bool narrow = true;
            foreach (string line in text.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                if (line.Length > 42) narrow = false;

            CheckTrue("строки чека не шире 42 знаков", narrow, "есть длинные строки");

            // частичная оплата: в чеке должен быть остаток
            payment.Due = 3000m;
            text = receipt.ToText(42);
            CheckTrue("при недоплате указан остаток", text.Contains("ОСТАЛОСЬ ДОПЛАТИТЬ"), "нет остатка");
            CheckTrue("сумма остатка верна", text.Contains("500,00"), "нет суммы остатка");

            string saved = receipt.SaveText(work);
            CheckTrue("чек сохранён файлом", File.Exists(saved), saved);
            CheckTrue("имя файла чека содержит номер", Path.GetFileName(saved).Contains("6_2026"), Path.GetFileName(saved));

            Section("Сохранение заказчиков и склада в файлах");

            FileCustomerBook fileCustomers = new FileCustomerBook();
            fileCustomers.Save(book);
            CustomerBook readCustomers = fileCustomers.Load();

            Check("заказчиков прочитано", 2, readCustomers.Customers.Count);
            Customer loaded = readCustomers.Match("Иван Петров", "");
            CheckTrue("карточка найдена по имени", loaded != null, "не найдена");
            Check("телефон сохранён", "+7 (912) 345-67-89", loaded.Phone);
            Check("машина сохранена", "Toyota Camry", loaded.Car);
            Check("номер сохранён", "А123ВС 77", loaded.Plate);

            FileWarehouse fileWarehouse = new FileWarehouse();
            fileWarehouse.Save(warehouse);
            Warehouse readWarehouse = fileWarehouse.Load();

            Check("движений прочитано", warehouse.Moves.Count, readWarehouse.Moves.Count);
            Check("остаток масла после чтения", 14m, readWarehouse.Stock("M-100", "Масло моторное"));
            Check("средняя себестоимость после чтения", 450m, readWarehouse.AverageCost("M-100", "Масло моторное"));
            Check("движение по заявке прочитано", 1, readWarehouse.MovesForEstimate("5/2026").Count);

            Section("Проверка записи JSON");

            string customerJson = CustomerJson.ToJson(ivan);
            Customer restored = CustomerJson.FromJson(customerJson);
            CheckTrue("заказчик записан в JSON", customerJson.Contains("raschet-smeta-customer"), customerJson);
            CheckTrue("заказчик прочитан из JSON", restored != null, "null");
            Check("имя в JSON", "Иван Петров", restored.Name);
            CheckTrue("испорченный JSON отклонён", CustomerJson.FromJson("ерунда") == null, "принят");

            string moveJson = WarehouseJson.ToJson(written[0]);
            StockMove moveBack = WarehouseJson.FromJson(moveJson);
            CheckTrue("движение записано в JSON", moveJson.Contains("raschet-smeta-stockmove"), moveJson);
            CheckTrue("движение прочитано из JSON", moveBack != null, "null");
            Check("количество в JSON", -4m, moveBack.Quantity);
            Check("номер заявки в JSON", "5/2026", moveBack.Number);
            CheckTrue("движение без количества отклонено",
                WarehouseJson.FromJson("{\"format\":\"raschet-smeta-stockmove\"}") == null, "принят");

            Section("Проверка хранилища в базе");

            PgConnectionInfo dbInfo = new PgConnectionInfo(host, 5432, database, user, password);
            bool dbAvailable = true;

            try
            {
                PgClient probe = new PgClient(dbInfo);
                probe.Connect();
                probe.Dispose();
            }
            catch { dbAvailable = false; }

            if (!dbAvailable)
            {
                Console.WriteLine("  ПРОПУЩЕНО: база данных недоступна");
            }
            else
            {
                PgClient admin = new PgClient(dbInfo);
                admin.Connect();
                admin.Execute("DROP TABLE IF EXISTS smeta_customers");
                admin.Execute("DROP TABLE IF EXISTS smeta_stockmoves");
                admin.Dispose();

                ICustomerStore sqlCustomers = new SqlCustomerBook(dbInfo);
                sqlCustomers.Save(book);
                CustomerBook sqlRead = sqlCustomers.Load();

                Check("заказчиков в базе", 2, sqlRead.Customers.Count);
                Customer sqlIvan = sqlRead.Match("Иван Петров", "");
                CheckTrue("карточка из базы найдена", sqlIvan != null, "не найдена");
                Check("телефон из базы", "+7 (912) 345-67-89", sqlIvan.Phone);
                Check("машина из базы", "Toyota Camry", sqlIvan.Car);

                IWarehouseStore sqlWarehouse = new SqlWarehouse(dbInfo);
                sqlWarehouse.Save(warehouse);
                Warehouse sqlStock = sqlWarehouse.Load();

                Check("движений в базе", warehouse.Moves.Count, sqlStock.Moves.Count);
                Check("остаток из базы", 14m, sqlStock.Stock("M-100", "Масло моторное"));
                Check("движение по заявке из базы", 1, sqlStock.MovesForEstimate("5/2026").Count);

                IEstimateArchive sqlEstimates = new SqlEstimateArchive(dbInfo);
                sqlEstimates.Save(margin);
                List<SavedEstimate> sqlList = sqlEstimates.Load();
                Check("заявок в базе", 1, sqlList.Count);
                Check("статус заявки из базы", EstimateStatus.New, sqlList[0].Status);

                SavedEstimate withCustomer = MakeEstimate("7/2026", DateTime.Now, ivan.Name, 1);
                withCustomer.CustomerId = ivan.Id;
                withCustomer.CustomerPhone = ivan.Phone;
                withCustomer.Car = ivan.Car;
                withCustomer.Plate = ivan.Plate;
                withCustomer.Status = EstimateStatus.Work;
                sqlEstimates.Save(withCustomer);

                SavedEstimate loadedWith = null;
                foreach (SavedEstimate item in sqlEstimates.Load())
                    if (item.Number == "7/2026") loadedWith = item;

                CheckTrue("заявка с заказчиком прочитана", loadedWith != null, "не найдена");
                Check("заказчик в заявке из базы", ivan.Id, loadedWith.CustomerId);
                Check("телефон в заявке из базы", ivan.Phone, loadedWith.CustomerPhone);
                Check("машина в заявке из базы", ivan.Car, loadedWith.Car);
                Check("статус «в работе» из базы", EstimateStatus.Work, loadedWith.Status);

                PgClient cleanup = new PgClient(dbInfo);
                cleanup.Connect();
                cleanup.Execute("DROP TABLE IF EXISTS smeta_customers");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_stockmoves");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_estimates");
                cleanup.Dispose();
            }

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ЗАКАЗЧИКОВ, СКЛАДА И ЧЕКА ПРОЙДЕНЫ"); Environment.Exit(0); }
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

    private static Payment MakePayment(string number, string desk, decimal amount)
    {
        Payment payment = new Payment();
        payment.Number = number;
        payment.Desk = desk;
        payment.Kind = PaymentKind.Cash;
        payment.Cash = amount;
        payment.Due = amount;
        payment.Saved = DateTime.Now;
        return payment;
    }

    private static SavedEstimate MakeEstimate(string number, DateTime saved, string customer, int items)
    {
        SavedEstimate estimate = new SavedEstimate();
        estimate.Number = number;
        estimate.Saved = saved;
        estimate.Customer = customer;

        int sequence, year;
        if (EstimateNumbering.Parse(number, out sequence, out year))
        {
            estimate.Sequence = sequence;
            estimate.Year = year;
        }

        for (int i = 0; i < items; i++)
        {
            EstimateItem item = new EstimateItem();
            item.Group = "Работы";
            item.Article = "ART-" + (i + 1);
            item.Name = "Работа " + (i + 1);
            item.Unit = "услуга";
            item.Quantity = 1m;
            item.Price = 1000m;
            item.Cost = 400m;
            estimate.Items.Add(item);
        }

        return estimate;
    }
}
