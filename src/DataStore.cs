// ---------------------------------------------------------------------------
//  Слой хранения данных: одинаковый набор операций для файлов и для базы.
//
//  Программа работает в двух режимах:
//    * FileDataStore  — данные лежат в личной папке программы (как раньше);
//    * SqlDataStore   — данные лежат в PostgreSQL по указанному адресу.
//
//  Режим переключается в настройках, остальной код о нём не знает.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KotovCalc
{
    /// <summary>Что удалось прочитать из хранилища.</summary>
    internal sealed class StoreSnapshot
    {
        public List<ServiceItem> Prices = new List<ServiceItem>();
        public List<ServiceTemplate> Templates = new List<ServiceTemplate>();
        public DocumentFields Document = new DocumentFields();
        public string Theme = "light";
        public string Logo = "";
        public string LastTemplate = "";
        public bool HasPrices;
        public bool HasTemplates;
        public bool HasSettings;

        public string Summary()
        {
            return "позиций прайса: " + Prices.Count + ", наборов: " + Templates.Count;
        }
    }

    /// <summary>Общий набор операций над хранилищем.</summary>
    internal interface IDataStore
    {
        /// <summary>Название режима для интерфейса.</summary>
        string Title { get; }

        /// <summary>Проверка доступности хранилища. Возвращает null, если всё хорошо.</summary>
        string Test();

        /// <summary>Подготовка хранилища (создание таблиц и файлов).</summary>
        void Prepare();

        List<ServiceItem> LoadPrices();
        void SavePrices(IList<ServiceItem> prices);

        List<ServiceTemplate> LoadTemplates();
        void SaveTemplates(IList<ServiceTemplate> templates);

        StoreSnapshot LoadAll();
        void SaveSettings(StoreSnapshot snapshot);

        /// <summary>
        /// Отпечаток данных: если он изменился, значит прайс или наборы правил
        /// другой пользователь — тогда программу нужно перечитать.
        /// </summary>
        string Stamp();
    }

    /// <summary>Хранилище в личной папке программы.</summary>
    internal sealed class FileDataStore : IDataStore
    {
        public string Title { get { return "Файлы на этом компьютере"; } }

        public string Test()
        {
            try
            {
                string folder = PriceBook.StoreFolder;
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

                string probe = System.IO.Path.Combine(folder, "проверка.tmp");
                System.IO.File.WriteAllText(probe, "ok", new UTF8Encoding(false));
                System.IO.File.Delete(probe);
                return null;
            }
            catch (Exception ex)
            {
                return "Папка данных недоступна: " + ex.Message;
            }
        }

        public void Prepare()
        {
            string error;
            PriceBook.Load(out error);          // создаёт хранилище при первом запуске
        }

        public List<ServiceItem> LoadPrices()
        {
            string error;
            return PriceBook.Load(out error);
        }

        public void SavePrices(IList<ServiceItem> prices)
        {
            PriceBook.Save(prices);
        }

        public List<ServiceTemplate> LoadTemplates()
        {
            return TemplateStore.Load();
        }

        public void SaveTemplates(IList<ServiceTemplate> templates)
        {
            TemplateStore.Save(templates);
        }

        public StoreSnapshot LoadAll()
        {
            StoreSnapshot snapshot = new StoreSnapshot();
            snapshot.Prices = LoadPrices();
            snapshot.Templates = LoadTemplates();
            snapshot.HasPrices = true;

            AppSettings settings = AppSettings.Load();
            snapshot.Document.Number = settings.Number;
            snapshot.Document.Customer = settings.Customer;
            snapshot.Document.Discount = settings.Discount;
            snapshot.Document.Normalize();
            snapshot.Theme = settings.Theme;
            snapshot.Logo = settings.Logo;
            snapshot.LastTemplate = settings.LastTemplate;
            snapshot.HasSettings = true;

            return snapshot;
        }

        public void SaveSettings(StoreSnapshot snapshot)
        {
            AppSettings settings = AppSettings.Load();
            settings.Number = snapshot.Document.Number;
            settings.Customer = snapshot.Document.Customer;
            settings.Discount = snapshot.Document.Discount;
            settings.Theme = snapshot.Theme;
            settings.Logo = snapshot.Logo;
            settings.LastTemplate = snapshot.LastTemplate;
            settings.Save();
        }

        /// <summary>Отпечаток данных: время правки файлов хранилища.</summary>
        public string Stamp()
        {
            StringBuilder text = new StringBuilder();

            string[] files = new string[]
            {
                PriceBook.StorePath,
                System.IO.Path.Combine(PriceBook.StoreFolder, "templates.json")
            };

            foreach (string file in files)
            {
                try
                {
                    text.Append(System.IO.File.Exists(file)
                        ? System.IO.File.GetLastWriteTimeUtc(file).Ticks.ToString(CultureInfo.InvariantCulture)
                        : "нет");
                }
                catch { text.Append("?"); }
                text.Append('|');
            }

            return text.ToString();
        }

    }

    /// <summary>Пароль пользователя программы.</summary>
    internal static class UserPassword
    {
        private const int Iterations = 10000;

        /// <summary>Запись пароля: pbkdf2-sha256$итерации$соль$хеш (всё в base64).</summary>
        public static string Create(string password)
        {
            byte[] salt = new byte[16];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(salt);

            byte[] hash = Pbkdf2(password, salt, Iterations, 32);
            return "pbkdf2-sha256$" + Iterations.ToString(CultureInfo.InvariantCulture) + "$" +
                   Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>Проверка пароля по сохранённой записи.</summary>
        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrEmpty(stored)) return false;

            string[] parts = stored.Split('$');
            if (parts.Length != 4) return false;
            if (parts[0] != "pbkdf2-sha256") return false;

            int iterations;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out iterations))
                return false;

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch { return false; }

            byte[] actual = Pbkdf2(password, salt, iterations, expected.Length);
            if (actual.Length != expected.Length) return false;

            // сравниваем все байты, чтобы время проверки не зависело от совпадения
            int difference = 0;
            for (int i = 0; i < actual.Length; i++) difference |= actual[i] ^ expected[i];
            return difference == 0;
        }

        /// <summary>
        /// PBKDF2 с HMAC-SHA256: встроенный Rfc2898DeriveBytes в .NET Framework
        /// считает по SHA-1, поэтому считаем сами.
        /// </summary>
        public static byte[] Pbkdf2(string password, byte[] salt, int iterations, int length)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] result = new byte[length];
            int blocks = (length + 31) / 32;

            for (int block = 1; block <= blocks; block++)
            {
                byte[] first = new byte[salt.Length + 4];
                Array.Copy(salt, 0, first, 0, salt.Length);
                first[salt.Length] = (byte)(block >> 24);
                first[salt.Length + 1] = (byte)(block >> 16);
                first[salt.Length + 2] = (byte)(block >> 8);
                first[salt.Length + 3] = (byte)block;

                byte[] current;
                using (HMACSHA256 hmac = new HMACSHA256(passwordBytes)) current = hmac.ComputeHash(first);
                byte[] accumulated = (byte[])current.Clone();

                for (int i = 1; i < iterations; i++)
                {
                    using (HMACSHA256 hmac = new HMACSHA256(passwordBytes)) current = hmac.ComputeHash(current);
                    for (int k = 0; k < accumulated.Length; k++) accumulated[k] ^= current[k];
                }

                int offset = (block - 1) * 32;
                int count = Math.Min(32, length - offset);
                Array.Copy(accumulated, 0, result, offset, count);
            }

            return result;
        }
    }
}
