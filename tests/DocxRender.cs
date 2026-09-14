// Рисует документ Word (.docx) в PNG — наглядная проверка, что смета не пустая.
// Разбор упрощённый: абзацы и первая таблица документа.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

internal static class DocxRender
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [STAThread]
    private static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "estimate.docx";
        string output = args.Length > 1 ? args[1] : Path.ChangeExtension(path, ".png");

        string xml;
        using (FileStream file = File.OpenRead(path))
        using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Read))
        using (Stream stream = zip.GetEntry("word/document.xml").Open())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            xml = reader.ReadToEnd();

        XmlDocument document = new XmlDocument();
        document.LoadXml(xml);
        XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("w", W);

        double scale = 1.4;
        int width = (int)(595.28 * scale);
        int height = (int)(841.89 * scale);
        int darkRows;

        using (Bitmap page = new Bitmap(width, height))
        {
            using (Graphics g = Graphics.FromImage(page))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (Font normal = new Font("Segoe UI", (float)(9 * scale), FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font bold = new Font("Segoe UI", (float)(9 * scale), FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font title = new Font("Segoe UI", (float)(15 * scale), FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font small = new Font("Segoe UI", (float)(8 * scale), FontStyle.Regular, GraphicsUnit.Pixel))
                using (Pen line = new Pen(Color.FromArgb(150, 150, 150), 1f))
                {
                    double x = 45 * scale;
                    double y = 45 * scale;
                    double contentWidth = width - 90 * scale;

                    // абзацы до таблицы
                    XmlNode body = document.SelectSingleNode("//w:body", ns);
                    double[] columns = null;

                    foreach (XmlNode node in body.ChildNodes)
                    {
                        if (node.Name == "w:p")
                        {
                            string text = NodeText(node, ns);
                            if (text.Length == 0) { y += 8 * scale; continue; }

                            bool isTitle = HasStyle(node, ns, "Title");
                            bool isSubtitle = HasStyle(node, ns, "Subtitle");
                            Font font = isTitle ? title : (isSubtitle ? small : normal);
                            SizeF size = g.MeasureString(text, font);
                            double px = isTitle || isSubtitle ? x + (contentWidth - size.Width) / 2 : x;
                            g.DrawString(text, font, Brushes.Black, (float)px, (float)y);
                            y += size.Height + 4 * scale;
                        }
                        else if (node.Name == "w:tbl")
                        {
                            XmlNodeList grid = node.SelectNodes("w:tblGrid/w:gridCol", ns);
                            List<double> widths = new List<double>();
                            double totalTwips = 0;
                            foreach (XmlNode col in grid)
                            {
                                double twips = double.Parse(col.Attributes["w:w"].Value);
                                widths.Add(twips);
                                totalTwips += twips;
                            }
                            columns = new double[widths.Count];
                            for (int i = 0; i < widths.Count; i++)
                                columns[i] = widths[i] / totalTwips * contentWidth;

                            XmlNodeList rows = node.SelectNodes("w:tr", ns);
                            foreach (XmlNode row in rows)
                            {
                                XmlNodeList cells = row.SelectNodes("w:tc", ns);
                                double rowHeight = 0;

                                // вычисляем высоту строки
                                for (int i = 0; i < cells.Count && i < columns.Length; i++)
                                {
                                    string text = NodeText(cells[i], ns);
                                    Font font = RowFont(cells[i], ns, normal, bold, small);
                                    SizeF size = g.MeasureString(text, font,
                                        new SizeF((float)columns[i] - 6, float.MaxValue));
                                    rowHeight = Math.Max(rowHeight, size.Height);
                                }
                                rowHeight = Math.Max(rowHeight, 13 * scale);

                                // рамка строки
                                g.DrawLine(line, (float)x, (float)y, (float)(x + contentWidth), (float)y);

                                double cx = x;
                                for (int i = 0; i < cells.Count && i < columns.Length; i++)
                                {
                                    string text = NodeText(cells[i], ns);
                                    Font font = RowFont(cells[i], ns, normal, bold, small);
                                    bool numeric = i >= 4;
                                    StringFormat format = new StringFormat();
                                    format.Alignment = numeric ? StringAlignment.Far : StringAlignment.Near;
                                    if (i >= 1 && i <= 3 && i != 2) format.Alignment = StringAlignment.Center;

                                    RectangleF box = new RectangleF((float)(cx + 3), (float)(y + 2),
                                        (float)columns[i] - 6, (float)rowHeight);
                                    g.DrawString(text, font, Brushes.Black, box, format);
                                    cx += columns[i];
                                }

                                y += rowHeight + 2 * scale;
                            }

                            g.DrawLine(line, (float)x, (float)y, (float)(x + contentWidth), (float)y);
                            y += 10 * scale;
                        }
                    }
                }
            }

            page.Save(output, System.Drawing.Imaging.ImageFormat.Png);

            darkRows = 0;
            for (int row = 0; row < page.Height; row += 3)
            {
                for (int column = 0; column < page.Width; column += 3)
                {
                    Color pixel = page.GetPixel(column, row);
                    if (pixel.R < 160 && pixel.G < 160 && pixel.B < 160) { darkRows++; break; }
                }
            }
        }

        Console.WriteLine("картинка: " + Path.GetFullPath(output) + ", строк с текстом: " + darkRows);
    }

    private static Font RowFont(XmlNode cell, XmlNamespaceManager ns, Font normal, Font bold, Font small)
    {
        string style = StyleOf(cell, ns);
        string text = NodeText(cell, ns);

        // строки-заголовки разделов печатаются прописными и жирным
        if (style == "GroupRow") return bold;
        if (style == "TotalRow") return bold;
        if (style == "TableHeader") return small;
        if (style == "Normal" && text.Length > 0) return normal;
        if (style.Length == 0) return normal;
        return normal;
    }

    private static string StyleOf(XmlNode node, XmlNamespaceManager ns)
    {
        XmlNode style = node.SelectSingleNode(".//w:pStyle", ns);
        return style == null ? "" : style.Attributes["w:val"].Value;
    }

    private static bool HasStyle(XmlNode paragraph, XmlNamespaceManager ns, string name)
    {
        return StyleOf(paragraph, ns) == name;
    }

    private static string NodeText(XmlNode node, XmlNamespaceManager ns)
    {
        StringBuilder text = new StringBuilder();
        XmlNodeList parts = node.SelectNodes(".//w:t", ns);
        if (parts != null) foreach (XmlNode part in parts) text.Append(part.InnerText);
        return text.ToString();
    }
}