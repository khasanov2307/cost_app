// ---------------------------------------------------------------------------
//  Дополнительные возможности главного окна:
//    * реквизиты документа (номер и ФИО заказчика) и скидка;
//    * изменение цены позиции прямо в заявке;
//    * предпросмотр заявки и сохранение в Word или текстом;
//    * шаблоны наборов услуг с поиском;
//    * светлая и тёмная тема оформления;
//    * выгрузка и загрузка всех данных одним файлом.
//
//  Файл — часть класса MainForm (partial).
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed partial class MainForm
    {
        // ------------------------------------------------------------- поля

        private AppSettings _settings = new AppSettings();
        private DocumentFields _fields = new DocumentFields();
        private List<ServiceTemplate> _templates = new List<ServiceTemplate>();
        private bool _dark;
        private bool _restoring;

        private TextBox _numberBox;
        private NumericUpDown _discountBox;
        private ComboBox _templateBox;
        private TextBox _templateSearch;
        private Button _btnTemplateApply;
        private Button _btnTemplateDelete;

        private Label _subtotalLabel;
        private Button _btnTheme;
        private Button _btnPreview;
        private Image _logoIcon;            // значок логотипа для нижней панели
        private WebService _web;                   // сервис для веб-версии

        // цена для этой заявки: колонка добавляется последней
        private const int ColPriceEdit = 6;

        private static Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            return label;
        }

        // ------------------------------------------------- панель с реквизитами

        /// <summary>Наборы услуг и реквизиты документа в верхней панели, кнопки — в нижней.</summary>
        private void BuildExtraToolbar(Panel top, Panel bottom)
        {
            top.Height = 206;   // строки: поиск, наборы, реквизиты заявки (три строки)

            // --- первая строка: наборы услуг ---
            Label lblTemplate = MakeLabel("Набор услуг:");
            lblTemplate.Location = new Point(14, 54);
            top.Controls.Add(lblTemplate);

            _templateBox = new ComboBox();
            _templateBox.Location = new Point(104, 50);
            _templateBox.Width = 300;
            _templateBox.DropDownStyle = ComboBoxStyle.DropDown;
            top.Controls.Add(_templateBox);

            _btnTemplateApply = MakeButton("Применить", 110);
            _btnTemplateApply.Location = new Point(416, 49);
            _btnTemplateApply.Click += delegate { ApplyTemplate(); };
            top.Controls.Add(_btnTemplateApply);

            Button btnTemplateSave = MakeButton("Сохранить набор", 160);
            btnTemplateSave.Location = new Point(532, 49);
            btnTemplateSave.Click += delegate { SaveTemplate(); };
            top.Controls.Add(btnTemplateSave);

            _btnTemplateDelete = MakeButton("Удалить", 100);
            _btnTemplateDelete.Location = new Point(698, 49);
            _btnTemplateDelete.Click += delegate { DeleteTemplate(); };
            top.Controls.Add(_btnTemplateDelete);

            Label lblSearch = MakeLabel("Поиск набора:");
            lblSearch.Location = new Point(812, 54);
            top.Controls.Add(lblSearch);

            _templateSearch = new TextBox();
            _templateSearch.Location = new Point(920, 50);
            _templateSearch.Width = 190;
            _templateSearch.TextChanged += Template_Selected;
            top.Controls.Add(_templateSearch);

            // --- строки реквизитов: номер, статус, заказчик, автомобиль ---
            BuildRequisiteRow(top);

            // --- нижняя панель: данные, тема, предпросмотр, подытог ---
            Button btnData = MakeButton("Данные…", 110);
            btnData.Location = new Point(14, 10);
            btnData.Click += delegate { DataMenu(); };
            bottom.Controls.Add(btnData);

            // показ только отмеченных позиций — кнопка над таблицей
            _btnOnlySelected = MakeButton("Показать только выбранные", 230);
            _btnOnlySelected.FlatStyle = FlatStyle.System;
            _btnOnlySelected.Click += delegate { ToggleOnlySelected(); };
            bottom.Controls.Add(_btnOnlySelected);
            _btnOnlySelected.Location = new Point(246, 10);


            _btnTheme = MakeButton("Тёмная", 100);
            _btnTheme.Location = new Point(132, 10);
            _btnTheme.Click += delegate { ToggleTheme(); };
            bottom.Controls.Add(_btnTheme);




            _subtotalLabel = new Label();
            _subtotalLabel.AutoSize = true;
            _subtotalLabel.ForeColor = Color.FromArgb(120, 130, 145);
            _subtotalLabel.Location = new Point(18, 64);
            _subtotalLabel.Text = "";
            bottom.Controls.Add(_subtotalLabel);
        }

        /// <summary>Показ реквизитов, скидки и списка наборов в элементах управления.</summary>
        private void RestoreFormState()
        {
            _restoring = true;
            try
            {
                _numberBox.Text = _fields.Number;
            RestoreCustomerFields();
                _discountBox.Value = AppSettings.ClampDiscount(_fields.Discount);
                FillTemplateList(_settings.LastTemplate);
            }
            finally
            {
                _restoring = false;
            }

            Recalculate();
        }

        private void DocumentFields_Changed(object sender, EventArgs e)
        {
            if (_restoring) return;

            _fields.Number = _numberBox.Text;
            _fields.Customer = _customerPick == null ? _fields.Customer : _customerPick.Text;
            _fields.Discount = _discountBox.Value;
            _fields.Normalize();

            SaveSettings();
            Recalculate();
        }

        private void SaveSettings()
        {
            _settings.Number = _fields.Number;
            _settings.Customer = _fields.Customer;
            _settings.Discount = _fields.Discount;
            _settings.Theme = _dark ? "dark" : "light";

            if (_templateBox != null && _templateBox.Text != null)
                _settings.LastTemplate = _templateBox.Text.Trim();

            _settings.Save();
        }

        // ----------------------------------------------------------- шаблоны

        private void FillTemplateList(string select)
        {
            if (_templateBox == null) return;

            string query = _templateSearch == null ? "" : _templateSearch.Text;
            List<ServiceTemplate> found = TemplateStore.Find(_templates, query);

            _templateBox.BeginUpdate();
            try
            {
                _templateBox.Items.Clear();
                foreach (ServiceTemplate template in found) _templateBox.Items.Add(template.Name);

                if (!string.IsNullOrEmpty(select) && _templateBox.Items.Contains(select))
                    _templateBox.SelectedItem = select;
                else if (_templateBox.Items.Count > 0)
                    _templateBox.SelectedIndex = 0;
            }
            finally
            {
                _templateBox.EndUpdate();
            }

            bool hasTemplates = _templates.Count > 0;
            if (_btnTemplateApply != null) _btnTemplateApply.Enabled = hasTemplates;
            if (_btnTemplateDelete != null) _btnTemplateDelete.Enabled = hasTemplates;
        }

        private void Template_Selected(object sender, EventArgs e)
        {
            FillTemplateList(_templateBox.Text);
        }

        private ServiceTemplate FindTemplate(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (ServiceTemplate template in _templates)
            {
                if (string.Equals(template.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    return template;
            }
            return null;
        }

        /// <summary>Сохранение отмеченных позиций как набора услуг.</summary>
        private void SaveTemplate()
        {
            if (CountPicked() == 0)
            {
                MessageBox.Show(this, "Сначала отметьте услуги, которые войдут в набор.",
                    "Набор услуг", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = _templateBox.Text == null ? "" : _templateBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Введите название набора в поле «Набор услуг».",
                    "Набор услуг", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _templateBox.Focus();
                return;
            }

            ServiceTemplate existing = FindTemplate(name);
            if (existing != null &&
                MessageBox.Show(this, "Набор «" + name + "» уже есть. Заменить его?",
                    "Набор услуг", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            ServiceTemplate template = new ServiceTemplate();
            template.Name = name;
            template.Saved = DateTime.Now;

            foreach (EstimateRow row in _rows)
            {
                if (!row.Selected || row.Quantity <= 0m) continue;

                TemplateItem item = new TemplateItem();
                item.Group = row.Item.Group;
                item.Article = row.Item.Article;
                item.Name = row.Item.Name;
                item.Quantity = row.Quantity;
                template.Items.Add(item);
            }

            _templates = TemplateStore.AddOrReplace(_templates, template);

            try
            {
                TemplateStore.Save(_templates);
                SetStatus("Набор «" + name + "» сохранён: " + template.Items.Count + " позиций");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить набор:\n" + ex.Message,
                    "Набор услуг", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveSettings();
            FillTemplateList(name);
        }

        /// <summary>Отметка позиций из выбранного набора.</summary>
        private void ApplyTemplate()
        {
            ServiceTemplate template = FindTemplate(_templateBox.Text);
            if (template == null)
            {
                MessageBox.Show(this, "Выберите набор услуг в списке.",
                    "Набор услуг", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> missing = new List<string>();
            int applied = 0;

            foreach (TemplateItem item in template.Items)
            {
                EstimateRow target = FindRowByTemplateItem(item);
                if (target == null) { missing.Add(item.Name); continue; }

                target.Selected = true;
                target.Quantity = item.Quantity;
                applied++;
            }

            RestyleRows();

            string message = "Набор «" + template.Name + "»: отмечено позиций — " + applied;
            if (missing.Count > 0)
                message += ", не найдено в каталоге — " + missing.Count;

            SetStatus(message);
            SaveSettings();
        }

        private EstimateRow FindRowByTemplateItem(TemplateItem item)
        {
            EstimateRow byName = null;

            foreach (EstimateRow row in _rows)
            {
                if (!string.IsNullOrEmpty(item.Article) &&
                    string.Equals(row.Item.Article, item.Article, StringComparison.CurrentCultureIgnoreCase))
                    return row;

                if (byName == null &&
                    string.Equals(row.Item.Name, item.Name, StringComparison.CurrentCultureIgnoreCase))
                    byName = row;
            }

            return byName;
        }

        private void DeleteTemplate()
        {
            ServiceTemplate template = FindTemplate(_templateBox.Text);
            if (template == null)
            {
                SetStatus("Выберите набор услуг в списке.");
                return;
            }

            if (MessageBox.Show(this, "Удалить набор «" + template.Name + "»?",
                    "Набор услуг", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _templates.Remove(template);

            try
            {
                TemplateStore.Save(_templates);
                SetStatus("Набор «" + template.Name + "» удалён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить список наборов:\n" + ex.Message,
                    "Набор услуг", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            FillTemplateList("");
        }

        // -------------------------------------------------------------- тема

        private void ToggleTheme()
        {
            _dark = !_dark;
            _settings.Theme = _dark ? "dark" : "light";
            _settings.Save();

            ApplyThemeColors();
            RestyleRows();
            SetStatus(_dark ? "Включена тёмная тема." : "Включена светлая тема.");
        }

        /// <summary>Цвета окна по выбранной теме.</summary>
        private void ApplyThemeColors()
        {
            Color window = _dark ? Color.FromArgb(32, 36, 44) : SystemColors.Control;
            Color panel = _dark ? Color.FromArgb(40, 45, 55) : Color.White;
            Color text = _dark ? Color.FromArgb(232, 236, 244) : Color.FromArgb(30, 30, 30);

            BackColor = window;
            ForeColor = text;

            foreach (Control control in Controls)
            {
                if (control is DataGridView) continue;
                if (control is StatusStrip) continue;

                control.BackColor = panel;

                foreach (Control child in control.Controls)
                {
                    if (child is TextBox || child is ComboBox || child is NumericUpDown)
                    {
                        child.BackColor = _dark ? Color.FromArgb(52, 58, 70) : Color.White;
                        child.ForeColor = text;
                    }
                    else if (child is Button || child is Label)
                    {
                        if (child is Button) child.BackColor = _dark ? Color.FromArgb(58, 64, 78) : SystemColors.Control;
                        child.ForeColor = text;
                    }
                }
            }

            _grid.BackgroundColor = _dark ? Color.FromArgb(36, 40, 48) : Color.White;
            _grid.GridColor = _dark ? Color.FromArgb(70, 76, 88) : Splitter;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = _dark ? Color.FromArgb(52, 58, 70) : HeaderBack;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = _dark ? Color.FromArgb(232, 236, 244) : HeaderFore;

            if (_btnTheme != null) _btnTheme.Text = _dark ? "Светлая" : "Тёмная";
            if (_totalLabel != null)
                _totalLabel.ForeColor = _dark ? Color.FromArgb(140, 190, 255) : Color.FromArgb(20, 70, 130);
            if (_countLabel != null)
                _countLabel.ForeColor = _dark ? Color.FromArgb(170, 180, 195) : Color.FromArgb(90, 100, 115);
            if (_subtotalLabel != null)
                _subtotalLabel.ForeColor = _dark ? Color.FromArgb(170, 180, 195) : Color.FromArgb(120, 130, 145);

            _grid.Invalidate();
        }

        /// <summary>Переоформление строк таблицы после смены темы или набора.</summary>
        private void RestyleRows()
        {
            _restoring = true;
            try
            {
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    EstimateRow row = gridRow.Tag as EstimateRow;
                    if (row == null) continue;

                    gridRow.Cells[ColCheck].Value = row.Selected;
                    gridRow.Cells[ColQty].Value = row.Quantity;
                    gridRow.Cells[ColQty].Tag = row.Quantity;
                    gridRow.Cells[ColPriceEdit].Value = row.Price;
                    gridRow.Cells[ColSum].Value = row.Sum;
                    StyleItemRow(gridRow, row.Selected);
                }
            }
            finally
            {
                _restoring = false;
            }

            Recalculate();
            _grid.Invalidate();
        }

        // ------------------------------------------------------ предпросмотр

        /// <summary>Окно предпросмотра заявки.</summary>
        // ---------------------------------------------- логотип компании

        /// <summary>Выбор файла логотипа для печатной формы.</summary>
        /// <summary>Останов сервиса при закрытии главного окна.</summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopWebService();
        }

        private void ChooseLogo()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Логотип компании для заявки";
            dialog.Filter = "Изображения (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif|Все файлы (*.*)|*.*";
            dialog.InitialDirectory = PriceBook.StoreFolder;

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            Logo logo = Logo.Read(dialog.FileName);
            if (logo == null)
            {
                MessageBox.Show(this,
                    "Не удалось прочитать изображение.\n\n" +
                    "Подойдут файлы PNG, JPEG или GIF.",
                    "Логотип компании", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.Logo = dialog.FileName;
            _settings.Save();
            ShowLogo();

            SetStatus("Логотип компании: " + Path.GetFileName(dialog.FileName) +
                      " (" + logo.Width + "×" + logo.Height + " в документе)");
        }

        /// <summary>Показ значка логотипа и подписи кнопки.</summary>
        private void ShowLogo()
        {
            bool hasLogo = !string.IsNullOrEmpty(_settings.Logo);
            string logoTitle = hasLogo ? "Сменить логотип компании…" : "Логотип компании…";


            if (_logoIcon != null)
            {
                Image old = _logoIcon;
                _logoIcon = null;
                old.Dispose();
            }

            if (hasLogo)
            {
                try
                {
                    // читаем через поток, чтобы файл не оставался занятым
                    using (FileStream stream = new FileStream(_settings.Logo, FileMode.Open, FileAccess.Read))
                    {
                        _logoIcon = LogoIconFrom(stream);
                    }
                }
                catch
                {
                    _settings.Logo = "";
                    _settings.Save();
                    SetStatus("Файл логотипа не найден — логотип отключён.");
                }
            }


        }

        /// <summary>Значок для нижней панели: уменьшенная копия логотипа.</summary>
        private static Image LogoIconFrom(Stream stream)
        {
            using (Image source = Image.FromStream(stream))
            {
                int width = 40;
                int height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
                if (height > 26)
                {
                    height = 26;
                    width = Math.Max(1, (int)Math.Round(source.Width * (double)height / source.Height));
                }

                Bitmap small = new Bitmap(width, height);
                using (Graphics graphics = Graphics.FromImage(small))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, width, height);
                }
                return small;
            }
        }

        // ---------------------------------------- подключение и веб-сервис

        /// <summary>Окно выбора хранилища: файлы или база данных.</summary>
        private void OpenConnectionSettings()
        {
            using (ConnectionForm dialog = new ConnectionForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }

            try
            {
                ReloadPricesPreserving();
                ApplyThemeColors();
                ShowLogo();
                SetStatus("Хранилище: " + ConnectionSettings.Store.Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось перечитать данные:\n" + ex.Message,
                    "Подключение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartWebService();
        }

        /// <summary>Запуск сервиса для веб-версии, если он включён в настройках.</summary>
        private void StartWebService()
        {
            if (_web != null)
            {
                _web.Dispose();
                _web = null;
            }

            if (!ConnectionSettings.WebEnabled) return;

            _web = new WebService(ConnectionSettings.Store, new SessionEstimateStore(), ConnectionSettings.WebPort);
            string error = _web.Start();

            if (error != null)
            {
                SetStatus(error);
                MessageBox.Show(this, error, "Веб-версия", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _web = null;
                return;
            }

            SetStatus("Сервис для веб-версии открыт: " + _web.Address);
        }

        /// <summary>Останов сервиса при закрытии программы.</summary>
        private void StopWebService()
        {
            if (_web == null) return;
            _web.Dispose();
            _web = null;
        }

        private void ShowPreview()
        {
            if (CountPicked() == 0)
            {
                MessageBox.Show(this, "Не отмечено ни одной услуги.", "Предпросмотр заявки",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string text = DocumentBuilder.ToText(BuildEstimateDocument());

            using (Form dialog = new Form())
            {
                dialog.Text = "Предпросмотр заявки";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimumSize = new Size(780, 540);
                dialog.ClientSize = new Size(920, 660);
                dialog.Font = _baseFont;
                dialog.ShowInTaskbar = false;

                TextBox view = new TextBox();
                view.Multiline = true;
                view.ReadOnly = true;
                view.ScrollBars = ScrollBars.Both;
                view.WordWrap = false;
                view.Font = _monoFont;
                view.Dock = DockStyle.Fill;
                view.Text = text;
                view.BackColor = Color.White;
                view.ForeColor = Color.FromArgb(25, 28, 34);

                Panel actions = new Panel();
                actions.Dock = DockStyle.Bottom;
                actions.Height = 54;
                actions.Padding = new Padding(12, 10, 12, 10);

                Button btnSave = MakeButton("Сохранить в Word", 168);
                btnSave.Font = _boldFont;
                btnSave.Location = new Point(12, 11);
                btnSave.Click += delegate
                {
                    dialog.Close();
                    GenerateDocument();
                };
                actions.Controls.Add(btnSave);

                Button btnCopyText = MakeButton("Копировать текстом", 176);
                btnCopyText.Location = new Point(190, 11);
                btnCopyText.Click += delegate
                {
                    try
                    {
                        Clipboard.SetText(text);
                        SetStatus("Заявка скопирована в буфер обмена.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "Не удалось скопировать:\n" + ex.Message,
                            "Предпросмотр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                actions.Controls.Add(btnCopyText);

                Button btnClose = MakeButton("Закрыть", 110);
                btnClose.Location = new Point(376, 11);
                btnClose.Click += delegate { dialog.Close(); };
                actions.Controls.Add(btnClose);

                Label hint = new Label();
                hint.AutoSize = true;
                hint.ForeColor = Color.FromArgb(120, 130, 145);
                hint.Location = new Point(500, 17);
                hint.Text = "Так будет выглядеть готовая заявка";
                actions.Controls.Add(hint);

                dialog.Controls.Add(view);
                dialog.Controls.Add(actions);
                dialog.CancelButton = btnClose;

                dialog.ShowDialog(this);
            }
        }

        // ----------------------------------------------------- обмен данными

        private void DataMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Подключение к базе данных…", null, delegate { OpenConnectionSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Логотип компании…", null, delegate { ChooseLogo(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Кассы и балансы…", null, delegate { OpenCashDesks(); });
            menu.Items.Add("Показатели…", null, delegate { OpenDashboard(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Заказчики…", null, delegate { OpenCustomers(); });
            menu.Items.Add("Склад…", null, delegate { OpenWarehouse(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Чек по заявке…", null, delegate { ShowReceipt(_fields.Number.Trim()); });
            menu.Items.Add("Повторить прошлую заявку…", null, delegate { CopyLastEstimate(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Горячие клавиши", null, delegate { ShowHotKeys(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выгрузить все данные…", null, delegate { ExportData(); });
            menu.Items.Add("Загрузить данные…", null, delegate { ImportData(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Открыть папку с данными", null, delegate { OpenDataFolder(); });

            menu.Show(_btnTheme, new Point(0, _btnTheme.Height));
        }

        /// <summary>Выгрузка всех данных программы в один файл.</summary>
        private void ExportData()
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "Выгрузить все данные программы";
            dialog.Filter = "Данные программы (*.zip)|*.zip|Все файлы (*.*)|*.*";
            dialog.InitialDirectory = PriceBook.StoreFolder;
            dialog.FileName = FullBackup.SuggestedFileName(DateTime.Now);

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                FullBackup.Save(dialog.FileName);

                SetStatus("Все данные выгружены: " + dialog.FileName +
                          "   •   позиций: " + _catalog.Count +
                          ", наборов: " + _templates.Count);

                MessageBox.Show(this,
                    "Данные выгружены в файл:\\n" + dialog.FileName + "\\n\\n" +
                    "В него попали каталог, наборы услуг, реквизиты с темой и логотипом, " +
                    "заявки со статусами, заказчики, склад и кассы с оплатами.\\n\\n" +
                    "Сохраните файл: он понадобится при переходе на новую версию программы.",
                    "Выгрузка данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось выгрузить данные:\\n" + ex.Message,
                    "Выгрузка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Загрузка всех данных программы из файла выгрузки.</summary>
        private void ImportData()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Загрузить все данные программы";
            dialog.Filter = "Данные программы (*.zip)|*.zip|Все файлы (*.*)|*.*";
            dialog.InitialDirectory = PriceBook.StoreFolder;

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            string error;
            BackupContent content = FullBackup.Inspect(dialog.FileName, out error);

            if (content == null)
            {
                MessageBox.Show(this, "Файл не подходит:\\n" + error,
                    "Загрузка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string question =
                "Загрузить данные из файла?\\n\\n" +
                "Выгружен: " + content.Created + "\\n" +
                "Из хранилища: " + content.Source + "\\n" +
                "Содержимое: " + content.Summary() + "\\n\\n" +
                "Данные текущего хранилища будут заменены по этим разделам.";

            if (MessageBox.Show(this, question, "Загрузка данных",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            try
            {
                BackupContent loaded = FullBackup.Restore(dialog.FileName);

                // перечитываем всё, что показано на экране
                ReloadEverything();

                SetStatus("Данные загружены: " + loaded.Summary());

                MessageBox.Show(this, "Данные загружены.\\n\\n" + loaded.Summary(),
                    "Загрузка данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось загрузить данные:\\n" + ex.Message,
                    "Загрузка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Перечитать данные после загрузки из файла.</summary>
        private void ReloadEverything()
        {
            _cashLoaded = false;
            _customersLoaded = false;
            _warehouseLoaded = false;

            ReloadPricesPreserving();
            RestoreFormState();
        }


        private void OpenDataFolder()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    PriceBook.StoreFolder) { UseShellExecute = true });
                SetStatus("Папка с данными: " + PriceBook.StoreFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку:\n" + ex.Message,
                    "Обмен данными", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // -------------------------------------------------- документ и цены

        /// <summary>Документ заявки по текущим отметкам и реквизитам.</summary>
        private EstimateDocument BuildEstimateDocument()
        {
            return DocumentBuilder.Build(_rows, _fields, "", DateTime.Now);
        }

        /// <summary>Разбор цены, введённой в заявке.</summary>
        private static bool TryParsePrice(string text, out decimal price)
        {
            price = 0m;
            if (string.IsNullOrEmpty(text)) return false;

            string cleaned = text.Replace("\u20BD", "").Replace("\u00A0", "")
                                 .Replace(" ", "").Replace("'", "").Trim();
            if (cleaned.Length == 0) return false;

            if (decimal.TryParse(cleaned, NumberStyles.Number, Fmt.Ru, out price) && price >= 0m) return true;

            cleaned = cleaned.Replace(',', '.');
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out price)
                && price >= 0m)
                return true;

            return false;
        }
    }
}
