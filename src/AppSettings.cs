// ---------------------------------------------------------------------------
//  Настройки программы и шаблоны наборов услуг.
//
//  Настройки лежат в простом текстовом файле «settings.ini» (ключ=значение),
//  шаблоны — в «templates.json». Оба файла находятся рядом с хранилищем цен,
//  в личной папке программы.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    internal sealed class AppSettings
    {
        public string Theme = "light";          // light | dark
        public string Number = "";              // номер сметы
        public string Customer = "";            // ФИО заказчика
        public decimal Discount = 0m;           // скидка на всю смету, %
        public string LastTemplate = "";

        private const string FileName = "settings.ini";

        public static string Path
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, FileName); }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            if (!File.Exists(Path)) return settings;

            try
            {
                foreach (string line in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;

                    int at = line.IndexOf('=');
                    if (at <= 0) continue;

                    string key = line.Substring(0, at).Trim();
                    string value = line.Substring(at + 1).Trim();

                    switch (key)
                    {
                        case "theme": settings.Theme = value == "dark" ? "dark" : "light"; break;
                        case "number": settings.Number = value; break;
                        case "customer": settings.Customer = value; break;
                        case "discount":
                            decimal discount;
                            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out discount))
                                settings.Discount = ClampDiscount(discount);
                            break;
                        case "template": settings.LastTemplate = value; break;
                    }
                }
            }
            catch { /* настройки не критичны — работаем со значениями по умолчанию */ }

            return settings;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Настройки программы «Расчет сметы». Файл создаётся автоматически.");
                sb.AppendLine("theme=" + (Theme == "dark" ? "dark" : "light"));
                sb.AppendLine("number=" + Clean(Number));
                sb.AppendLine("customer=" + Clean(Customer));
                sb.AppendLine("discount=" + Discount.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("template=" + Clean(LastTemplate));

                File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { /* не удалось сохранить — не мешаем работе */ }
        }

        public static decimal ClampDiscount(decimal value)
        {
            if (value < 0m) return 0m;
            if (value > 90m) return 90m;
            return value;
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public bool IsDark { get { return Theme == "dark"; } }

        public void ToggleTheme()
        {
            Theme = IsDark ? "light" : "dark";
        }
    }

    /// <summary>Наборы услуг: сохранение, чтение, удаление.</summary>
    internal static class TemplateStore
    {
        private const string FileName = "templates.json";

        public static string Path
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, FileName); }
        }

        public static List<ServiceTemplate> Load()
        {
            List<ServiceTemplate> templates = new List<ServiceTemplate>();
            if (!File.Exists(Path)) return templates;

            try
            {
                string text = File.ReadAllText(Path, Encoding.UTF8);
                IDictionary<string, object> root = SimpleJson.Parse(text) as IDictionary<string, object>;
                if (root == null) return templates;

                foreach (object entry in SimpleJson.Array(root, "templates"))
                {
                    IDictionary<string, object> map = entry as IDictionary<string, object>;
                    if (map == null) continue;

                    ServiceTemplate template = new ServiceTemplate();
                    template.Name = SimpleJson.Text(map, "name");
                    template.Saved = DateTime.Now;

                    string saved = SimpleJson.Text(map, "saved");
                    DateTime parsed;
                    if (DateTime.TryParse(saved, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out parsed))
                        template.Saved = parsed;

                    foreach (object itemEntry in SimpleJson.Array(map, "items"))
                    {
                        IDictionary<string, object> itemMap = itemEntry as IDictionary<string, object>;
                        if (itemMap == null) continue;

                        TemplateItem item = new TemplateItem();
                        item.Group = SimpleJson.Text(itemMap, "group");
                        item.Article = SimpleJson.Text(itemMap, "article");
                        item.Name = SimpleJson.Text(itemMap, "name");
                        item.Quantity = SimpleJson.Number(itemMap, "quantity", 1m);

                        if (item.Name.Length == 0) continue;
                        if (item.Quantity <= 0m) item.Quantity = 1m;

                        template.Items.Add(item);
                    }

                    if (template.Name.Length == 0 || template.Items.Count == 0) continue;
                    templates.Add(template);
                }
            }
            catch { /* повреждённый файл — начнём с пустого списка */ }

            return templates;
        }

        public static void Save(IList<ServiceTemplate> templates)
        {
            List<object> list = new List<object>();

            foreach (ServiceTemplate template in templates)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["name"] = template.Name;
                map["saved"] = template.Saved.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                List<object> items = new List<object>();
                foreach (TemplateItem item in template.Items)
                {
                    SortedDictionary<string, object> itemMap =
                        new SortedDictionary<string, object>(StringComparer.Ordinal);
                    itemMap["group"] = item.Group;
                    itemMap["article"] = item.Article;
                    itemMap["name"] = item.Name;
                    itemMap["quantity"] = item.Quantity;
                    items.Add(itemMap);
                }
                map["items"] = items;

                list.Add(map);
            }

            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = "raschet-smeta-templates";
            root["version"] = 1;
            root["templates"] = list;

            File.WriteAllText(Path, SimpleJson.Write(root), new UTF8Encoding(true));
        }

        /// <summary>Сохраняет набор: одноимённый заменяется.</summary>
        public static List<ServiceTemplate> AddOrReplace(IList<ServiceTemplate> templates,
                                                         ServiceTemplate template)
        {
            List<ServiceTemplate> result = new List<ServiceTemplate>();
            bool replaced = false;

            foreach (ServiceTemplate existing in templates)
            {
                if (!replaced && string.Equals(existing.Name, template.Name,
                                                StringComparison.CurrentCultureIgnoreCase))
                {
                    result.Add(template);
                    replaced = true;
                    continue;
                }
                result.Add(existing);
            }

            if (!replaced) result.Add(template);
            return result;
        }

        /// <summary>Поиск шаблонов по части названия.</summary>
        public static List<ServiceTemplate> Find(IList<ServiceTemplate> templates, string query)
        {
            List<ServiceTemplate> found = new List<ServiceTemplate>();
            if (string.IsNullOrEmpty(query)) { found.AddRange(templates); return found; }

            string needle = query.Trim();
            foreach (ServiceTemplate template in templates)
            {
                if (template.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    TemplateItemsText(template).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    found.Add(template);
            }

            return found;
        }

        private static string TemplateItemsText(ServiceTemplate template)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TemplateItem item in template.Items)
            {
                sb.Append(item.Name).Append(' ').Append(item.Article).Append(' ');
            }
            return sb.ToString();
        }
    }
}