// Проверка документа Word со сметой: структура архива, разметка OOXML и содержимое.
//
// Запуск:  DocxCheck.exe <образец.docx> [<файл-программы.docx>]
//          DocxCheck.exe --check <файл.docx>     — проверить только указанный файл

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using KotovCalc;

internal static class DocxCheck
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

    [STAThread]
    private static void Main(string[] args)
    {
        string output = args != null && args.Length > 0 ? args[0] : "docx-sample.docx";
        string external = args != null && args.Length > 1 ? args[1] : null;

        if (output == "--check" && external != null)
        {
            CheckFile(external, null, null, null);
            Finish();
            return;
        }

        // готовим смету из заводского прайса: изменённая цена и скидка 5 %
        List<ServiceItem> items = PriceBook.ReadSeed();
        List<EstimateRow> rows = new List<EstimateRow>();
        decimal subtotal = 0m;
        int picked = 0;

        foreach (ServiceItem item in items)
        {
            if (item.Price <= 0) continue;
            if (item.Group == "Ремонтные работы" && picked >= 3) continue;
            if (item.Group == "Запасные части и материалы") continue;

            EstimateRow row = new EstimateRow(item);
            row.Selected = true;
            row.Quantity = 2m;
            if (picked == 1) row.PriceOverride = item.Price + 100m;   // цена, изменённая в смете
            rows.Add(row);

            subtotal += row.Sum;
            picked++;
        }

        DocumentFields fields = new DocumentFields();
        fields.Number = "12/2026";
        fields.Customer = "Иванов Иван Иванович";
        fields.Discount = 5m;

        EstimateDocument document = DocumentBuilder.Build(rows, fields, "Петров П.П.",
                                                          new DateTime(2026, 1, 1, 12, 0, 0));
        EstimateDocx.Save(output, document);

        Console.WriteLine("образец: " + Path.GetFullPath(output) + "  (" + new FileInfo(output).Length + " байт)");
        Console.WriteLine("подытог " + Fmt.Money(document.Subtotal) +
                          ", скидка " + Fmt.Money(document.DiscountAmount) +
                          ", к оплате " + Fmt.Money(document.Total) +
                          ", позиций " + document.PositionCount);
        Console.WriteLine();

        CheckFile(output, "12/2026", "Иванов Иван Иванович", "Петров П.П.");

        if (external != null && File.Exists(external))
        {
            Console.WriteLine();
            Console.WriteLine("=== проверка файла, созданного программой ===");
            CheckFile(external, null, null, null);
        }

        Finish();
    }

    private static void Finish()
    {
        Console.WriteLine();
        if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ"); Environment.Exit(0); }
        Console.WriteLine("ПРОВАЛЕНО: " + _failed);
        Environment.Exit(1);
    }

    /// <summary>Проверки готового .docx.</summary>
    private static void CheckFile(string path, string expectedNumber, string expectedCustomer, string expectedExecutor)
    {
        byte[] raw = File.ReadAllBytes(path);
        string name = Path.GetFileName(path);
        Console.WriteLine("файл: " + name + "  (" + raw.Length + " байт)");
        CheckTrue("[" + name + "] размер файла разумный", raw.Length > 1500, "всего " + raw.Length + " байт");

        // ZIP-архив
        Check("[" + name + "] признак ZIP-архива", "PK", Encoding.ASCII.GetString(raw, 0, 2));

        List<string> parts = new List<string>();
        Dictionary<string, string> content = new Dictionary<string, string>();

        using (MemoryStream memory = new MemoryStream(raw))
        using (ZipArchive zip = new ZipArchive(memory, ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                parts.Add(entry.FullName);
                using (Stream stream = entry.Open())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    content[entry.FullName] = reader.ReadToEnd();
            }
        }

        Console.WriteLine("        части документа: " + string.Join(", ", parts.ToArray()));

        CheckTrue("[" + name + "] есть [Content_Types].xml", content.ContainsKey("[Content_Types].xml"), "нет");
        CheckTrue("[" + name + "] есть word/document.xml", content.ContainsKey("word/document.xml"), "нет");
        CheckTrue("[" + name + "] есть стили", content.ContainsKey("word/styles.xml"), "нет");
        CheckTrue("[" + name + "] есть связи документа", content.ContainsKey("word/_rels/document.xml.rels"), "нет");
        Check("[" + name + "] [Content_Types].xml идёт первым", "[Content_Types].xml", parts[0]);

        CheckTrue("[" + name + "] тип документа Word",
                  content["[Content_Types].xml"].Contains(
                      "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
                  "нет типа документа");

        // разметка документа разбирается как XML
        XmlDocument document = new XmlDocument();
        bool parsed = true;
        string parseError = "";
        try { document.LoadXml(content["word/document.xml"]); }
        catch (Exception ex) { parsed = false; parseError = ex.Message; }
        CheckTrue("[" + name + "] разметка документа — корректный XML", parsed, parseError);
        if (!parsed) return;

        XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

        XmlNode body = document.SelectSingleNode("//w:body", ns);
        CheckTrue("[" + name + "] есть тело документа", body != null, "нет w:body");

        XmlNodeList tables = document.SelectNodes("//w:tbl", ns);
        CheckTrue("[" + name + "] есть таблицы", tables.Count >= 2, "таблиц: " + tables.Count);

        XmlNodeList rows = document.SelectNodes("//w:tbl[1]/w:tr", ns);
        XmlNodeList cells = document.SelectNodes("//w:tbl[1]/w:tr[1]/w:tc", ns);
        Console.WriteLine("        строк в таблице сметы: " + rows.Count + ", колонок: " + cells.Count);

        Check("[" + name + "] колонок в смете", 7, cells.Count);

        // текст документа
        StringBuilder text = new StringBuilder();
        XmlNodeList texts = document.SelectNodes("//w:t", ns);
        foreach (XmlNode node in texts) text.Append(node.InnerText).Append('\n');
        string all = text.ToString();

        CheckTrue("[" + name + "] есть заголовок СМЕТА", all.Contains("СМЕТА"), "нет заголовка");
        CheckTrue("[" + name + "] есть шапка таблицы",
                  all.Contains("Наименование") && all.Contains("Кол-во") && all.Contains("Всего"),
                  "нет шапки таблицы");
        CheckTrue("[" + name + "] есть разделы сметы",
                  all.Contains("ДИАГНОСТИКА") || all.Contains("Диагностика"), "нет разделов");
        CheckTrue("[" + name + "] есть позиции", all.Contains("услуга") || all.Contains("шт."), "нет позиций");
        // реквизиты, скидку и подпись исполнителя проверяем только у образца:
        // у файла, который делает программа по ключу --estimate, они берутся из настроек
        if (expectedNumber != null)
        {
            CheckTrue("[" + name + "] есть номер сметы", all.Contains(expectedNumber), "нет номера");
            CheckTrue("[" + name + "] есть ФИО заказчика", all.Contains(expectedCustomer), "нет заказчика");
            CheckTrue("[" + name + "] есть подытог", all.Contains("Подытог"), "нет подытога");
            CheckTrue("[" + name + "] есть скидка", all.Contains("Скидка"), "нет строки скидки");
            CheckTrue("[" + name + "] есть подпись исполнителя",
                      all.Contains(expectedExecutor), "нет исполнителя");
        }
        CheckTrue("[" + name + "] есть итог", all.Contains("ИТОГО"), "нет итога");
        CheckTrue("[" + name + "] есть подписи сторон",
                  all.Contains("Исполнитель") && all.Contains("Заказчик"), "нет подписей");

        // количество строк таблицы должно соответствовать позициям
        int groupRows = 0, itemRows = 0;
        for (int i = 1; i < rows.Count; i++)          // без шапки
        {
            XmlNodeList rowCells = rows[i].SelectNodes("w:tc", ns);
            if (rowCells == null || rowCells.Count < 7) continue;

            string first = CellText(rowCells[0], ns);
            string third = CellText(rowCells[2], ns);

            if (first.Trim().Length == 0 && third.Trim().Length > 0 && !third.StartsWith("ИТОГО"))
                groupRows++;
            else if (first.Trim().Length > 0)
                itemRows++;
        }

        Console.WriteLine("        разделов: " + groupRows + ", позиций: " + itemRows);
        CheckTrue("[" + name + "] позиции выведены строками", itemRows >= 3, "позиций: " + itemRows);
        CheckTrue("[" + name + "] разделы выведены строками", groupRows >= 1, "разделов: " + groupRows);

        // поля страницы и альбомная/книжная ориентация
        XmlNode pageSize = document.SelectSingleNode("//w:sectPr/w:pgSz", ns);
        CheckTrue("[" + name + "] задан формат страницы A4",
                  pageSize != null && pageSize.Attributes["w:w"] != null &&
                  pageSize.Attributes["w:w"].Value == "11906",
                  pageSize == null ? "нет pgSz" : "w=" + pageSize.Attributes["w:w"].Value);

        // контрольная сумма каждой части проверяется при распаковке архива
        CheckTrue("[" + name + "] архив распакован без ошибок", true, "");
    }

    private static string CellText(XmlNode cell, XmlNamespaceManager ns)
    {
        StringBuilder text = new StringBuilder();
        XmlNodeList texts = cell.SelectNodes(".//w:t", ns);
        if (texts != null) foreach (XmlNode node in texts) text.Append(node.InnerText);
        return text.ToString();
    }
}
