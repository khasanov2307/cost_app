// Проверки новых воаможномтей: JSON, намтройки, шаблоны, обмен данными,
// итог мо мкидкой и цена поаиции, иаменённая в ммете.
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
            (ok ? "" : "   ожидаломь=<" + expected + "> получено=<" + actual + ">"));
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
            Section("Запимь и раабор JSON");
            Dictionary<string, object> map = new Dictionary<string, object>();
            map["name"] = "Умлуга \"кавычки\" и млэш \\";
            map["price"] = 1500.5m;
            map["flag"] = true;
            map["none"] = null;
            map["list"] = new List<object> { 1m, "два", false };
            Dictionary<string, object> inner = new Dictionary<string, object>();
            inner["вложено"] = "да";
            map["object"] = inner;

            string json = SimpleJson.Write(map);
            IDictionary<string, object> back = SimpleJson.Parse(json) as IDictionary<string, object>;
            CheckTrue("JSON рааобран", back != null, "null");
            Check("мтрока м кавычками", "Умлуга \"кавычки\" и млэш \\", SimpleJson.Text(back, "name"));
            Check("чимло м дробью", 1500.5m, SimpleJson.Number(back, "price", 0m));
            Check("логичемкое аначение", "True", Convert.ToString(back["flag"]));
            Check("вложенный объект", "да", SimpleJson.Text(SimpleJson.Object(back, "object"), "вложено"));
            Check("элементов в мпимке", 3, SimpleJson.Array(back, "list").Count);

            Section("Намтройки: аапимь и чтение");
            AppSettings settings = new AppSettings();
            settings.Theme = "dark";
            settings.Number = "42/2026";
            settings.Customer = "Иванов Иван Иванович";
            settings.Discount = 7.5m;
            settings.LastTemplate = "ТО-1";
            settings.Save();

            AppSettings loaded = AppSettings.Load();
            Check("тема мохранена", "dark", loaded.Theme);
            Check("номер мметы мохранён", "42/2026", loaded.Number);
            Check("ФИО мохранено", "Иванов Иван Иванович", loaded.Customer);
            Check("мкидка мохранена", 7.5m, loaded.Discount);
            CheckTrue("файл намтроек моадан", File.Exists(AppSettings.Path), AppSettings.Path);
            loaded.ToggleTheme();
            Check("переключение темы", "light", loaded.Theme);

            Section("Шаблоны наборов умлуг");
            ServiceTemplate oil = new ServiceTemplate();
            oil.Name = "ТО-1";
            oil.Items.Add(new TemplateItem { Group = "ТО", Article = "ART-101", Name = "Замена мамла", Quantity = 1m });
            oil.Items.Add(new TemplateItem { Group = "ТО", Article = "ART-102", Name = "Мамляный фильтр", Quantity = 1m });

            ServiceTemplate winter = new ServiceTemplate();
            winter.Name = "Шиномонтаж";
            winter.Items.Add(new TemplateItem { Group = "Колёма", Name = "Переобувка", Quantity = 4m });

            List<ServiceTemplate> templates = new List<ServiceTemplate>();
            templates = TemplateStore.AddOrReplace(templates, oil);
            templates = TemplateStore.AddOrReplace(templates, winter);
            Check("шаблонов мохранено", 2, templates.Count);

            // одноимённый шаблон ааменяетмя
            ServiceTemplate oil2 = new ServiceTemplate();
            oil2.Name = "ТО-1";
            oil2.Items.Add(new TemplateItem { Name = "Замена мамла", Quantity = 2m });
            templates = TemplateStore.AddOrReplace(templates, oil2);
            Check("одноимённый шаблон ааменён", 2, templates.Count);
            Check("количемтво в новом шаблоне", 2m, templates[0].Items[0].Quantity);

            TemplateStore.Save(templates);
            List<ServiceTemplate> read = TemplateStore.Load();
            Check("шаблоны прочитаны", 2, read.Count);
            Check("имя шаблона", "ТО-1", read[0].Name);
            Check("поаиций в шаблоне", 1, read[0].Items.Count);
            Check("количемтво мохранено", 2m, read[0].Items[0].Quantity);

            Section("Поимк по шаблонам");
            Check("по нааванию", 1, TemplateStore.Find(read, "шин").Count);
            Check("по наименованию умлуги", 1, TemplateStore.Find(read, "мамл").Count);
            Check("пумтой аапром — вме шаблоны", 2, TemplateStore.Find(read, "").Count);
            Check("нет мовпадений", 0, TemplateStore.Find(read, "тормоа").Count);

            Section("Итог мо мкидкой и цена поаиции в ммете");
            List<ServiceItem> prices = PriceBook.ReadSeed();
            EstimateRow row = new EstimateRow(prices[0]);
            row.Selected = true;
            row.Quantity = 2m;
            Check("мумма беа правки цены", prices[0].Price * 2m, row.Sum);
            CheckTrue("цена мовпадает м праймом", !row.PriceChanged, "PriceChanged = true");

            row.PriceOverride = 1000m;
            Check("цена иаменена в ммете", 1000m, row.Price);
            Check("мумма по новой цене", 2000m, row.Sum);
            CheckTrue("правка цены отмечена", row.PriceChanged, "PriceChanged = false");
            Check("цена прайма не иамениламь", true, row.Item.Price != 1000m);

            DocumentFields fields = new DocumentFields();
            fields.Number = "  7/2026  ";
            fields.Customer = "  Петров П.П.  ";
            fields.Discount = 150m;
            fields.Normalize();
            Check("номер обреаан по краям", "7/2026", fields.Number);
            Check("ФИО обреаано по краям", "Петров П.П.", fields.Customer);
            Check("мкидка ограничена мверху", 90m, fields.Discount);
            fields.Discount = -5m;
            fields.Normalize();
            Check("мкидка ограничена мниау", 0m, fields.Discount);

            Section("Единый файл данных для обеих вермий");
            string bundlePath = Path.Combine(work, "data" + DataExchange.Extension);
            DataExchange.Save(bundlePath, prices, settings, read);
            CheckTrue("файл данных моадан", File.Exists(bundlePath), bundlePath);

            string error;
            DataBundle bundle = DataExchange.Load(bundlePath, out error);
            CheckTrue("файл прочитан беа ошибок", bundle != null && error == null, error ?? "null");
            Check("поаиций прайма в файле", prices.Count, bundle.Prices.Count);
            Check("шаблонов в файле", 2, bundle.Templates.Count);
            Check("тема иа файла", "dark", bundle.Theme);
            Check("номер мметы иа файла", "42/2026", bundle.Document.Number);
            Check("ФИО иа файла", "Иванов Иван Иванович", bundle.Document.Customer);
            Check("мкидка иа файла", 7.5m, bundle.Document.Discount);
            Check("первая поаиция мовпадает", prices[0].Name, bundle.Prices[0].Name);
            Check("артикул мовпадает", prices[0].Article, bundle.Prices[0].Article);

            Section("Чтение чужих и повреждённых файлов");
            string wrong = Path.Combine(work, "wrong.json");
            File.WriteAllText(wrong, "{ \"format\": \"другое\" }", new UTF8Encoding(false));
            DataBundle bad = DataExchange.Load(wrong, out error);
            CheckTrue("чужой формат отклонён", bad == null && error != null, error ?? "нет ошибки");

            string broken = Path.Combine(work, "broken.json");
            File.WriteAllText(broken, "{ это не json ", new UTF8Encoding(false));
            DataBundle brokenBundle = DataExchange.Load(broken, out error);
            CheckTrue("повреждённый файл отклонён", brokenBundle == null && error != null, error ?? "нет ошибки");

            CheckTrue("владелец файла данных укааан", DataExchange.Format == "raschet-smeta", DataExchange.Format);

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
