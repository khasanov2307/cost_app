// ---------------------------------------------------------------------------
//  Сохранение сметы в формате Word (.docx) без внешних библиотек.
//
//  Файл .docx — это ZIP-архив с XML внутри (формат OOXML). Архив собирается
//  средствами System.IO.Compression, разметка — обычным текстом, поэтому
//  Word, «Word Online» и LibreOffice открывают документ без замечаний.
//
//  Содержимое документа готовит DocumentBuilder — тот же, что и для
//  предпросмотра в программе, поэтому вид сметы совпадает.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KotovCalc
{
    internal static class EstimateDocx
    {
        /// <summary>Создание документа Word со сметой.</summary>
        public static void Save(string path, EstimateDocument document)
        {
            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                // [Content_Types].xml обязан идти в архиве первым
                Add(zip, "[Content_Types].xml", ContentTypes());
                Add(zip, "_rels/.rels", RootRels());
                Add(zip, "word/document.xml", BuildDocument(document));
                Add(zip, "word/styles.xml", Styles());
                Add(zip, "word/_rels/document.xml.rels", DocumentRels());
            }
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            {
                byte[] mark = new byte[] { 0xEF, 0xBB, 0xBF };      // BOM: Word так надёжнее
                stream.Write(mark, 0, mark.Length);

                byte[] text = Encoding.UTF8.GetBytes(content);
                stream.Write(text, 0, text.Length);
            }
        }

        // ------------------------------------------------------- части пакета

        private static string ContentTypes()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                "</Types>";
        }

        private static string RootRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>";
        }

        private static string DocumentRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>";
        }

        private static string Styles()
        {
            string font = "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/>";

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:docDefaults><w:rPrDefault><w:rPr>" + font +
                "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault>" +
                "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:pPrDefault></w:docDefaults>" +

                "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
                "<w:name w:val=\"Normal\"/><w:qFormat/>" +
                "<w:pPr><w:spacing w:after=\"0\"/></w:pPr><w:rPr>" + font + "</w:rPr></w:style>" +

                "<w:style w:type=\"paragraph\" w:styleId=\"Title\">" +
                "<w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>" +
                "<w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"120\"/></w:pPr>" +
                "<w:rPr><w:b/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr></w:style>" +

                "<w:style w:type=\"paragraph\" w:styleId=\"Subtitle\">" +
                "<w:name w:val=\"Subtitle\"/><w:basedOn w:val=\"Normal\"/>" +
                "<w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"240\"/></w:pPr>" +
                "<w:rPr><w:color w:val=\"595959\"/><w:sz w:val=\"20\"/></w:rPr></w:style>" +

                "<w:style w:type=\"paragraph\" w:styleId=\"TableHeader\">" +
                "<w:name w:val=\"Table Header\"/><w:basedOn w:val=\"Normal\"/>" +
                "<w:rPr><w:b/></w:rPr></w:style>" +

                "<w:style w:type=\"paragraph\" w:styleId=\"GroupRow\">" +
                "<w:name w:val=\"Group Row\"/><w:basedOn w:val=\"Normal\"/>" +
                "<w:rPr><w:b/><w:caps/></w:rPr></w:style>" +

                "<w:style w:type=\"paragraph\" w:styleId=\"TotalRow\">" +
                "<w:name w:val=\"Total Row\"/><w:basedOn w:val=\"Normal\"/>" +
                "<w:rPr><w:b/><w:sz w:val=\"24\"/></w:rPr></w:style>" +

                "<w:style w:type=\"paragraph\" w:styleId=\"FootLine\">" +
                "<w:name w:val=\"Foot Line\"/><w:basedOn w:val=\"Normal\"/>" +
                "<w:pPr><w:jc w:val=\"right\"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>" +

                "</w:styles>";
        }

        // ------------------------------------------------------- разметка

        private static string BuildDocument(EstimateDocument source)
        {
            StringBuilder body = new StringBuilder();

            body.Append(Paragraph(source.Title, "Title", "center"));
            body.Append(Paragraph(source.Subtitle, "Subtitle", "center"));
            body.Append(BuildTable(source));

            // строки подытога, скидки и итога
            foreach (string line in source.FootLines)
                body.Append(Paragraph(line, "FootLine", "right"));

            body.Append(Paragraph("", "Normal", null));
            body.Append(BuildSignatures(source));

            string section =
                "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
                "<w:pgMar w:top=\"1134\" w:right=\"850\" w:bottom=\"1134\" w:left=\"850\" " +
                "w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body>" + body + section + "</w:body></w:document>";
        }

        private static string BuildTable(EstimateDocument source)
        {
            string borders =
                "<w:tblBorders>" +
                "<w:top w:val=\"single\" w:sz=\"4\" w:color=\"808080\"/>" +
                "<w:left w:val=\"none\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"808080\"/>" +
                "<w:right w:val=\"none\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"BFBFBF\"/>" +
                "<w:insideV w:val=\"none\"/></w:tblBorders>";

            StringBuilder grid = new StringBuilder("<w:tblGrid>");
            foreach (int width in EstimateDocument.Grid) grid.Append(Col(width));
            grid.Append("</w:tblGrid>");

            StringBuilder table = new StringBuilder();
            table.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>").Append(borders)
                 .Append("</w:tblPr>").Append(grid);

            foreach (DocRow row in source.Rows)
            {
                StringBuilder line = new StringBuilder("<w:tr>");

                for (int i = 0; i < row.Cells.Length; i++)
                {
                    bool numeric = i >= 4;
                    bool center = i <= 1 || i == 3;
                    line.Append(Cell(row.Cells[i], row.Style, numeric ? "right" : (center ? "center" : "left")));
                }

                line.Append("</w:tr>");
                table.Append(line);
            }

            table.Append("</w:tbl>");
            return table.ToString();
        }

        private static string Col(int width)
        {
            return "<w:gridCol w:w=\"" + width.ToString(CultureInfo.InvariantCulture) + "\"/>";
        }

        private static string Cell(string text, string style, string alignment)
        {
            return "<w:tc><w:tcPr><w:vAlign w:val=\"center\"/></w:tcPr>" +
                   Paragraph(text, style, alignment) + "</w:tc>";
        }

        private static string Paragraph(string text, string style, string alignment)
        {
            StringBuilder paragraph = new StringBuilder();
            paragraph.Append("<w:p><w:pPr>");

            if (!string.IsNullOrEmpty(style)) paragraph.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
            if (!string.IsNullOrEmpty(alignment)) paragraph.Append("<w:jc w:val=\"").Append(alignment).Append("\"/>");
            paragraph.Append("</w:pPr>");

            if (!string.IsNullOrEmpty(text))
            {
                paragraph.Append("<w:r><w:t xml:space=\"preserve\">")
                         .Append(Escape(text))
                         .Append("</w:t></w:r>");
            }

            paragraph.Append("</w:p>");
            return paragraph.ToString();
        }

        private static string BuildSignatures(EstimateDocument source)
        {
            StringBuilder table = new StringBuilder();
            table.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>");
            table.Append("<w:tblBorders><w:top w:val=\"none\"/><w:left w:val=\"none\"/>");
            table.Append("<w:bottom w:val=\"none\"/><w:right w:val=\"none\"/>");
            table.Append("<w:insideH w:val=\"none\"/><w:insideV w:val=\"none\"/></w:tblBorders></w:tblPr>");
            table.Append("<w:tblGrid>").Append(Col(4600)).Append(Col(4600)).Append("</w:tblGrid><w:tr>");
            table.Append(Cell(source.LeftSign, "Normal", "left"));
            table.Append(Cell(source.RightSign, "Normal", "left"));
            table.Append("</w:tr></w:tbl>");
            return table.ToString();
        }

        private static string Escape(string text)
        {
            StringBuilder result = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': result.Append("&amp;"); break;
                    case '<': result.Append("&lt;"); break;
                    case '>': result.Append("&gt;"); break;
                    case '"': result.Append("&quot;"); break;
                    case '\'': result.Append("&apos;"); break;
                    default:
                        if (c < 32 && c != '\t' && c != '\n' && c != '\r') result.Append(' ');
                        else result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }
    }
}