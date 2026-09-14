// ---------------------------------------------------------------------------
//  Точка входа приложения «Расчет сметы».
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace KotovCalc
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Десятичный разделитель — запятая (как принято при работе с рублями),
            // независимо от региональных настроек конкретного компьютера.
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            }
            catch { /* если культура недоступна, остаются системные настройки */ }

            // Ключ --estimate <файл>: сформировать смету (Word) по всему прайс-листу без окна.
            // Удобно для автоматизации и проверок.
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--estimate", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(MakeEstimate(args[1]));
                return;
            }

            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(
                    "Произошла непредвиденная ошибка:\n\n" + e.Exception.Message +
                    "\n\nПрограмма продолжит работу.",
                    "Расчет сметы", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }

        /// <summary>Формирование сметы по всему прайс-листу в PDF. Возвращает код выхода.</summary>
        private static int MakeEstimate(string path)
        {
            try
            {
                string error;
                List<ServiceItem> items = PriceBook.Load(out error);

                // все позиции прайса — как отмеченные, количество 1
                List<EstimateRow> rows = new List<EstimateRow>();
                foreach (ServiceItem item in items)
                {
                    EstimateRow row = new EstimateRow(item);
                    row.Selected = true;
                    rows.Add(row);
                }

                AppSettings settings = AppSettings.Load();
                DocumentFields fields = new DocumentFields();
                fields.Number = settings.Number;
                fields.Customer = settings.Customer;
                fields.Discount = settings.Discount;

                EstimateDocument document = DocumentBuilder.Build(rows, fields, "", DateTime.Now);
                EstimateDocx.Save(path, document, settings.Logo);

                Console.WriteLine("Смета сохранена: " + Path.GetFullPath(path) +
                                  "  (" + document.PositionCount + " позиций, " +
                                  Fmt.Money(document.Total) + ")");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Не удалось сформировать смету: " + ex.Message);
                return 1;
            }
        }
    }
}
