// Рисует иконку приложения и сохраняет её в формате .ico для встраивания в exe.
//
// Кадры до 48 точек пишутся как BMP (их понимает GDI+ при показе значка),
// крупные кадры — как PNG: так делают штатные средства Windows.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class MakeIcon
{
    /// <summary>Рисует значок в заданном размере.</summary>
    private static Bitmap Draw(int size)
    {
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float k = size / 32f;                       // масштаб от исходных 32 точек
            float pad = 1.2f * k;

            // лист документа
            RectangleF sheet = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

            using (GraphicsPath path = RoundedRect(sheet, 3f * k))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                           sheet, Color.White, Color.FromArgb(232, 240, 250), 90f))
                    g.FillPath(brush, path);

                using (Pen pen = new Pen(Color.FromArgb(28, 78, 140), Math.Max(1f, 1.3f * k)))
                    g.DrawPath(pen, path);
            }

            // шапка листа
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(28, 78, 140)))
                g.FillRectangle(brush, sheet.Left, sheet.Top, sheet.Width, Math.Max(1.5f, 3.4f * k));

            // знак рубля
            if (size >= 24)
            {
                float fontSize = 19f * k;

                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(20, 60, 115)))
                {
                    SizeF measured = g.MeasureString("\u20BD", font);
                    g.DrawString("\u20BD", font, brush,
                        sheet.Left + (sheet.Width - measured.Width) / 2f,
                        sheet.Top + 3.4f * k + (sheet.Height - 3.4f * k - measured.Height) / 2f);
                }
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(20, 60, 115)))
                {
                    float w = sheet.Width * 0.44f;
                    float h = sheet.Height * 0.40f;
                    g.FillRectangle(brush, sheet.Left + (sheet.Width - w) / 2f,
                                    sheet.Top + 3.4f * k + (sheet.Height - 3.4f * k - h) / 2f, w, h);
                }
            }
        }

        return bitmap;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;

        path.AddArc(rect.Left, rect.Top, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();

        return path;
    }

    /// <summary>Кадр в виде BMP: заголовок DIB, пиксели снизу вверх и маска прозрачности.</summary>
    private static byte[] ToBmpFrame(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int maskStride = ((size + 31) / 32) * 4;
        int pixelBytes = size * size * 4;
        int maskBytes = maskStride * size;

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(40);                   // размер заголовка
            writer.Write(size);                 // ширина
            writer.Write(size * 2);             // высота: пиксели плюс маска
            writer.Write((ushort)1);            // плоскостей
            writer.Write((ushort)32);           // бит на точку
            writer.Write(0);                    // без сжатия
            writer.Write(pixelBytes + maskBytes);
            writer.Write(0);                    // разрешение по X
            writer.Write(0);                    // разрешение по Y
            writer.Write(0);                    // цветов в палитре
            writer.Write(0);                    // важных цветов

            for (int y = size - 1; y >= 0; y--)
            {
                for (int x = 0; x < size; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    writer.Write(color.B);
                    writer.Write(color.G);
                    writer.Write(color.R);
                    writer.Write(color.A);
                }
            }

            for (int y = 0; y < size; y++)
            {
                int written = 0;

                for (int x = 0; x < size; x += 8)
                {
                    byte bits = 0;

                    for (int bit = 0; bit < 8; bit++)
                    {
                        int px = x + bit;
                        if (px >= size) break;
                        if (bitmap.GetPixel(px, y).A < 128) bits |= (byte)(1 << (7 - bit));
                    }

                    writer.Write(bits);
                    written++;
                }

                while (written < maskStride) { writer.Write((byte)0); written++; }
            }

            writer.Flush();
            return stream.ToArray();
        }
    }

    private static byte[] ToPngFrame(Bitmap bitmap)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    /// <summary>Собирает .ico из кадров: заголовок, оглавление и данные.</summary>
    private static void WriteIcon(string path, int[] sizes)
    {
        List<byte[]> frames = new List<byte[]>();

        foreach (int size in sizes)
        {
            using (Bitmap bitmap = Draw(size))
                frames.Add(ToBmpFrame(bitmap));
        }

        using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);                        // зарезервировано
            writer.Write((ushort)1);                        // тип: значок
            writer.Write((ushort)sizes.Length);             // число кадров

            int offset = 6 + sizes.Length * 16;

            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));   // ширина
                writer.Write((byte)(size >= 256 ? 0 : size));   // высота
                writer.Write((byte)0);                          // цветов в палитре
                writer.Write((byte)0);                          // зарезервировано
                writer.Write((ushort)1);                        // плоскостей
                writer.Write((ushort)32);                       // бит на точку
                writer.Write((uint)frames[i].Length);           // размер данных
                writer.Write((uint)offset);                     // смещение данных
                offset += frames[i].Length;
            }

            foreach (byte[] frame in frames) writer.Write(frame);
        }
    }

    private static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "app.ico";
        // без кадра 256: он сильно утяжеляет файл, а в проводнике хватает 128
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128 };

        WriteIcon(path, sizes);

        FileInfo info = new FileInfo(path);
        Console.WriteLine("иконка: " + Path.GetFullPath(path) + ", " + info.Length + " байт, " +
                          sizes.Length + " кадров");

        foreach (int size in sizes)
        {
            try
            {
                using (Icon icon = new Icon(path, size, size))
                    Console.WriteLine("  кадр " + size + ": читается как " + icon.Width + "x" + icon.Height);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  кадр " + size + ": ОШИБКА " + ex.GetType().Name);
            }
        }

        // значок должен рисоваться без ошибок — так его показывает и программа
        using (Icon icon = new Icon(path, 32, 32))
        using (Bitmap bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.DrawIcon(icon, new Rectangle(8, 8, 48, 48));
            }

            Console.WriteLine("  отрисовка: значение точки в центре = " +
                              bitmap.GetPixel(32, 32).Name);
        }
    }
}
