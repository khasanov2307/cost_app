// ---------------------------------------------------------------------------
//  Окно «Подключение к базе данных» и вход в программу.
//
//  Здесь выбирается режим работы: файлы на этом компьютере или база
//  PostgreSQL по указанному адресу. Пароль базы запрашивается при запуске
//  и на диск не записывается. Вход в программу проверяет пользователя
//  в таблице smeta_users самой базы.
// ---------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Окно настроек подключения.</summary>
    internal sealed class ConnectionForm : Form
    {
        private RadioButton _filesMode;
        private RadioButton _sqlMode;

        private TextBox _host;
        private TextBox _port;
        private TextBox _database;
        private TextBox _user;
        private TextBox _password;

        private CheckBox _webEnabled;
        private TextBox _webPort;

        private Label _status;
        private Button _ok;

        private readonly Font _bold;

        public ConnectionForm()
        {
            _bold = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            Build();
            LoadCurrent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _bold != null) _bold.Dispose();
            base.Dispose(disposing);
        }

        private void Build()
        {
            Text = "Подключение — Расчет сметы";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 460);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            Label header = new Label();
            header.Text = "Где хранить прайс-лист и сметы";
            header.Font = _bold;
            header.AutoSize = true;
            header.Location = new Point(16, 14);
            Controls.Add(header);

            _filesMode = new RadioButton();
            _filesMode.Text = "Файлы на этом компьютере";
            _filesMode.AutoSize = true;
            _filesMode.Location = new Point(20, 44);
            _filesMode.CheckedChanged += delegate { UpdateEnabled(); };
            Controls.Add(_filesMode);

            _sqlMode = new RadioButton();
            _sqlMode.Text = "База данных PostgreSQL — общая для нескольких рабочих мест";
            _sqlMode.AutoSize = true;
            _sqlMode.Location = new Point(20, 70);
            _sqlMode.CheckedChanged += delegate { UpdateEnabled(); };
            Controls.Add(_sqlMode);

            int top = 104;

            AddLabel("Адрес сервера:", 16, top);
            _host = AddBox(150, top, 180);

            AddLabel("Порт:", 344, top);
            _port = AddBox(392, top, 70);

            top += 32;
            AddLabel("Имя базы:", 16, top);
            _database = AddBox(150, top, 180);

            AddLabel("Пользователь:", 344, top);
            _user = AddBox(446, top, 96);

            top += 32;
            AddLabel("Пароль:", 16, top);
            _password = AddBox(150, top, 180);
            _password.UseSystemPasswordChar = true;

            Button test = MakeButton("Проверить связь", 160);
            test.Location = new Point(344, top - 2);
            test.Click += delegate { TestConnection(); };
            Controls.Add(test);

            top += 40;
            Label note = new Label();
            note.AutoSize = false;
            note.Size = new Size(520, 34);
            note.ForeColor = Color.FromArgb(110, 118, 130);
            note.Location = new Point(16, top);
            note.Text = "Пароль базы не сохраняется на диске: программа спросит его " +
                        "при следующем запуске. Таблицы создаются автоматически.";
            Controls.Add(note);

            top += 44;
            Label webHeader = new Label();
            webHeader.Text = "Веб-версия";
            webHeader.Font = _bold;
            webHeader.AutoSize = true;
            webHeader.Location = new Point(16, top);
            Controls.Add(webHeader);

            top += 26;
            _webEnabled = new CheckBox();
            _webEnabled.Text = "Открыть доступ странице «Смета.html» к этим же данным";
            _webEnabled.AutoSize = true;
            _webEnabled.Location = new Point(20, top);
            _webEnabled.CheckedChanged += delegate { UpdateEnabled(); };
            Controls.Add(_webEnabled);

            top += 28;
            AddLabel("Порт сервиса:", 32, top);
            _webPort = AddBox(150, top, 70);
            Controls.Add(_webPort);

            _status = new Label();
            _status.AutoSize = false;
            _status.Size = new Size(520, 40);
            _status.Location = new Point(16, top + 34);
            _status.ForeColor = Color.FromArgb(40, 90, 40);
            Controls.Add(_status);

            _ok = MakeButton("Сохранить", 150);
            _ok.Font = _bold;
            _ok.Location = new Point(ClientSize.Width - 150 - 130, ClientSize.Height - 46);
            _ok.Click += delegate { Accept(); };
            Controls.Add(_ok);

            Button cancel = MakeButton("Отмена", 110);
            cancel.Location = new Point(ClientSize.Width - 110 - 16, ClientSize.Height - 46);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = _ok;
            CancelButton = cancel;
        }

        private void AddLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y + 3);
            Controls.Add(label);
        }

        private TextBox AddBox(int x, int y, int width)
        {
            TextBox box = new TextBox();
            box.Location = new Point(x, y);
            box.Width = width;
            Controls.Add(box);
            return box;
        }

        private static Button MakeButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 28;
            button.FlatStyle = FlatStyle.System;
            return button;
        }

        private void LoadCurrent()
        {
            _filesMode.Checked = !ConnectionSettings.UseDatabase && ConnectionSettings.DatabaseMode != "sql";
            _sqlMode.Checked = !_filesMode.Checked;

            _host.Text = ConnectionSettings.DatabaseHost;
            _port.Text = ConnectionSettings.DatabasePort.ToString(CultureInfo.InvariantCulture);
            _database.Text = ConnectionSettings.DatabaseName;
            _user.Text = ConnectionSettings.DatabaseUser;
            _password.Text = ConnectionSettings.DatabasePassword;

            _webEnabled.Checked = ConnectionSettings.WebEnabled;
            _webPort.Text = ConnectionSettings.WebPort.ToString(CultureInfo.InvariantCulture);

            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            bool sql = _sqlMode.Checked;

            _host.Enabled = sql;
            _port.Enabled = sql;
            _database.Enabled = sql;
            _user.Enabled = sql;
            _password.Enabled = sql;

            _webPort.Enabled = _webEnabled.Checked;

            _status.Text = "";
        }

        private PgConnectionInfo Read()
        {
            int port;
            if (!int.TryParse(_port.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
                port <= 0 || port > 65535)
                throw new PgException("Порт базы указан неверно: нужно число от 1 до 65535.");

            if (_host.Text.Trim().Length == 0) throw new PgException("Укажите адрес сервера базы данных.");
            if (_database.Text.Trim().Length == 0) throw new PgException("Укажите имя базы данных.");
            if (_user.Text.Trim().Length == 0) throw new PgException("Укажите пользователя базы данных.");

            return new PgConnectionInfo(_host.Text.Trim(), port, _database.Text.Trim(),
                _user.Text.Trim(), _password.Text);
        }

        private void TestConnection()
        {
            try
            {
                PgConnectionInfo info = Read();
                SqlDataStore store = new SqlDataStore(info);
                string error = store.Test();

                if (error != null)
                {
                    _status.ForeColor = Color.FromArgb(170, 40, 40);
                    _status.Text = "Связи нет: " + error;
                    return;
                }

                _status.ForeColor = Color.FromArgb(40, 90, 40);
                _status.Text = "Связь есть. База " + info.Database + " на " + info.Host + ":" + info.Port +
                               " доступна.";
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.FromArgb(170, 40, 40);
                _status.Text = ex.Message;
            }
        }

        private void Accept()
        {
            try
            {
                int webPort;
                if (_webEnabled.Checked &&
                    (!int.TryParse(_webPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out webPort) || webPort <= 0 || webPort > 65535))
                    throw new PgException("Порт сервиса для веб-версии указан неверно.");

                if (_sqlMode.Checked)
                {
                    PgConnectionInfo info = Read();

                    SqlDataStore store = new SqlDataStore(info);
                    string error = store.Test();
                    if (error != null) throw new PgException("Нет связи с базой данных: " + error);

                    store.Prepare();
                    ConnectionSettings.UseSql(info);
                    _status.ForeColor = Color.FromArgb(40, 90, 40);
                    _status.Text = "Работа с базой данных: " + info.Describe();
                }
                else
                {
                    ConnectionSettings.UseFiles();
                }

                ConnectionSettings.WebEnabled = _webEnabled.Checked;
                if (_webEnabled.Checked)
                    ConnectionSettings.WebPort = int.Parse(_webPort.Text.Trim(), CultureInfo.InvariantCulture);

                ConnectionSettings.Save();

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _status.ForeColor = Color.FromArgb(170, 40, 40);
                _status.Text = ex.Message;
            }
        }
    }

    /// <summary>Вход в программу по логину и паролю из базы.</summary>
    internal sealed class LoginForm : Form
    {
        private TextBox _login;
        private TextBox _password;
        private Label _status;
        private readonly SqlDataStore _store;
        private readonly bool _createFirst;

        /// <summary>Сколько раз спрашивать пароль при автоматической проверке.</summary>
        public LoginForm(SqlDataStore store, bool createFirst)
        {
            _store = store;
            _createFirst = createFirst;
            Build();
        }

        public string Login { get { return _login.Text.Trim(); } }

        private void Build()
        {
            Text = _createFirst ? "Создание пользователя — Расчет сметы" : "Вход — Расчет сметы";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 200);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            Label header = new Label();
            header.Text = _createFirst
                ? "В базе ещё нет пользователей. Создайте первого:"
                : "Вход в программу. Данные хранятся в базе " + ConnectionSettings.DatabaseName + ".";
            header.AutoSize = false;
            header.Size = new Size(388, 36);
            header.Location = new Point(16, 14);
            Controls.Add(header);

            Label loginLabel = new Label();
            loginLabel.Text = "Логин:";
            loginLabel.AutoSize = true;
            loginLabel.Location = new Point(16, 62);
            Controls.Add(loginLabel);

            _login = new TextBox();
            _login.Location = new Point(110, 59);
            _login.Width = 290;
            Controls.Add(_login);

            Label passwordLabel = new Label();
            passwordLabel.Text = "Пароль:";
            passwordLabel.AutoSize = true;
            passwordLabel.Location = new Point(16, 96);
            Controls.Add(passwordLabel);

            _password = new TextBox();
            _password.Location = new Point(110, 93);
            _password.Width = 290;
            _password.UseSystemPasswordChar = true;
            Controls.Add(_password);

            _status = new Label();
            _status.AutoSize = false;
            _status.Size = new Size(388, 34);
            _status.Location = new Point(16, 126);
            _status.ForeColor = Color.FromArgb(170, 40, 40);
            Controls.Add(_status);

            Button ok = new Button();
            ok.Text = _createFirst ? "Создать" : "Войти";
            ok.Width = 130;
            ok.Height = 28;
            ok.FlatStyle = FlatStyle.System;
            ok.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            ok.Location = new Point(ClientSize.Width - 130 - 120, ClientSize.Height - 44);
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.Width = 110;
            cancel.Height = 28;
            cancel.FlatStyle = FlatStyle.System;
            cancel.Location = new Point(ClientSize.Width - 110 - 16, ClientSize.Height - 44);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            string login = _login.Text.Trim();
            string password = _password.Text;

            if (login.Length == 0) { _status.Text = "Введите логин."; _login.Focus(); return; }
            if (password.Length == 0) { _status.Text = "Введите пароль."; _password.Focus(); return; }

            try
            {
                if (_createFirst)
                {
                    _store.SaveUser(login, password);
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                if (_store.CheckUser(login, password))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                _status.Text = "Неверный логин или пароль.";
                _password.SelectAll();
                _password.Focus();
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка при обращении к базе: " + ex.Message;
            }
        }
    }

    /// <summary>Простой запрос пароля базы данных при запуске.</summary>
    internal sealed class PasswordForm : Form
    {
        private TextBox _password;

        public PasswordForm(string address)
        {
            Text = "Пароль базы данных";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(400, 150);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(368, 36);
            header.Location = new Point(16, 14);
            header.Text = "Пароль пользователя базы данных для подключения к " + address + ":";
            Controls.Add(header);

            _password = new TextBox();
            _password.Location = new Point(16, 58);
            _password.Width = 368;
            _password.UseSystemPasswordChar = true;
            Controls.Add(_password);

            Button ok = new Button();
            ok.Text = "Продолжить";
            ok.Width = 130;
            ok.Height = 28;
            ok.FlatStyle = FlatStyle.System;
            ok.Location = new Point(ClientSize.Width - 130 - 120, ClientSize.Height - 44);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.Width = 110;
            cancel.Height = 28;
            cancel.FlatStyle = FlatStyle.System;
            cancel.Location = new Point(ClientSize.Width - 110 - 16, ClientSize.Height - 44);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string Password { get { return _password.Text; } }
    }
}
