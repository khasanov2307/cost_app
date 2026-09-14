// ---------------------------------------------------------------------------
//  Единый файл данных «*.smeta»: прайс-лист, настройки и шаблоны наборов.
//
//  Один и тот же формат читают и настольная программа, и веб-версия,
//  поэтому его удобно использовать для резервной копии и для переноса
//  данных между версиями и компьютерами.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Что удалось прочитать из файла данных.</summary>
    internal sealed class DataBundle
    {
        public List<ServiceItem> Prices = new List<ServiceItem>();
        public List<ServiceTemplate> Templates = new List<ServiceTemplate>();
        public DocumentFields Document = new DocumentFields();
        public string Theme = "light";
        public bool HasPrices;
        public bool HasSettings;

        public string Summary()
        {
            return "позиций прайса: " + Prices.Count + ", шаблонов: " + Templates.Count;
        }
    }

    internal static class DataExchange
    {
        public const string Format = "raschet-smeta";
        public const int Version = 1;
        public const string Extension = ".smeta";

        /// <summary>Сборка файла данных.</summary>
        public static void Save(string path, IList<ServiceItem> prices,
                                AppSettings settings, IList<ServiceTemplate> templates)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = Format;
            root["version"] = Version;
            root["saved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            // --- настройки и реквизиты документа ---
            if (settings != null)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["theme"] = settings.Theme == "dark" ? "dark" : "light";
                map["number"] = settings.Number ?? "";
                map["customer"] = settings.Customer ?? "";
                map["discount"] = settings.Discount;
                map["template"] = settings.LastTemplate ?? "";
                root["settings"] = map;
            }

            // --- прайс-лист ---
            if (prices != null)
            {
                List<object> list = new List<object>();
                foreach (ServiceItem item in prices)
                {
                    SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                    map["group"] = item.Group ?? "";
                    map["article"] = item.Article ?? "";
                    map["name"] = item.Name ?? "";
                    map["unit"] = item.Unit ?? "";
                    map["price"] = item.Price;
                    list.Add(map);
                }
                root["prices"] = list;
            }

            // --- шаблоны наборов услуг ---
            if (templates != null)
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
                root["templates"] = list;
            }

            File.WriteAllText(path, SimpleJson.Write(root), new UTF8Encoding(true));
        }

        /// <summary>Чтение файла данных. При ошибке возвращает null и текст ошибки.</summary>
        public static DataBundle Load(string path, out string error)
        {
            error = null;

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                IDictionary<string, object> root = SimpleJson.Parse(text) as IDictionary<string, object>;

                if (root == null)
                {
                    error = "Файл не похож на данные программы: не удалось разобрать содержимое.";
                    return null;
                }

                string format = SimpleJson.Text(root, "format");
                if (format != Format)
                {
                    error = "Неизвестный формат файла: «" + format + "». Ожидается «" + Format + "».";
                    return null;
                }

                DataBundle bundle = new DataBundle();

                // --- настройки ---
                IDictionary<string, object> settings = SimpleJson.Object(root, "settings");
                if (settings != null)
                {
                    bundle.HasSettings = true;
                    bundle.Theme = SimpleJson.Text(settings, "theme") == "dark" ? "dark" : "light";
                    bundle.Document.Number = SimpleJson.Text(settings, "number");
                    bundle.Document.Customer = SimpleJson.Text(settings, "customer");
                    bundle.Document.Discount = AppSettings.ClampDiscount(SimpleJson.Number(settings, "discount", 0m));
                    bundle.Document.Normalize();
                }

                // --- прайс-лист ---
                List<object> prices = SimpleJson.Array(root, "prices");
                foreach (object entry in prices)
                {
                    IDictionary<string, object> map = entry as IDictionary<string, object>;
                    if (map == null) continue;

                    ServiceItem item = new ServiceItem();
                    item.Group = SimpleJson.Text(map, "group");
                    item.Article = SimpleJson.Text(map, "article");
                    item.Name = SimpleJson.Text(map, "name");
                    item.Unit = SimpleJson.Text(map, "unit");
                    item.Price = SimpleJson.Number(map, "price", 0m);

                    if (item.Group.Length == 0) item.Group = "Прочее";
                    if (item.Name.Length == 0) continue;
                    if (item.Unit.Length == 0) item.Unit = Uom.List[0];

                    bundle.Prices.Add(item);
                }
                bundle.HasPrices = prices.Count > 0;

                // --- шаблоны ---
                foreach (object entry in SimpleJson.Array(root, "templates"))
                {
                    IDictionary<string, object> map = entry as IDictionary<string, object>;
                    if (map == null) continue;

                    ServiceTemplate template = new ServiceTemplate();
                    template.Name = SimpleJson.Text(map, "name");
                    if (template.Name.Length == 0) continue;

                    DateTime saved;
                    if (DateTime.TryParse(SimpleJson.Text(map, "saved"), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out saved))
                        template.Saved = saved;

                    foreach (object itemEntry in SimpleJson.Array(map, "items"))
                    {
                        IDictionary<string, object> itemMap = itemEntry as IDictionary<string, object>;
                        if (itemMap == null) continue;

                        TemplateItem item = new TemplateItem();
                        item.Group = SimpleJson.Text(itemMap, "group");
                        item.Article = SimpleJson.Text(itemMap, "article");
                        item.Name = SimpleJson.Text(itemMap, "name");
                        item.Quantity = SimpleJson.Number(itemMap, "quantity", 1m);
                        if (item.Quantity <= 0m) item.Quantity = 1m;
                        if (item.Name.Length == 0) continue;

                        template.Items.Add(item);
                    }

                    if (template.Items.Count > 0) bundle.Templates.Add(template);
                }

                return bundle;
            }
            catch (Exception ex)
            {
                error = "Не удалось прочитать файл: " + ex.Message;
                return null;
            }
        }
    }
}