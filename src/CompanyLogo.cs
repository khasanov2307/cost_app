// ---------------------------------------------------------------------------
//  Логотип компании для печатной формы сметы.
//
//  Файл читается при формировании документа, размеры берутся из самого
//  изображения (PNG, JPEG или GIF), затем картинка кладётся в архив Word
//  отдельным файлом и подключается связью.
// ---------------------------------------------------------------------------

using System;
using System.Drawing;
using System.IO;

namespace KotovCalc
{
    /// <summary>Готовый к вставке логотип.</summary>
    internal sealed class Logo
    {
        public byte[] Bytes = new byte[0];
        public string Extension = "png";
        public string ContentType = "image/png";
        public int Width = 180;
        public int Height = 60;

        private const int MaxWidth = 180;
        private const int MaxHeight = 64;

        /// <summary>Имя файла внутри архива документа.</summary>
        public string FileName { get { return "logo." + Extension; } }

        /// <summary>Чтение файла логотипа. При ошибке возвращает null.</summary>
        public static Logo Read(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                Logo logo = new Logo();
                logo.Bytes = File.ReadAllBytes(path);
                if (logo.Bytes.Length == 0) return null;

                string extension = Path.GetExtension(path) == null
                    ? ""
                    : Path.GetExtension(path).ToLowerInvariant();

                switch (extension)
                {
                    case ".png": logo.Extension = "png"; logo.ContentType = "image/png"; break;
                    case ".jpg":
                    case ".jpeg": logo.Extension = "jpeg"; logo.ContentType = "image/jpeg"; break;
                    case ".gif": logo.Extension = "gif"; logo.ContentType = "image/gif"; break;
                    default:
                        string guess = GuessExtension(logo.Bytes);
                        if (guess == null) return null;

                        logo.Extension = guess;
                        logo.ContentType = guess == "jpeg" ? "image/jpeg" : "image/" + guess;
                        break;
                }

                Size size = Measure(logo.Bytes);
                if (size.Width > 0 && size.Height > 0)
                {
                    int width = size.Width;
                    int height = size.Height;

                    if (width > MaxWidth)
                    {
                        height = (int)Math.Round(height * (double)MaxWidth / width);
                        width = MaxWidth;
                    }
                    if (height > MaxHeight)
                    {
                        width = (int)Math.Round(width * (double)MaxHeight / height);
                        height = MaxHeight;
                    }

                    logo.Width = Math.Max(1, width);
                    logo.Height = Math.Max(1, height);
                }

                return logo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Определение формата по содержимому файла.</summary>
        private static string GuessExtension(byte[] bytes)
        {
            if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50) return "png";
            if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "jpeg";
            if (bytes.Length > 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "gif";
            return null;
        }

        /// <summary>Размеры изображения. Для неизвестных форматов — нули.</summary>
        private static Size Measure(byte[] bytes)
        {
            // PNG: размеры лежат в блоке IHDR
            if (bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
            {
                int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                if (w > 0 && h > 0) return new Size(w, h);
            }

            // JPEG: ищем маркер начала кадра
            if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                int at = 2;
                while (at + 9 < bytes.Length)
                {
                    if (bytes[at] != 0xFF) { at++; continue; }

                    int marker = bytes[at + 1];
                    int size = (bytes[at + 2] << 8) | bytes[at + 3];

                    bool frame = marker >= 0xC0 && marker <= 0xCF &&
                                 marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

                    if (frame)
                    {
                        int h = (bytes[at + 5] << 8) | bytes[at + 6];
                        int w = (bytes[at + 7] << 8) | bytes[at + 8];
                        if (w > 0 && h > 0) return new Size(w, h);
                    }

                    at += 2 + size;
                }
            }

            // GIF: размеры идут сразу после заголовка
            if (bytes.Length > 10 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                int w = bytes[6] | (bytes[7] << 8);
                int h = bytes[8] | (bytes[9] << 8);
                if (w > 0 && h > 0) return new Size(w, h);
            }

            return new Size(0, 0);
        }
    }
}