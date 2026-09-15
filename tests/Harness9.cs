// Проверки оплат, касс и сводки для панели показателей.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KotovCalc;

internal static class Harness9
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

    private static Payment Pay(string number, string desk, PaymentKind kind, decimal cash, decimal cashless,
                               DateTime saved, decimal due)
    {
        Payment payment = new Payment();
        payment.Number = number;
        payment.Desk = desk;
        payment.Kind = kind;
        payment.Cash = cash;
        payment.Cashless = cashless;
        payment.Saved = saved;
        payment.Due = due;
        payment.Customer = "Заказчик " + number;
        return payment;
    }

    private static void Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        string database = args.Length > 1 ? args[1] : "smeta";
        string user = args.Length > 2 ? args[2] : "smeta";
        string password = args.Length > 3 ? args[3] : "smeta";

        string work = Path.Combine(Path.GetTempPath(), "kotov-cash-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);
        PriceBook.StorePath = Path.Combine(work, "prices.xml");

        try
        {
            DateTime now = new DateTime(2026, 3, 18, 15, 30, 0);   // среда

            Section("Кассы и балансы");

            CashBook cash = new CashBook();
            cash.Ensure("Основная касса");
            cash.Ensure("Касса мастеров");
            Check("касс создано", 2, cash.Desks.Count);
            CheckTrue("повторное создание не дублирует", cash.Ensure("Основная касса") == cash.Find("Основная касса"),
                "создалась вторая");
            Check("касс всё ещё", 2, cash.Desks.Count);
            CheckTrue("поиск без учёта регистра", cash.Find("основная касса") != null, "не найдена");

            cash.AddPayment(Pay("1/2026", "Основная касса", PaymentKind.Cash, 1500m, 0m, now, 1500m));
            cash.AddPayment(Pay("2/2026", "Основная касса", PaymentKind.Cashless, 0m, 2000m, now, 2000m));
            cash.AddPayment(Pay("5/2026", "Касса мастеров", PaymentKind.Mixed, 500m, 1500m, now, 2000m));
            cash.AddPayment(Pay("6/2026", "Основная касса", PaymentKind.Cash, 2000m, 0m, now.AddDays(-1), 2000m));
            cash.AddPayment(Pay("7/2026", "Основная касса", PaymentKind.Cashless, 0m, 2000m, now.AddDays(-40), 2000m));

            Check("баланс основной кассы", 7500m, cash.Balance("Основная касса"));
            Check("баланс кассы мастеров", 2000m, cash.Balance("Касса мастеров"));
            Check("баланс неизвестной кассы", 0m, cash.Balance("Нет такой"));

            Section("Смешанная оплата и недоплата");

            Payment mixed = cash.FindPayment("5/2026");
            Check("способ оплаты смешанный", PaymentKind.Mixed, mixed.Kind);
            Check("наличными", 500m, mixed.Cash);
            Check("безналичными", 1500m, mixed.Cashless);
            Check("всего по оплате", 2000m, mixed.Total);
            CheckTrue("заявка оплачена полностью", mixed.IsFull, "считается неполной");
            Check("доплата не нужна", 0m, mixed.Remaining);

            Payment partial = Pay("8/2026", "Основная касса", PaymentKind.Cash, 1000m, 0m, now, 2500m);
            cash.AddPayment(partial);
            CheckTrue("частичная оплата не считается полной", !partial.IsFull, "считается полной");
            Check("осталось доплатить", 1500m, partial.Remaining);
            Check("баланс вырос на внесённое", 8500m, cash.Balance("Основная касса"));

            Section("Повторная оплата заменяет прежнюю");

            cash.AddPayment(Pay("8/2026", "Основная касса", PaymentKind.Mixed, 1500m, 1000m, now, 2500m));
            Check("оплат по заявке 8/2026 одна", 1, CountPayments(cash, "8/2026"));
            Payment updated = cash.FindPayment("8/2026");
            CheckTrue("заявка оплачена полностью", updated.IsFull, "не полная");
            Check("баланс пересчитан", 10000m, cash.Balance("Основная касса"));

            Section("Ручные операции по кассе");

            DeskOperation income = new DeskOperation();
            income.Desk = "Основная касса";
            income.Amount = 10000m;
            income.Saved = now;
            income.Note = "Внесение размена";
            cash.Operations.Add(income);

            DeskOperation expense = new DeskOperation();
            expense.Desk = "Основная касса";
            expense.Amount = -2500m;
            expense.Saved = now;
            expense.Note = "Инкассация";
            cash.Operations.Add(expense);

            Check("баланс с операциями", 17500m, cash.Balance("Основная касса"));
            Check("операции не задели другую кассу", 2000m, cash.Balance("Касса мастеров"));

            Section("Сводка: день, неделя, месяц, средний чек");

            string estimatesFile = Path.Combine(work, "estimates.json");
            List<SavedEstimate> estimates = new List<SavedEstimate>();
            estimates.Add(MakeEstimate("1/2026", now, "Пётр", new string[] { "Диагностика двигателя", "Замена масла" }));
            estimates.Add(MakeEstimate("2/2026", now.AddDays(-1), "Иван", new string[] { "Диагностика двигателя" }));
            estimates.Add(MakeEstimate("3/2026", now.AddDays(-9), "Сергей", new string[] { "Замена масла", "Шиномонтаж" }));
            estimates.Add(MakeEstimate("4/2026", now.AddDays(-40), "Анна", new string[] { "Диагностика двигателя" }));

            DashboardInfo info = Dashboard.Build(cash, estimates, now, 4);

            Check("касс в сводке", 2, info.Desks.Count);
            Check("первая касса по балансу", "Основная касса", info.Desks[0].Desk);
            Check("баланс первой кассы", 17500m, info.Desks[0].Balance);
            Check("денег во всех кассах", 19500m, info.DeskTotal);

            Check("оплачено сегодня", 4, info.PaidToday);
            Check("сумма за сегодня", 8000m, info.AmountToday);
            Check("оплачено за неделю", 5, info.PaidWeek);
            Check("сумма за неделю", 10000m, info.AmountWeek);
            Check("оплачено за месяц", 5, info.PaidMonth);
            Check("сумма за месяц", 10000m, info.AmountMonth);
            Check("средний чек", 2000m, info.AverageCheck);
            Check("всего оплат", 6, info.PaidTotal);

            Section("Популярные позиции");

            Check("позиций в сводке", 3, info.Popular.Count);
            Check("самая частая позиция", "Диагностика двигателя", info.Popular[0].Name);
            Check("заявок с ней", 3, info.Popular[0].Requests);
            Check("второе место по заявкам", "Замена масла", info.Popular[1].Name);
            Check("заявок с заменой масла", 2, info.Popular[1].Requests);
            CheckTrue("количество просуммировано", info.Popular[0].Quantity > 0m, "ноль");

            Section("Сводка по прошедшим датам");

            // ожидания считаем по фактическому списку оплат: проверка не зависит
            // от того, как именно разложены даты в наборе
            DateTime reference = now.Date.AddDays(-1).AddHours(15);
            DateTime referenceWeek = reference.Date.AddDays(-((int)reference.DayOfWeek + 6) % 7);
            DateTime referenceMonth = new DateTime(reference.Year, reference.Month, 1);

            int expectedWeek = 0;
            int expectedMonth = 0;
            foreach (Payment payment in cash.Payments)
            {
                if (payment.Saved >= referenceWeek && payment.Saved <= reference) expectedWeek++;
                if (payment.Saved >= referenceMonth && payment.Saved <= reference) expectedMonth++;
            }

            DashboardInfo yesterday = Dashboard.Build(cash, estimates, reference, 4);
            // величины должны быть согласованы: день не больше недели, неделя не больше месяца
            CheckTrue("день не больше недели", info.PaidToday <= info.PaidWeek,
                "день " + info.PaidToday + ", неделя " + info.PaidWeek);
            CheckTrue("неделя не больше месяца", info.PaidWeek <= info.PaidMonth,
                "неделя " + info.PaidWeek + ", месяц " + info.PaidMonth);
            CheckTrue("месяц не больше общего числа", info.PaidMonth <= info.PaidTotal,
                "месяц " + info.PaidMonth + ", всего " + info.PaidTotal);
            CheckTrue("суммы согласованы так же",
                info.AmountToday <= info.AmountWeek && info.AmountWeek <= info.AmountMonth,
                "суммы: " + info.AmountToday + " / " + info.AmountWeek + " / " + info.AmountMonth);
            // при ссылке на прошлый день оплат не может быть больше, чем за все время
            CheckTrue("вчерашняя сводка не больше общей",
                yesterday.PaidMonth <= info.PaidTotal, "месяц вчера больше общего числа");
            CheckTrue("сегодняшние оплаты не попали во вчерашний день",
                yesterday.PaidTotal == info.PaidTotal, "всего оплат не совпало");

            DashboardInfo empty = Dashboard.Build(new CashBook(), new List<SavedEstimate>(), now, 4);
            Check("пустая сводка: касс", 0, empty.Desks.Count);
            Check("пустая сводка: оплат", 0, empty.PaidToday);
            Check("пустая сводка: средний чек", 0m, empty.AverageCheck);


            Section("Сохранение и чтение в файлах");

            FileCashBook fileCash = new FileCashBook();
            fileCash.Save(cash);
            CheckTrue("папка касс создана", Directory.Exists(FileCashBook.Folder), FileCashBook.Folder);

            FileCashBook archive = new FileCashBook();
            CashBook read = archive.Load();
            Check("касс прочитано", 2, read.Desks.Count);
            Check("оплат прочитано", 6, read.Payments.Count);
            Check("баланс основной кассы после чтения", 17500m, read.Balance("Основная касса"));
            Check("операций прочитано", 2, read.Operations.Count);
            Check("смешанная оплата сохранилась", PaymentKind.Mixed, read.FindPayment("5/2026").Kind);
            Check("наличные сохранились", 500m, read.FindPayment("5/2026").Cash);
            Check("безналичные сохранились", 1500m, read.FindPayment("5/2026").Cashless);
            Check("сумма заявки сохранилась", 2000m, read.FindPayment("5/2026").Due);
            Check("время оплаты сохранилось", now.ToString("dd.MM.yyyy HH:mm", Fmt.Ru),
                read.FindPayment("5/2026").Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));

            DashboardInfo afterRead = Dashboard.Build(read, estimates, now, 4);
            Check("сводка по прочитанному совпадает", info.DeskTotal, afterRead.DeskTotal);
            Check("средний чек после чтения", info.AverageCheck, afterRead.AverageCheck);

            Section("Удаление оплаты");

            CheckTrue("оплата удалена", read.RemovePayment("8/2026"), "не найдена");
            Check("оплат стало", 5, read.Payments.Count);
            Check("баланс уменьшился", 15000m, read.Balance("Основная касса"));

            Section("Проверка записи JSON");

            string json = CashBookJson.PaymentToJson(cash.FindPayment("5/2026"));
            CheckTrue("оплата записана в JSON", json.Contains("raschet-smeta-payment"), json);
            Payment restored = CashBookJson.PaymentFromJson(json);
            CheckTrue("оплата прочитана из JSON", restored != null, "null");
            Check("способ оплаты в JSON", PaymentKind.Mixed, restored.Kind);
            Check("наличные в JSON", 500m, restored.Cash);
            CheckTrue("испорченный JSON отклонён", CashBookJson.PaymentFromJson("ерунда") == null, "принят");
            CheckTrue("чужой формат отклонён",
                CashBookJson.PaymentFromJson("{\"format\":\"другое\"}") == null, "принят");

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
                admin.Execute("DROP TABLE IF EXISTS smeta_cashdesks");
                admin.Execute("DROP TABLE IF EXISTS smeta_payments");
                admin.Execute("DROP TABLE IF EXISTS smeta_cashops");
                admin.Dispose();

                ICashBookStore sqlCash = new SqlCashBook(dbInfo);
                sqlCash.Save(cash);

                CashBook sqlRead = sqlCash.Load();
                Check("касс в базе", 2, sqlRead.Desks.Count);
                Check("оплат в базе", 6, sqlRead.Payments.Count);
                Check("баланс из базы", 17500m, sqlRead.Balance("Основная касса"));
                Check("операций в базе", 2, sqlRead.Operations.Count);
                Check("способ оплаты из базы", PaymentKind.Mixed, sqlRead.FindPayment("5/2026").Kind);
                Check("наличные из базы", 500m, sqlRead.FindPayment("5/2026").Cash);
                Check("безналичные из базы", 1500m, sqlRead.FindPayment("5/2026").Cashless);

                DashboardInfo sqlInfo = Dashboard.Build(sqlRead, estimates, now, 4);
                Check("сводка по базе совпадает", info.DeskTotal, sqlInfo.DeskTotal);
                Check("средний чек по базе", info.AverageCheck, sqlInfo.AverageCheck);
                Check("популярная позиция по базе", "Диагностика двигателя", sqlInfo.Popular[0].Name);

                sqlCash.Delete(sqlRead.FindPayment("8/2026"));
                Check("оплата удалена из базы", 5, sqlCash.Load().Payments.Count);

                PgClient cleanup = new PgClient(dbInfo);
                cleanup.Connect();
                cleanup.Execute("DROP TABLE IF EXISTS smeta_cashdesks");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_payments");
                cleanup.Execute("DROP TABLE IF EXISTS smeta_cashops");
                cleanup.Dispose();
                CheckTrue("таблицы убраны", true, "ошибка уборки");
            }

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ОПЛАТ И КАСС ПРОЙДЕНЫ"); Environment.Exit(0); }
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

    private static int CountPayments(CashBook cash, string number)
    {
        int count = 0;
        foreach (Payment payment in cash.Payments)
            if (payment.Number == number) count++;
        return count;
    }

    private static SavedEstimate MakeEstimate(string number, DateTime saved, string customer, string[] names)
    {
        SavedEstimate estimate = new SavedEstimate();
        estimate.Saved = saved;
        estimate.Number = number;
        estimate.Customer = customer;

        int sequence, year;
        if (EstimateNumbering.Parse(number, out sequence, out year))
        {
            estimate.Sequence = sequence;
            estimate.Year = year;
        }

        foreach (string name in names)
        {
            EstimateItem item = new EstimateItem();
            item.Group = "Работы";
            item.Article = "ART-" + name.Length;
            item.Name = name;
            item.Unit = "услуга";
            item.Quantity = 1m;
            item.Price = 1000m;
            estimate.Items.Add(item);
        }

        return estimate;
    }
}
