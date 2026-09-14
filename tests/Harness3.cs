// Проверки новых возможностей: JSON, настройки, шаблоны, обмен данными,
// итог со скидкой и цена позиции, изменённая в смете.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KotovCalc;

internal static class Harness3
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

    [STAThread]
    private static void Main(string[] args)
    {
        string work = Path.Combine(Path.GetTempPath(), "kotov-h3-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);
        PriceBook.StorePath = Path.Combine(work, "prices.xml");

        try
        {
            Section("Запись и разбор JSON");
            Dictionary<string, object> map = new Dictionary<string, object>();
            map["name"] = "Услуга \"кавычки\" и слэш \\";
            map["price"] = 1500.5m;
            map["flag"] = true;
            map["none"] = null;
            map["list"] = new List<object> { 1m, "два", false };
            Dictionary<string, object> inner = new Dictionary<string, object>();
            inner["вложено"] = "да";
            map["object"] = inner;

            string json = SimpleJson.Write(map);
            IDictionary<string, object> back = SimpleJson.Parse(json) as IDictionary<string, object>;
            CheckTrue("JSON разобран", back != null, "null");
            Check("строка с кавычками", "Услуга \"кавычки\" и слэш \\", SimpleJson.Text(back, "name"));
            Check("число с дробью", 1500.5m, SimpleJson.Number(back, "price", 0m));
            Check("логическое значение", "True", Convert.ToString(back["flag"]));
            Check("вложенный объект", "да", SimpleJson.Text(SimpleJson.Object(back, "object"), "вложено"));
            Check("элементов в списке", 3, SimpleJson.Array(back, "list").Count);

            Section("Настройки: запись и чтение");
            AppSettings settings = new AppSettings();
            settings.Theme = "dark";
            settings.Number = "42/2026";
            settings.Customer = "Иванов Иван Иванович";
            settings.Discount = 7.5m;
            settings.LastTemplate = "ТО-1";
            settings.Save();

            AppSettings loaded = AppSettings.Load();
            Check("тема сохранена", "dark", loaded.Theme);
            Check("номер сметы сохранён", "42/2026", loaded.Number);
            Check("ФИО сохранено", "Иванов Иван Иванович", loaded.Customer);
            Check("скидка сохранена", 7.5m, loaded.Discount);
            CheckTrue("файл настроек создан", File.Exists(AppSettings.Path), AppSettings.Path);
            loaded.ToggleTheme();
            Check("переключение темы", "light", loaded.Theme);

            Section("Шаблоны наборов услуг");
            ServiceTemplate oil = new ServiceTemplate();
            oil.Name = "ТО-1";
            oil.Items.Add(new TemplateItem { Group = "ТО", Article = "ART-101", Name = "Замена масла", Quantity = 1m });
            oil.Items.Add(new TemplateItem { Group = "ТО", Article = "ART-102", Name = "Масляный фильтр", Quantity = 1m });

            ServiceTemplate winter = new ServiceTemplate();
            winter.Name = "Шиномонтаж";
            winter.Items.Add(new TemplateItem { Group = "Колёса", Name = "Переобувка", Quantity = 4m });

            List<ServiceTemplate> templates = new List<ServiceTemplate>();
            templates = TemplateStore.AddOrReplace(templates, oil);
            templates = TemplateStore.AddOrReplace(templates, winter);
            Check("шаблонов сохранено", 2, templates.Count);

            // одноимённый шаблон заменяется
            ServiceTemplate oil2 = new ServiceTemplate();
            oil2.Name = "ТО-1";
            oil2.Items.Add(new TemplateItem { Name = "Замена масла", Quantity = 2m });
            templates = TemplateStore.AddOrReplace(templates, oil2);
            Check("одноимённый шаблон заменён", 2, templates.Count);
            Check("количество в новом шаблоне", 2m, templates[0].Items[0].Quantity);

            TemplateStore.Save(templates);
            List<ServiceTemplate> read = TemplateStore.Load();
            Check("шаблоны прочитаны", 2, read.Count);
            Check("имя шаблона", "ТО-1", read[0].Name);
            Check("позиций в шаблоне", 1, read[0].Items.Count);
            Check("количество сохранено", 2m, read[0].Items[0].Quantity);

            Section("Поиск по шаблонам");
            Check("по названию", 1, TemplateStore.Find(read, "шин").Count);
            Check("по наименованию услуги", 1, TemplateStore.Find(read, "масл").Count);
            Check("пустой запрос — все шаблоны", 2, TemplateStore.Find(read, "").Count);
            Check("нет совпадений", 0, TemplateStore.Find(read, "тормоз").Count);

            Section("Итог со скидкой и цена позиции в смете");
            List<ServiceItem> prices = PriceBook.ReadSeed();
            EstimateRow row = new EstimateRow(prices[0]);
            row.Selected = true;
            row.Quantity = 2m;
            Check("сумма без правки цены", prices[0].Price * 2m, row.Sum);
            CheckTrue("цена совпадает с прайсом", !row.PriceChanged, "PriceChanged = true");

            row.PriceOverride = 1000m;
            Check("цена изменена в смете", 1000m, row.Price);
            Check("сумма по новой цене", 2000m, row.Sum);
            CheckTrue("правка цены отмечена", row.PriceChanged, "PriceChanged = false");
            Check("цена прайса не изменилась", true, row.Item.Price != 1000m);

            DocumentFields fields = new DocumentFields();
            fields.Number = "  7/2026  ";
            fields.Customer = "  Петров П.П.  ";
            fields.Discount = 150m;
            fields.Normalize();
            Check("номер обрезан по краям", "7/2026", fields.Number);
            Check("ФИО обрезано по краям", "Петров П.П.", fields.Customer);
            Check("скидка ограничена сверху", 90m, fields.Discount);
            fields.Discount = -5m;
            fields.Normalize();
            Check("скидка ограничена снизу", 0m, fields.Discount);

            Section("Единый файл данных для обеих версий");
            string bundlePath = Path.Combine(work, "data" + DataExchange.Extension);
            DataExchange.Save(bundlePath, prices, settings, read);
            CheckTrue("файл данных создан", File.Exists(bundlePath), bundlePath);

            string error;
            DataBundle bundle = DataExchange.Load(bundlePath, out error);
            CheckTrue("файл прочитан без ошибок", bundle != null && error == null, error ?? "null");
            Check("позиций прайса в файле", prices.Count, bundle.Prices.Count);
            Check("шаблонов в файле", 2, bundle.Templates.Count);
            Check("тема из файла", "dark", bundle.Theme);
            Check("номер сметы из файла", "42/2026", bundle.Document.Number);
            Check("ФИО из файла", "Иванов Иван Иванович", bundle.Document.Customer);
            Check("скидка из файла", 7.5m, bundle.Document.Discount);
            Check("первая позиция совпадает", prices[0].Name, bundle.Prices[0].Name);
            Check("артикул совпадает", prices[0].Article, bundle.Prices[0].Article);

            Section("Чтение чужих и повреждённых файлов");
            string wrong = Path.Combine(work, "wrong.json");
            File.WriteAllText(wrong, "{ \"format\": \"другое\" }", new UTF8Encoding(false));
            DataBundle bad = DataExchange.Load(wrong, out error);
            CheckTrue("чужой формат отклонён", bad == null && error != null, error ?? "нет ошибки");

            string broken = Path.Combine(work, "broken.json");
            File.WriteAllText(broken, "{ это не json ", new UTF8Encoding(false));
            DataBundle brokenBundle = DataExchange.Load(broken, out error);
            CheckTrue("повреждённый файл отклонён", brokenBundle == null && error != null, error ?? "нет ошибки");

            CheckTrue("владелец файла данных указан", DataExchange.Format == "raschet-smeta", DataExchange.Format);

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ"); Environment.Exit(0); }
            Console.WriteLine("ПРОВАЛЕНО: " + _failed);
            Environment.Exit(1);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
