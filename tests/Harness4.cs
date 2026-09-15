// Проверки логотипа компании: чтение файла, размеры, вставка в документ Word.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using KotovCalc;

internal static class Harness4
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
        string work = Path.Combine(Path.GetTempPath(), "kotov-logo-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);
        PriceBook.StorePath = Path.Combine(work, "prices.xml");

        try
        {
            Console.WriteLine("[1] Чтение файла логотипа");

            string pngPath = Path.Combine(work, "logo.png");
            MakePng(pngPath, 240, 80);

            Logo logo = Logo.Read(pngPath);
            CheckTrue("PNG прочитан", logo != null, "null");
            Check("формат PNG", "png", logo.Extension);
            Check("тип содержимого", "image/png", logo.ContentType);
            Check("имя файла в архиве", "logo.png", logo.FileName);
            Check("ширина ограничена", 180, logo.Width);
            Check("высота сохранена пропорция", 60, logo.Height);
            CheckTrue("байты прочитаны", logo.Bytes.Length > 100, "мало байт");

            // маленькая картинка не растягивается
            string smallPng = Path.Combine(work, "small.png");
            MakePng(smallPng, 60, 20);
            Logo small = Logo.Read(smallPng);
            Check("маленькая картинка не растянута", 60, small.Width);
            Check("высота маленькой картинки", 20, small.Height);

            // очень высокая картинка ограничивается по высоте
            string tallPng = Path.Combine(work, "tall.png");
            MakePng(tallPng, 100, 400);
            Logo tall = Logo.Read(tallPng);
            Check("высокая картинка ограничена по высоте", 64, tall.Height);
            CheckTrue("ширина пересчитана", tall.Width < 100, "ширина: " + tall.Width);

            Console.WriteLine();
            Console.WriteLine("[2] Ошибочные файлы");

            CheckTrue("несуществующий файл", Logo.Read(Path.Combine(work, "нет.png")) == null, "не null");
            CheckTrue("пустой путь", Logo.Read("") == null, "не null");
            CheckTrue("путь null", Logo.Read(null) == null, "не null");

            string textPath = Path.Combine(work, "note.txt");
            File.WriteAllText(textPath, "это не картинка", new UTF8Encoding(false));
            CheckTrue("текстовый файл отклонён", Logo.Read(textPath) == null, "принят");

            // файл без расширения, но с содержимым PNG — формат определяется по содержимому
            string noExt = Path.Combine(work, "logoWithoutExtension");
            File.Copy(pngPath, noExt);
            Logo guessed = Logo.Read(noExt);
            CheckTrue("формат определён по содержимому", guessed != null, "null");
            if (guessed != null) Check("расширение угадано", "png", guessed.Extension);

            string wrongExt = Path.Combine(work, "logo.bmp");
            File.Copy(pngPath, wrongExt);
            Logo byContent = Logo.Read(wrongExt);
            CheckTrue("неверное расширение исправлено", byContent != null, "null");
            if (byContent != null) Check("формат по содержимому", "png", byContent.Extension);

            Console.WriteLine();
            Console.WriteLine("[3] Документ Word с логотипом");

            List<EstimateRow> rows = new List<EstimateRow>();
            foreach (ServiceItem item in PriceBook.ReadSeed())
            {
                EstimateRow row = new EstimateRow(item);
                row.Selected = true;
                rows.Add(row);
                if (rows.Count >= 5) break;
            }

            DocumentFields fields = new DocumentFields();
            fields.Number = "5/2026";
            fields.Customer = "Сидоров С.С.";

            EstimateDocument document = DocumentBuilder.Build(rows, fields, "", DateTime.Now);

            string withLogo = Path.Combine(work, "with-logo.docx");
            EstimateDocx.Save(withLogo, document, pngPath);

            string withoutLogo = Path.Combine(work, "without-logo.docx");
            EstimateDocx.Save(withoutLogo, document);

            CheckTrue("документ с логотипом создан", File.Exists(withLogo), "нет файла");
            CheckTrue("документ больше без логотипа", new FileInfo(withLogo).Length > new FileInfo(withoutLogo).Length,
                "размеры: " + new FileInfo(withLogo).Length + " и " + new FileInfo(withoutLogo).Length);

            using (ZipArchive zip = ZipFileOpen(withLogo))
            {
                CheckTrue("картинка в архиве", zip.GetEntry("word/media/logo.png") != null, "нет файла");
                CheckTrue("картинка не пустая", zip.GetEntry("word/media/logo.png").Length > 100, "пусто");

                string rels = Read(zip, "word/_rels/document.xml.rels");
                CheckTrue("связь рисунка объявлена", rels.Contains("rId2"), "нет rId2");
                CheckTrue("тип связи — рисунок", rels.Contains("relationships/image"), "нет типа");
                CheckTrue("цель связи верна", rels.Contains("media/logo.png"), "неверная цель");

                string types = Read(zip, "[Content_Types].xml");
                CheckTrue("тип png объявлен", types.Contains("image/png"), "нет типа png");

                string body = Read(zip, "word/document.xml");
                CheckTrue("разметка рисунка есть", body.Contains("wordprocessingDrawing"), "нет разметки");
                CheckTrue("ссылка rId2 в тексте", body.Contains("r:embed=\"rId2\""), "нет ссылки");
                CheckTrue("размер в EMU задан", body.Contains("wp:extent"), "нет размеров");
                CheckTrue("заголовок заявки на месте", body.Contains("ЗАЯВКА НА РАСЧЕТ"), "нет заголовка");
                CheckTrue("номер заявки на месте", body.Contains("5/2026"), "нет номера");
            }

            using (ZipArchive zip = ZipFileOpen(withoutLogo))
            {
                CheckTrue("без логотипа картинки нет", zip.GetEntry("word/media/logo.png") == null, "есть файл");
                string rels = Read(zip, "word/_rels/document.xml.rels");
                CheckTrue("без логотипа связи нет", !rels.Contains("rId2"), "есть связь");
                string body = Read(zip, "word/document.xml");
                CheckTrue("без логотипа разметки нет", !body.Contains("wordprocessingDrawing"), "есть разметка");
            }

            Console.WriteLine();
            Console.WriteLine("[4] Сохранение пути в настройках");

            AppSettings settings = AppSettings.Load();
            settings.Logo = pngPath;
            settings.Number = "5/2026";
            settings.Save();

            AppSettings read = AppSettings.Load();
            Check("путь к логотипу сохранён", pngPath, read.Logo);
            Check("номер сохранён вместе с логотипом", "5/2026", read.Number);

            settings.Logo = "";
            settings.Save();
            Check("пустой логотип сохраняется", "", AppSettings.Load().Logo);

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ ЛОГОТИПА ПРОЙДЕНЫ"); Environment.Exit(0); }
            Console.WriteLine("ПРОВАЛЕНО: " + _failed);
            Environment.Exit(1);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>Открытие архива только для чтения.</summary>
    private static ZipArchive ZipFileOpen(string path)
    {
        return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read), ZipArchiveMode.Read);
    }

    private static string Read(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name);
        if (entry == null) return "";

        using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
            return reader.ReadToEnd();
    }

    /// <summary>Простой PNG нужного размера — для проверок.</summary>
    private static void MakePng(string path, int width, int height)
    {
        using (Bitmap bitmap = new Bitmap(width, height))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(47, 111, 208));
            graphics.FillRectangle(Brushes.White, 4, 4, Math.Max(1, width / 3), Math.Max(1, height / 3));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
