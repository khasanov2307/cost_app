// ---------------------------------------------------------------------------
//  Точка входа приложения «Расчет заявки».
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

            // Ключ --estimate <файл>: сформировать заявку (Word) по всему прайс-листу без окна.
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
                    "Расчет заявки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // Режим работы: файлы на этом компьютере или база данных.
            // Если выбранная база недоступна, предлагаем перейти на файлы.
            ConnectionSettings.Load();
            PrepareStore();

            Application.Run(new MainForm());
        }

        /// <summary>
        /// Подготовка хранилища при запуске: запрос пароля базы, вход в программу,
        /// откат на файлы при недоступной базе.
        /// </summary>
        private static void PrepareStore()
        {
            if (ConnectionSettings.DatabaseMode != "sql")
            {
                ConnectionSettings.UseFiles();
                return;
            }

            // пароль базы на диске не хранится — спрашиваем при запуске
            if (string.IsNullOrEmpty(ConnectionSettings.DatabasePassword))
            {
                using (PasswordForm dialog = new PasswordForm(ConnectionSettings.DatabaseHost + ":" +
                                                              ConnectionSettings.DatabasePort + " / " +
                                                              ConnectionSettings.DatabaseName))
                {
                    if (dialog.ShowDialog() != DialogResult.OK || dialog.Password.Length == 0)
                    {
                        FallBackToFiles("Пароль базы данных не введён.");
                        return;
                    }

                    ConnectionSettings.DatabasePassword = dialog.Password;
                }
            }

            PgConnectionInfo info = ConnectionSettings.Build();

            try
            {
                ConnectionSettings.UseSql(info);
            }
            catch (Exception ex)
            {
                FallBackToFiles("Не удалось подключиться к базе данных:\n" + ex.Message);
                return;
            }

            SqlDataStore sql = ConnectionSettings.Store as SqlDataStore;
            if (sql == null) return;

            try
            {
                bool first = !sql.HasUsers();

                while (true)
                {
                    using (LoginForm login = new LoginForm(sql, first))
                    {
                        if (login.ShowDialog() == DialogResult.OK) return;
                    }

                    // от входа можно отказаться и вернуться к файлам
                    DialogResult answer = MessageBox.Show(
                        "Вход не выполнен. Перейти к работе с файлами на этом компьютере?",
                        "Вход в программу", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (answer == DialogResult.Yes)
                    {
                        FallBackToFiles(null);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                FallBackToFiles("Ошибка при входе в программу:\n" + ex.Message);
            }
        }

        /// <summary>Переход на файловое хранилище с сообщением пользователю.</summary>
        private static void FallBackToFiles(string reason)
        {
            ConnectionSettings.UseFiles();
            ConnectionSettings.DatabaseMode = "";
            ConnectionSettings.Save();

            if (string.IsNullOrEmpty(reason)) return;

            MessageBox.Show(
                reason + "\n\nПрограмма продолжит работу с файлами на этом компьютере. " +
                "Изменить режим можно в меню «Данные…» → «Подключение к базе данных…».",
                "Подключение", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Формирование заявки по всему прайс-листу в PDF. Возвращает код выхода.</summary>
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

                Console.WriteLine("Заявка сохранена: " + Path.GetFullPath(path) +
                                  "  (" + document.PositionCount + " позиций, " +
                                  Fmt.Money(document.Total) + ")");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Не удалось сформировать заявку: " + ex.Message);
                return 1;
            }
        }
    }
}
