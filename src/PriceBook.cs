// ---------------------------------------------------------------------------
//  Хранилище прайс-листа (справочника цен).
//
//  Прайс-лист хранится внутри приложения:
//    * исходный (заводской) набор цен зашит в exe как ресурс;
//    * изменения, сделанные в редакторе, сохраняются в личный профиль
//      пользователя — %LOCALAPPDATA%\Расчет заявки\prices.xml.
//  Отдельный файл рядом с программой не нужен: её можно положить в любую
//  папку (в том числе в Program Files) без прав администратора.
//
//  Для обмена с Excel сохранены импорт и экспорт формата TSV (табуляции).
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace KotovCalc
{
    internal static class PriceBook
    {
        /// <summary>Встроенный в сборку заводской набор цен.</summary>
        private const string SeedResource = "KotovCalc.Seed.tsv";

        /// <summary>Имя папки с данными программы в профиле пользователя.</summary>
        private const string AppDataFolderName = "Расчет заявки";

        private const string DefaultGroup = "Прочее";
        private const string DefaultUnit = "шт.";

        /// <summary>
        /// Расположение хранилища. По умолчанию — профиль пользователя;
        /// в автотестах подменяется на временную папку.
        /// </summary>
        public static string StorePath = DefaultStorePath();

        private static string DefaultStorePath()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppDataFolderName);
                return Path.Combine(folder, "prices.xml");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "prices.xml");
            }
        }

        /// <summary>Папка, в которой лежит программа (для поиска прежнего прайс-листа).</summary>
        public static string AppFolder
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>Папка личных данных программы (хранилище цен и состояние сеанса).</summary>
        public static string StoreFolder
        {
            get
            {
                string folder = Path.GetDirectoryName(StorePath);
                if (string.IsNullOrEmpty(folder)) folder = Path.GetTempPath();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                return folder;
            }
        }

        // ------------------------------------------------------- хранилище

        /// <summary>
        /// Загрузка прайс-листа: из хранилища, а при первом запуске — заводской набор
        /// (с переносом ранее сохранённого файла TSV, если он найден).
        /// Пустой прайс-лист — законное состояние: его вернули очисткой, и
        /// подменять его заводским набором нельзя.
        /// </summary>
        public static List<ServiceItem> Load(out string error)
        {
            error = null;

            // данные могли остаться в папке с прежним именем программы
            string migrated = MigrateFromOldFolder();
            if (migrated != null) error = migrated;

            if (File.Exists(StorePath))
            {
                try
                {
                    // файл есть — значит прайс-лист уже заведён, даже если он пуст
                    return ReadStore(StorePath);
                }
                catch (Exception ex)
                {
                    error = "Не удалось прочитать хранилище цен: " + ex.Message +
                            " Загружен заводской набор.";
                }
            }

            List<ServiceItem> items = ReadSeed();

            // перенос прежнего внешнего прайс-листа, если он остался от старой версии
            string legacy = FindLegacyFile();
            if (legacy != null)
            {
                try
                {
                    List<ServiceItem> old = Parse(File.ReadAllLines(legacy, Encoding.UTF8));
                    if (old.Count > 0)
                    {
                        items = old;
                        error = "Прежний прайс-лист перенесён во внутреннее хранилище: " + legacy;
                    }
                }
                catch (Exception ex)
                {
                    error = "Не удалось перенести прежний прайс-лист (" + legacy + "): " + ex.Message;
                }
            }

            try
            {
                Save(items);
            }
            catch (Exception ex)
            {
                error = "Не удалось создать хранилище цен: " + ex.Message;
            }

            return items;
        }

        /// <summary>Сохранение прайс-листа во внутреннее хранилище.</summary>
        public static void Save(IList<ServiceItem> items)
        {
            string folder = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // пишем через временный файл — чтобы хранилище не пострадало при сбое
            string temp = StorePath + ".tmp";

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = new UTF8Encoding(false);

            using (XmlWriter writer = XmlWriter.Create(temp, settings))
            {
                writer.WriteStartDocument();
                writer.WriteComment(" Прайс-лист программы «Расчет заявки». " +
                                    "Файл создаётся автоматически, править его вручную не нужно. ");
                writer.WriteStartElement("PriceList");
                writer.WriteAttributeString("version", "1");
                writer.WriteAttributeString("saved",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

                foreach (ServiceItem item in items)
                {
                    writer.WriteStartElement("Item");
                    writer.WriteElementString("Group", item.Group);
                    if (!string.IsNullOrEmpty(item.Article))
                        writer.WriteElementString("Article", item.Article);
                    writer.WriteElementString("Name", item.Name);
                    writer.WriteElementString("Unit", item.Unit);
                    writer.WriteElementString("Price",
                        item.Price.ToString("0.##", CultureInfo.InvariantCulture));
                    if (item.Cost > 0m)
                        writer.WriteElementString("Cost",
                            item.Cost.ToString("0.##", CultureInfo.InvariantCulture));
                    if (item.MinStock > 0m)
                        writer.WriteElementString("MinStock",
                            item.MinStock.ToString("0.###", CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            if (File.Exists(StorePath)) File.Delete(StorePath);
            File.Move(temp, StorePath);
        }

        private static List<ServiceItem> ReadStore(string path)
        {
            List<ServiceItem> items = new List<ServiceItem>();

            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlNode root = document.SelectSingleNode("PriceList");
            if (root == null) return items;

            foreach (XmlNode node in root.SelectNodes("Item"))
            {
                if (node == null) continue;

                ServiceItem item = new ServiceItem();
                item.Group = Text(node, "Group");
                item.Article = Text(node, "Article");
                item.Name = Text(node, "Name");
                item.Unit = Uom.Normalize(Text(node, "Unit"));

                decimal price = 0m;
                Fmt.TryParseDecimal(Text(node, "Price"), out price);
                item.Price = price;

                decimal cost = 0m;
                Fmt.TryParseDecimal(Text(node, "Cost"), out cost);
                item.Cost = cost;

                decimal minStock = 0m;
                Fmt.TryParseDecimal(Text(node, "MinStock"), out minStock);
                item.MinStock = minStock;

                Normalize(item);
                if (item.Name.Length == 0) continue;

                items.Add(item);
            }

            return items;
        }

        private static string Text(XmlNode parent, string child)
        {
            XmlNode node = parent.SelectSingleNode(child);
            return node == null || node.InnerText == null ? "" : node.InnerText.Trim();
        }

        private static void Normalize(ServiceItem item)
        {
            if (item.Group == null) item.Group = "";
            if (item.Name == null) item.Name = "";
            if (item.Unit == null) item.Unit = "";

            item.Group = item.Group.Trim();
            item.Name = item.Name.Trim();
            item.Unit = item.Unit.Trim();

            if (item.Group.Length == 0) item.Group = DefaultGroup;
            item.Unit = Uom.Normalize(item.Unit);
        }

        /// <summary>Существует ли хранилище (используется в подсказках интерфейса).</summary>
        public static bool StoreExists
        {
            get { return File.Exists(StorePath); }
        }

        /// <summary>Заводской набор цен, встроенный в программу.</summary>
        public static List<ServiceItem> ReadSeed()
        {
            return Parse(BuildDefaultText().Replace("\r\n", "\n").Split('\n'));
        }

        /// <summary>Восстановление заводского набора цен.</summary>
        public static List<ServiceItem> RestoreDefaults()
        {
            List<ServiceItem> items = ReadSeed();
            Save(items);
            return items;
        }

        /// <summary>
        /// Переносит прайс-лист из папки с прежним именем программы, если он там остался.
        /// Возвращает сообщение о переносе или null.
        /// </summary>
        private static string MigrateFromOldFolder()
        {
            try
            {
                if (File.Exists(StorePath)) return null;       // уже есть свои данные

                string parent = Path.GetDirectoryName(Path.GetDirectoryName(StorePath));
                if (string.IsNullOrEmpty(parent)) return null;

                string oldFolder = Path.Combine(parent, "Калькулятор услуг");
                string oldStore = Path.Combine(oldFolder, "prices.xml");
                if (!File.Exists(oldStore)) return null;

                List<ServiceItem> items = ReadStore(oldStore);
                if (items.Count == 0) return null;

                Save(items);
                return "Прайс-лист перенесён из прежней папки программы: " + oldStore;
            }
            catch { return null; }                             // перенос не критичен
        }

        /// <summary>Прежний внешний прайс-лист (до перехода на внутреннее хранилище).</summary>
        private static string FindLegacyFile()
        {
            try
            {
                string local = Path.Combine(AppFolder, "Цены_услуг.tsv");
                if (File.Exists(local)) return local;

                DirectoryInfo parent = Directory.GetParent(AppFolder);
                if (parent != null)
                {
                    string upper = Path.Combine(parent.FullName, "Цены_услуг.tsv");
                    if (File.Exists(upper)) return upper;
                }
            }
            catch { /* путь недоступен — не критично */ }

            return null;
        }

        // ------------------------------------------- текст TSV (встроенный/обмен)

        /// <summary>Разбор строк справочника в формате TSV.</summary>
        public static List<ServiceItem> Parse(string[] lines)
        {
            List<ServiceItem> items = new List<ServiceItem>();

            foreach (string raw in lines)
            {
                if (raw == null) continue;
                // \uFEFF может оказаться внутри первой строки, если данные пришли из ресурса
                string line = raw.TrimStart('\uFEFF', '\u200B').Trim('\r', '\n');
                if (line.Trim().Length == 0) continue;
                if (line.TrimStart().StartsWith("#")) continue;       // комментарий

                char separator = line.IndexOf('\t') >= 0 ? '\t' : ';';
                string[] parts = line.Split(separator);
                if (parts.Length < 2) continue;

                ServiceItem item = new ServiceItem();
                item.Group = parts[0].Trim();

                if (parts.Length >= 5)
                {
                    // формат: Группа, Артикул, Наименование, Ед. изм., Цена[, Закупка[, Минимум]]
                    item.Article = parts[1].Trim();
                    item.Name = parts[2].Trim();
                    item.Unit = parts[3].Trim();

                    decimal newPrice = 0m;
                    Fmt.TryParseDecimal(parts[4], out newPrice);
                    item.Price = newPrice;

                    if (parts.Length > 5)
                    {
                        decimal newCost = 0m;
                        Fmt.TryParseDecimal(parts[5], out newCost);
                        item.Cost = newCost;
                    }

                    if (parts.Length > 6)
                    {
                        decimal minStock = 0m;
                        Fmt.TryParseDecimal(parts[6], out minStock);
                        item.MinStock = minStock;
                    }
                }
                else
                {
                    // прежний формат: Группа, Наименование, Ед. изм., Цена
                    item.Name = parts[1].Trim();
                    item.Unit = parts.Length > 2 ? parts[2].Trim() : "";

                    decimal price = 0m;
                    if (parts.Length > 3) Fmt.TryParseDecimal(parts[3], out price);
                    item.Price = price;
                }

                Normalize(item);
                if (item.Name.Length == 0) continue;

                items.Add(item);
            }

            return items;
        }

        /// <summary>Чтение внешнего файла TSV (импорт прайс-листа).</summary>
        public static List<ServiceItem> ImportTsv(string path)
        {
            return Parse(File.ReadAllLines(path, Encoding.UTF8));
        }

        /// <summary>Запись прайс-листа во внешний файл TSV (экспорт для Excel).</summary>
        public static void ExportTsv(string path, IList<ServiceItem> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Прайс-лист программы «Расчет заявки» (экспорт).");
            sb.AppendLine("# Разделитель колонок — знак табуляции.");
            sb.AppendLine("# Группа\tАртикул\tНаименование\tЕд. изм.\tЦена");

            string currentGroup = null;
            foreach (ServiceItem item in items)
            {
                if (item.Group != currentGroup)
                {
                    if (currentGroup != null) sb.AppendLine();
                    currentGroup = item.Group;
                }
                sb.Append(item.Group).Append('\t')
                  .Append(item.Article ?? "").Append('\t')
                  .Append(item.Name).Append('\t')
                  .Append(item.Unit).Append('\t')
                  .AppendLine(item.Price.ToString("0.##", CultureInfo.InvariantCulture));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // ------------------------------------------------- встроенный шаблон

        public static string BuildDefaultText()
        {
            string embedded = ReadEmbeddedSeed();
            if (embedded != null) return embedded;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Группа\tНаименование\tЕд. изм.\tЦена");
            sb.AppendLine("Диагностика\tКомпьютерная диагностика двигателя\tуслуга\t1500");
            sb.AppendLine("Диагностика\tДиагностика ходовой части\tуслуга\t900");
            sb.AppendLine("ТО\tЗамена моторного масла и фильтра\tуслуга\t1200");
            sb.AppendLine("Ремонт\tЗамена тормозных колодок (передняя ось)\tуслуга\t1800");
            sb.AppendLine("Шиномонтаж\tШиномонтаж и балансировка\tколесо\t700");
            return sb.ToString();
        }

        private static string ReadEmbeddedSeed()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(SeedResource))
                {
                    if (stream == null) return null;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
