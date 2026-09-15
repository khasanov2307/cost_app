// ---------------------------------------------------------------------------
//  Текущее хранилище программы и вход пользователя.
//
//  Программа может работать с файлами на этом компьютере или с базой
//  PostgreSQL. Режим хранится в настройках, пароль базы каждый раз
//  запрашивается при запуске и на диск не попадает.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Параметры подключения и выбранный режим работы.</summary>
    internal static class ConnectionSettings
    {
        private static IDataStore _store = new FileDataStore();
        private static IEstimateArchive _archive = new FileEstimateArchive();
        private static PgConnectionInfo _database;

        /// <summary>Режим работы базы: пустая строка — файлы.</summary>
        public static string DatabaseMode = "";

        /// <summary>Адрес подключения без пароля — хранится в настройках.</summary>
        public static string DatabaseHost = "127.0.0.1";
        public static int DatabasePort = 5432;
        public static string DatabaseName = "smeta";
        public static string DatabaseUser = "smeta";

        /// <summary>Пароль базы: только в памяти, на диск не пишется.</summary>
        public static string DatabasePassword = "";

        public static bool WebEnabled;
        public static int WebPort = 8791;

        /// <summary>Режим работы: файлы или база.</summary>
        public static bool UseDatabase
        {
            get { return !string.IsNullOrEmpty(DatabaseMode) && _database != null; }
        }

        /// <summary>Действующее хранилище.</summary>
        public static IDataStore Store
        {
            get { return _store; }
        }

        /// <summary>Действующий архив заявок: файлы или база данных.</summary>
        public static IEstimateArchive Archive
        {
            get { return _archive; }
        }

        /// <summary>Параметры подключения к базе или null в файловом режиме.</summary>
        public static PgConnectionInfo Database
        {
            get { return _database; }
        }

        /// <summary>Переключение на файлы.</summary>
        public static void UseFiles()
        {
            _store = new FileDataStore();
            _database = null;
            DatabaseMode = "";
        }

        /// <summary>Переключение на базу данных.</summary>
        public static void UseSql(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");

            SqlDataStore sql = new SqlDataStore(info);
            string error = sql.Test();
            if (error != null) throw new PgException(error);

            sql.Prepare();

            _database = info;
            _store = sql;
            _archive = new SqlEstimateArchive(info);
            DatabaseMode = "sql";

            DatabaseHost = info.Host;
            DatabasePort = info.Port;
            DatabaseName = info.Database;
            DatabaseUser = info.User;
            DatabasePassword = info.Password;
        }

        /// <summary>Параметры подключения из сохранённых настроек.</summary>
        public static PgConnectionInfo Build()
        {
            return new PgConnectionInfo(DatabaseHost, DatabasePort, DatabaseName, DatabaseUser, DatabasePassword);
        }

        // ------------------------------------------------------- настройки

        private const string Section = "соединение";

        public static void Save()
        {
            try
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
                values[Section + ".режим"] = DatabaseMode;
                values[Section + ".адрес"] = DatabaseHost;
                values[Section + ".порт"] = DatabasePort.ToString(CultureInfo.InvariantCulture);
                values[Section + ".база"] = DatabaseName;
                values[Section + ".пользователь"] = DatabaseUser;
                values[Section + ".веб"] = WebEnabled ? "да" : "нет";
                values[Section + ".веб.порт"] = WebPort.ToString(CultureInfo.InvariantCulture);

                SettingsFile.SaveExtra(values);
            }
            catch { /* настройки не критичны */ }
        }

        /// <summary>Чтение сохранённых параметров подключения.</summary>
        public static void Load()
        {
            try
            {
                Dictionary<string, string> extra = SettingsFile.LoadExtra();

                string mode;
                extra.TryGetValue(Section + ".режим", out mode);
                DatabaseMode = mode == "sql" ? "sql" : "";

                string host;
                if (extra.TryGetValue(Section + ".адрес", out host) && host.Length > 0) DatabaseHost = host;

                string port;
                int parsedPort;
                if (extra.TryGetValue(Section + ".порт", out port) &&
                    int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedPort))
                    DatabasePort = parsedPort;

                string database;
                if (extra.TryGetValue(Section + ".база", out database) && database.Length > 0) DatabaseName = database;

                string user;
                if (extra.TryGetValue(Section + ".пользователь", out user) && user.Length > 0) DatabaseUser = user;

                string web;
                extra.TryGetValue(Section + ".веб", out web);
                WebEnabled = web == "да";

                string webPort;
                int parsedWebPort;
                if (extra.TryGetValue(Section + ".веб.порт", out webPort) &&
                    int.TryParse(webPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedWebPort))
                    WebPort = parsedWebPort;
            }
            catch { }
        }
    }

    /// <summary>Дополнительные настройки в отдельном файле (без паролей).</summary>
    internal static class SettingsFile
    {
        private const string FileName = "connection.ini";

        public static string Path
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, FileName); }
        }

        public static Dictionary<string, string> LoadExtra()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(Path)) return values;

            try
            {
                foreach (string line in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;

                    int at = line.IndexOf('=');
                    if (at <= 0) continue;

                    values[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                }
            }
            catch { }

            return values;
        }

        public static void SaveExtra(Dictionary<string, string> values)
        {
            try
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("# Параметры подключения программы «Расчет заявки».");
                text.AppendLine("# Пароль базы данных здесь не хранится — он запрашивается при запуске.");

                foreach (KeyValuePair<string, string> pair in values)
                    text.AppendLine(pair.Key + "=" + (pair.Value ?? "").Replace("\r", " ").Replace("\n", " "));

                File.WriteAllText(Path, text.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
