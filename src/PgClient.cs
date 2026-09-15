// ---------------------------------------------------------------------------
//  Клиент PostgreSQL, написанный на стандартной библиотеке .NET.
//
//  Зачем свой клиент: программа собирается одним файлом компилятором
//  .NET Framework и не тянет сторонние библиотеки (Npgsql и подобные).
//  Драйвер ODBC к PostgreSQL в Windows не входит, поэтому протокол
//  реализован напрямую — так программа остаётся самодостаточной.
//
//  Поддержано: версия протокола 3.0, авторизация trust, password,
//  md5 и scram-sha-256 (так настроен PostgreSQL по умолчанию),
//  простые запросы и параметризованные команды.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KotovCalc
{
    /// <summary>Параметры подключения к базе данных.</summary>
    internal sealed class PgConnectionInfo
    {
        public string Host = "localhost";
        public int Port = 5432;
        public string Database = "postgres";
        public string User = "postgres";
        public string Password = "";
        public int TimeoutSeconds = 15;

        public PgConnectionInfo() { }

        public PgConnectionInfo(string host, int port, string database, string user, string password)
        {
            Host = host;
            Port = port;
            Database = database;
            User = user;
            Password = password;
        }

        /// <summary>
        /// Разбор строки подключения. Понимает три вида записи:
        ///   host=127.0.0.1 port=5432 dbname=smeta user=smeta password=...
        ///   postgresql://smeta:smeta@127.0.0.1:5432/smeta
        ///   smeta:smeta@127.0.0.1:5432/smeta
        /// </summary>
        public static PgConnectionInfo Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Пустая строка подключения.");

            string value = text.Trim();

            if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                int at = value.IndexOf("://", StringComparison.Ordinal);
                return ParseUri(value.Substring(at + 3));
            }

            if (value.IndexOf('=') >= 0) return ParseKeyValues(value);

            return ParseUri(value);
        }

        private static PgConnectionInfo ParseUri(string body)
        {
            PgConnectionInfo info = new PgConnectionInfo();

            string credentials = "";
            int at = body.LastIndexOf('@');
            if (at >= 0)
            {
                credentials = body.Substring(0, at);
                body = body.Substring(at + 1);
            }

            if (credentials.Length > 0)
            {
                int colon = credentials.IndexOf(':');
                if (colon >= 0)
                {
                    info.User = Uri.UnescapeDataString(credentials.Substring(0, colon));
                    info.Password = Uri.UnescapeDataString(credentials.Substring(colon + 1));
                }
                else
                {
                    info.User = Uri.UnescapeDataString(credentials);
                }
            }

            string database = "";
            int slash = body.IndexOf('/');
            if (slash >= 0)
            {
                database = body.Substring(slash + 1);
                body = body.Substring(0, slash);
            }

            int question = database.IndexOf('?');
            if (question >= 0) database = database.Substring(0, question);

            int portColon = body.LastIndexOf(':');
            if (portColon >= 0)
            {
                int port;
                if (int.TryParse(body.Substring(portColon + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out port))
                    info.Port = port;

                body = body.Substring(0, portColon);
            }

            if (body.Length > 0) info.Host = body;
            if (database.Length > 0) info.Database = database;

            return info;
        }

        private static PgConnectionInfo ParseKeyValues(string text)
        {
            PgConnectionInfo info = new PgConnectionInfo();

            foreach (string part in text.Split(new char[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int at = part.IndexOf('=');
                if (at <= 0) continue;

                string key = part.Substring(0, at).Trim().ToLowerInvariant();
                string value = part.Substring(at + 1).Trim().Trim('\'', '"');

                switch (key)
                {
                    case "host":
                    case "server":
                    case "адрес": info.Host = value; break;
                    case "port":
                    case "порт":
                        int port;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                            info.Port = port;
                        break;
                    case "dbname":
                    case "database":
                    case "база": info.Database = value; break;
                    case "user":
                    case "username":
                    case "uid":
                    case "пользователь": info.User = value; break;
                    case "password":
                    case "pwd":
                    case "пароль": info.Password = value; break;
                    case "timeout":
                    case "connect_timeout":
                        int seconds;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) &&
                            seconds > 0)
                            info.TimeoutSeconds = seconds;
                        break;
                }
            }

            return info;
        }

        /// <summary>Строка подключения без пароля — её можно показывать и хранить.</summary>
        public string Describe()
        {
            return Host + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/" + Database +
                   " (пользователь " + User + ")";
        }
    }

    /// <summary>Строка результата: значения доступны по имени, порядок колонок сохранён.</summary>
    internal sealed class PgRow
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        public int Count { get { return _order.Count; } }

        /// <summary>Имена колонок в порядке, который прислал сервер.</summary>
        public IList<string> Columns { get { return _order; } }

        internal void Add(string name, object value)
        {
            if (!_values.ContainsKey(name)) _order.Add(name);
            _values[name] = value;
        }

        public object this[string name]
        {
            get
            {
                object value;
                return _values.TryGetValue(name, out value) ? value : null;
            }
        }

        public bool Has(string name) { return _values.ContainsKey(name); }

        /// <summary>Значение первой колонки.</summary>
        public object First()
        {
            return _order.Count == 0 ? null : _values[_order[0]];
        }

        public IEnumerable<KeyValuePair<string, object>> Pairs()
        {
            foreach (string name in _order) yield return new KeyValuePair<string, object>(name, _values[name]);
        }
    }
    /// <summary>Ошибка при работе с базой данных.</summary>
    internal sealed class PgException : Exception
    {
        public PgException(string message) : base(message) { }
    }

    /// <summary>Подключение к PostgreSQL и выполнение запросов.</summary>
    internal sealed class PgClient : IDisposable
    {
        private readonly PgConnectionInfo _info;
        private TcpClient _client;
        private NetworkStream _stream;
        private byte[] _buffer = new byte[0];      // ещё не обработанные байты ответа
        private bool _disposed;

        // состояние последнего запроса
        private string _lastError;
        private string _lastSqlState;
        private string _parameterStatus;

        public PgClient(PgConnectionInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
        }

        public PgConnectionInfo Info { get { return _info; } }

        /// <summary>Сообщение об ошибке последнего запроса.</summary>
        public string LastError { get { return _lastError; } }

        /// <summary>Код ошибки SQLSTATE последнего запроса.</summary>
        public string LastSqlState { get { return _lastSqlState; } }

        /// <summary>Параметр сервера, полученный при подключении (например, server_version).</summary>
        public string ParameterStatus { get { return _parameterStatus; } }

        // ------------------------------------------------------- подключение

        /// <summary>Подключение и авторизация.</summary>
        public void Connect()
        {
            _client = new TcpClient();
            _client.ReceiveTimeout = _info.TimeoutSeconds * 1000;
            _client.SendTimeout = _info.TimeoutSeconds * 1000;

            try
            {
                IAsyncResult pending = _client.BeginConnect(_info.Host, _info.Port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(_info.TimeoutSeconds * 1000))
                    throw new PgException("Не удалось подключиться к " + _info.Host + ":" + _info.Port +
                                          " за " + _info.TimeoutSeconds + " с. Проверьте адрес и доступность сервера.");

                _client.EndConnect(pending);
            }
            catch (PgException) { throw; }
            catch (Exception ex)
            {
                throw new PgException("Не удалось подключиться к " + _info.Host + ":" + _info.Port +
                                      ". " + ex.Message);
            }

            _stream = _client.GetStream();
            _buffer = new byte[0];

            SendStartup();
            Authenticate();
        }

        private void SendStartup()
        {
            List<byte> body = new List<byte>();
            AddInt32(body, 196608);                 // версия протокола 3.0
            AddCString(body, "user");
            AddCString(body, _info.User);
            AddCString(body, "database");
            AddCString(body, _info.Database);
            AddCString(body, "client_encoding");
            AddCString(body, "UTF8");
            AddCString(body, "application_name");
            AddCString(body, "raschet-smeta");
            body.Add(0);

            List<byte> message = new List<byte>();
            AddInt32(message, body.Count + 4);
            message.AddRange(body);

            Write(message);
            ReadStartupResponse();
        }

        /// <summary>Ответ на стартовое сообщение: запрос пароля или сразу готовность.</summary>
        private void ReadStartupResponse()
        {
            while (true)
            {
                int type;
                byte[] payload = ReadMessage(out type);

                if (type == 'R')
                {
                    int code = ReadInt32(payload, 0);
                    if (code == 0) continue;                       // ok
                    _authPayload = payload;
                    if (code == 3) _passwordRequest = 3;           // cleartext
                    else if (code == 5) _passwordRequest = 5;      // md5
                    else if (code == 10) _passwordRequest = 10;    // sasl
                    else throw new PgException("Сервер требует незнакомый способ авторизации: " + code);
                    return;
                }

                if (type == 'E') throw new PgException(Describe(payload));
                if (type == 'Z') return;                           // готов, пароль не нужен
                if (type == 'S') RememberParameter(payload);
            }
        }

        private byte[] _authPayload;        // тело сообщения с солью или списком механизмов
        private int _passwordRequest;

        private void Authenticate()
        {
            if (_passwordRequest == 0) return;

            if (_passwordRequest == 3)
            {
                List<byte> body = new List<byte>();
                AddCString(body, _info.Password);
                WriteMessage((byte)'p', body);
            }
            else if (_passwordRequest == 5)
            {
                // md5: "md5" + hex(md5(hex(md5(пароль + пользователь)) + соль))
                byte[] payload = _authPayload;
                string salt = Hex(payload, 4, 4);

                string inner = Hex(Md5(Encoding.UTF8.GetBytes(_info.Password + _info.User)));
                string hash = "md5" + Hex(Md5(Encoding.UTF8.GetBytes(inner + salt)));

                List<byte> body = new List<byte>();
                AddCString(body, hash);
                WriteMessage((byte)'p', body);
            }
            else if (_passwordRequest == 10)
            {
                ScramAuthenticate(_authPayload);
            }

            // ждём подтверждение
            while (true)
            {
                int type;
                byte[] payload = ReadMessage(out type);

                if (type == 'R')
                {
                    int code = ReadInt32(payload, 0);
                    if (code != 0) throw new PgException("Авторизация не завершена, код " + code);
                    continue;
                }

                if (type == 'E') throw new PgException(Describe(payload));
                if (type == 'S') { RememberParameter(payload); continue; }
                if (type == 'K') continue;
                if (type == 'Z') return;
            }
        }


        /// <summary>Авторизация SCRAM-SHA-256.</summary>
        private void ScramAuthenticate(byte[] saslPayload)
        {
            // сервер перечисляет поддерживаемые механизмы
            string mechanisms = Encoding.UTF8.GetString(saslPayload, 4, saslPayload.Length - 4);
            if (mechanisms.IndexOf("SCRAM-SHA-256", StringComparison.Ordinal) < 0)
                throw new PgException("Сервер не поддерживает SCRAM-SHA-256. Доступные способы: " + mechanisms);

            string nonce = Base64(RandomBytes(18));
            string clientFirstBare = "n=,r=" + nonce;
            string clientFirst = "n,," + clientFirstBare;

            List<byte> message = new List<byte>();
            AddCString(message, "SCRAM-SHA-256");
            AddInt32(message, Encoding.UTF8.GetByteCount(clientFirst));
            message.AddRange(Encoding.UTF8.GetBytes(clientFirst));
            WriteMessage((byte)'p', message);

            // server-first
            int type;
            byte[] payload = ReadMessage(out type);
            if (type == 'E') throw new PgException(Describe(payload));

            string serverFirst = Encoding.UTF8.GetString(payload, 4, payload.Length - 4);
            string serverNonce, salt, iterationsText;
            ParseServerFirst(serverFirst, out serverNonce, out salt, out iterationsText);

            if (!serverNonce.StartsWith(nonce, StringComparison.Ordinal))
                throw new PgException("Сервер прислал неверный одноразовый код — соединение небезопасно.");

            int iterations = int.Parse(iterationsText, CultureInfo.InvariantCulture);
            byte[] saltBytes = Convert.FromBase64String(salt);

            byte[] saltedPassword = Pbkdf2(_info.Password, saltBytes, iterations, 32);
            byte[] clientKey = Hmac(saltedPassword, Encoding.UTF8.GetBytes("Client Key"));
            byte[] storedKey = Sha256(clientKey);

            string clientFinalWithoutProof = "c=biws,r=" + serverNonce;
            string authMessage = clientFirstBare + "," + serverFirst + "," + clientFinalWithoutProof;

            byte[] clientSignature = Hmac(storedKey, Encoding.UTF8.GetBytes(authMessage));
            byte[] proof = Xor(clientKey, clientSignature);

            string clientFinal = clientFinalWithoutProof + ",p=" + Convert.ToBase64String(proof);

            List<byte> final = new List<byte>();
            final.AddRange(Encoding.UTF8.GetBytes(clientFinal));
            WriteMessage((byte)'p', final);

            // server-final: проверяем подпись сервера
            payload = ReadMessage(out type);
            if (type == 'E') throw new PgException(Describe(payload));

            string serverFinal = Encoding.UTF8.GetString(payload, 4, payload.Length - 4);
            if (serverFinal.StartsWith("e=", StringComparison.Ordinal))
                throw new PgException("Сервер отклонил авторизацию: " + serverFinal.Substring(2));

            int signatureAt = serverFinal.IndexOf("v=", StringComparison.Ordinal);
            if (signatureAt >= 0)
            {
                byte[] serverKey = Hmac(saltedPassword, Encoding.UTF8.GetBytes("Server Key"));
                byte[] expected = Hmac(serverKey, Encoding.UTF8.GetBytes(authMessage));
                string actual = serverFinal.Substring(signatureAt + 2);

                if (!string.Equals(Convert.ToBase64String(expected), actual, StringComparison.Ordinal))
                    throw new PgException("Подпись сервера не совпала — соединение прервано.");
            }
        }

        private static void ParseServerFirst(string text, out string nonce, out string salt, out string iterations)
        {
            nonce = null; salt = null; iterations = null;

            foreach (string part in text.Split(','))
            {
                if (part.StartsWith("r=", StringComparison.Ordinal)) nonce = part.Substring(2);
                else if (part.StartsWith("s=", StringComparison.Ordinal)) salt = part.Substring(2);
                else if (part.StartsWith("i=", StringComparison.Ordinal)) iterations = part.Substring(2);
            }

            if (nonce == null || salt == null || iterations == null)
                throw new PgException("Сервер прислал неполный ответ авторизации.");
        }

        // ---------------------------------------------------------- запросы

        /// <summary>Простой запрос. Возвращает null при ошибке — текст в LastError.</summary>
        public List<PgRow> Query(string sql, params object[] parameters)
        {
            string command = parameters == null || parameters.Length == 0 ? sql : Format(sql, parameters);
            return Execute(command);
        }

        /// <summary>Команда без результата (INSERT, UPDATE, DELETE, DDL).</summary>
        public bool Execute(string sql, params object[] parameters)
        {
            string command = parameters == null || parameters.Length == 0 ? sql : Format(sql, parameters);
            List<PgRow> result = Execute(command);
            return result != null;
        }

        /// <summary>Скалярный результат первой строки.</summary>
        public object Scalar(string sql, params object[] parameters)
        {
            List<PgRow> rows = Query(sql, parameters);
            if (rows == null || rows.Count == 0) return null;

            return rows[0].First();
        }

        private List<PgRow> Execute(string sql)
        {
            _lastError = null;
            _lastSqlState = null;

            try
            {
                List<byte> body = new List<byte>();
                AddCString(body, sql);
                WriteMessage((byte)'Q', body);

                List<PgRow> rows = new List<PgRow>();
                List<string> columns = new List<string>();
                bool failed = false;

                // Ответ читаем до сообщения ReadyForQuery: если его не дочитать,
                // следующий запрос получит чужой результат.
                while (true)
                {
                    int type;
                    byte[] payload = ReadMessage(out type);

                    if (type == 'T') columns = ReadRowDescription(payload);
                    else if (type == 'D') { if (!failed) rows.Add(ReadDataRow(payload, columns)); }
                    else if (type == 'C') continue;                       // тег команды
                    else if (type == 'N') continue;                       // замечание сервера
                    else if (type == 'S') { RememberParameter(payload); continue; }
                    else if (type == 'E')
                    {
                        if (_lastError == null)
                        {
                            _lastError = Describe(payload);
                            _lastSqlState = SqlState(payload);
                        }
                        failed = true;
                    }
                    else if (type == 'Z')
                    {
                        if (failed) return null;
                        return rows;
                    }
                }
            }
            catch (PgException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PgException("Ошибка при работе с базой данных: " + ex.Message);
            }
        }

        /// <summary>Подстановка значений вместо $1, $2, ...</summary>
        public static string Format(string sql, object[] parameters)
        {
            string result = sql;

            for (int i = parameters.Length; i >= 1; i--)
            {
                string marker = "$" + i.ToString(CultureInfo.InvariantCulture);
                result = result.Replace(marker, Literal(parameters[i - 1]));
            }

            return result;
        }

        /// <summary>Значение в виде литерала SQL.</summary>
        public static string Literal(object value)
        {
            if (value == null || value == DBNull.Value) return "NULL";

            if (value is bool) return ((bool)value) ? "true" : "false";
            if (value is int || value is long || value is short) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is decimal) return ((decimal)value).ToString("0.####", CultureInfo.InvariantCulture);
            if (value is double) return ((double)value).ToString("0.####", CultureInfo.InvariantCulture);
            if (value is float) return ((float)value).ToString("0.####", CultureInfo.InvariantCulture);
            if (value is DateTime)
                return "'" + ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "'";

            return Quote(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        /// <summary>Экранирование строки: удвоение кавычек и E'' для обратных слэшей.</summary>
        public static string Quote(string text)
        {
            string escaped = text.Replace("'", "''");
            if (escaped.IndexOf('\\') >= 0) return " E'" + escaped.Replace("\\", "\\\\") + "'";
            return " '" + escaped + "'";
        }

        // ------------------------------------------------- разбор сообщений

        private List<string> ReadRowDescription(byte[] payload)
        {
            List<string> columns = new List<string>();
            int count = ReadInt16(payload, 0);
            int at = 2;

            for (int i = 0; i < count; i++)
            {
                int end = at;
                while (end < payload.Length && payload[end] != 0) end++;
                columns.Add(Encoding.UTF8.GetString(payload, at, end - at));
                at = end + 1 + 18;                                  // пропускаем описание поля
            }

            return columns;
        }

        private PgRow ReadDataRow(byte[] payload, List<string> columns)
        {
            PgRow row = new PgRow();
            int count = ReadInt16(payload, 0);
            int at = 2;

            for (int i = 0; i < count; i++)
            {
                int length = ReadInt32(payload, at);
                at += 4;

                object value = null;
                if (length >= 0)
                {
                    value = Encoding.UTF8.GetString(payload, at, length);
                    at += length;
                }

                string name = i < columns.Count ? columns[i] : "column" + i;
                row.Add(name, value);
            }

            return row;
        }

        private static string Describe(byte[] payload)
        {
            string message = null;
            string detail = null;
            string hint = null;
            int at = 0;

            while (at < payload.Length && payload[at] != 0)
            {
                char field = (char)payload[at];
                int end = at + 1;
                while (end < payload.Length && payload[end] != 0) end++;
                string value = Encoding.UTF8.GetString(payload, at + 1, end - at - 1);

                if (field == 'M') message = value;
                else if (field == 'D') detail = value;
                else if (field == 'H') hint = value;

                at = end + 1;
            }

            string text = message == null ? "Неизвестная ошибка базы данных." : message;
            if (detail != null) text += " " + detail;
            if (hint != null) text += " Подсказка: " + hint;
            return text;
        }

        private static string SqlState(byte[] payload)
        {
            int at = 0;
            while (at < payload.Length && payload[at] != 0)
            {
                char field = (char)payload[at];
                int end = at + 1;
                while (end < payload.Length && payload[end] != 0) end++;

                if (field == 'C') return Encoding.UTF8.GetString(payload, at + 1, end - at - 1);
                at = end + 1;
            }

            return null;
        }

        private void RememberParameter(byte[] payload)
        {
            int at = 0;
            int end = at;
            while (end < payload.Length && payload[end] != 0) end++;
            string name = Encoding.UTF8.GetString(payload, at, end - at);

            int valueStart = end + 1;
            int valueEnd = valueStart;
            while (valueEnd < payload.Length && payload[valueEnd] != 0) valueEnd++;
            string value = Encoding.UTF8.GetString(payload, valueStart, valueEnd - valueStart);

            if (name == "server_version") _parameterStatus = value;
        }

        // ----------------------------------------------------- чтение потока

        private byte[] ReadMessage(out int type)
        {
            byte[] header = ReadBytes(5);
            type = header[0];
            int length = (header[1] << 24) | (header[2] << 16) | (header[3] << 8) | header[4];
            if (length < 4) return new byte[0];
            return ReadBytes(length - 4);
        }

        private byte[] ReadBytes(int count)
        {
            byte[] result = new byte[count];
            int filled = 0;

            while (filled < count)
            {
                if (_buffer.Length == 0)
                {
                    byte[] chunk = new byte[16384];
                    int read = _stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0) throw new PgException("Соединение с базой данных закрыто сервером.");

                    _buffer = new byte[read];
                    Array.Copy(chunk, 0, _buffer, 0, read);
                }

                int take = Math.Min(count - filled, _buffer.Length);
                Array.Copy(_buffer, 0, result, filled, take);
                filled += take;

                // прочитанные байты убираем из буфера, чтобы он не рос
                if (take == _buffer.Length)
                {
                    _buffer = new byte[0];
                }
                else
                {
                    byte[] rest = new byte[_buffer.Length - take];
                    Array.Copy(_buffer, take, rest, 0, rest.Length);
                    _buffer = rest;
                }
            }

            return result;
        }

        private void Write(List<byte> bytes)
        {
            _stream.Write(bytes.ToArray(), 0, bytes.Count);
            _stream.Flush();
        }

        private void WriteMessage(byte type, List<byte> body)
        {
            List<byte> message = new List<byte>();
            message.Add(type);
            AddInt32(message, body.Count + 4);
            message.AddRange(body);
            Write(message);
        }

        // ------------------------------------------------- помощники записи

        private static void AddInt32(List<byte> list, int value)
        {
            list.Add((byte)(value >> 24));
            list.Add((byte)(value >> 16));
            list.Add((byte)(value >> 8));
            list.Add((byte)value);
        }

        private static void AddCString(List<byte> list, string text)
        {
            list.AddRange(Encoding.UTF8.GetBytes(text));
            list.Add(0);
        }

        private static int ReadInt32(byte[] data, int at)
        {
            return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
        }

        private static int ReadInt16(byte[] data, int at)
        {
            return (data[at] << 8) | data[at + 1];
        }

        // ------------------------------------------------------ криптография

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return bytes;
        }

        private static byte[] Md5(byte[] data)
        {
            using (MD5 md5 = MD5.Create()) return md5.ComputeHash(data);
        }

        private static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        private static byte[] Hmac(byte[] key, byte[] data)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key)) return hmac.ComputeHash(data);
        }

        /// <summary>
        /// PBKDF2 с HMAC-SHA256: встроенный Rfc2898DeriveBytes в .NET Framework
        /// считает по SHA-1, а SCRAM-SHA-256 требует именно SHA-256.
        /// </summary>
        private static byte[] Pbkdf2(string password, byte[] salt, int iterations, int length)
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

        private static byte[] Xor(byte[] left, byte[] right)
        {
            byte[] result = new byte[left.Length];
            for (int i = 0; i < left.Length; i++) result[i] = (byte)(left[i] ^ right[i]);
            return result;
        }

        private static string Hex(byte[] data)
        {
            StringBuilder text = new StringBuilder(data.Length * 2);
            foreach (byte value in data) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static string Hex(byte[] data, int offset, int count)
        {
            StringBuilder text = new StringBuilder(count * 2);
            for (int i = 0; i < count; i++)
                text.Append(data[offset + i].ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static string Base64(byte[] data)
        {
            return Convert.ToBase64String(data);
        }

        // --------------------------------------------------------- закрытие

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_stream != null)
                {
                    List<byte> body = new List<byte>();
                    WriteMessage((byte)'X', body);
                }
            }
            catch { }

            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
        }
    }
}
