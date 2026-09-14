// ---------------------------------------------------------------------------
//  Окно «Редактор прайс-листа».
//
//  Позволяет править справочник цен прямо в программе: менять группы,
//  наименования, единицы измерения и цены, добавлять, дублировать, удалять
//  и переставлять позиции. Результат записывается в тот же файл TSV, из
//  которого программа читает цены.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Строка прайс-листа в редакторе.</summary>
    internal sealed class PriceRow
    {
        public string Group = "";
        public string Article = "";
        public string Name = "";
        public string Unit = "шт.";
        public decimal Price;

        public PriceRow() { }

        public PriceRow(string group, string name, string unit, decimal price)
        {
            Group = group;
            Name = name;
            Unit = unit;
            Price = price;
        }

        public PriceRow(string group, string article, string name, string unit, decimal price)
        {
            Group = group;
            Article = article;
            Name = name;
            Unit = unit;
            Price = price;
        }

        public PriceRow Clone()
        {
            return new PriceRow(Group, Article, Name, Unit, Price);
        }
    }

    internal sealed class PriceEditorForm : Form
    {
        private readonly List<PriceRow> _rows = new List<PriceRow>();
        private readonly string _storePath = PriceBook.StorePath;
        private readonly HashSet<string> _collapsed =
            new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        // позиции, добавленные в этом сеансе — показываем их жёлтым
        private readonly List<PriceRow> _newRows = new List<PriceRow>();

        // разделы, созданные в этом сеансе — тоже подсвечиваем, пока не сохранены
        private readonly List<string> _newSections = new List<string>();

        private DataGridView _grid;
        private TextBox _find;
        private Label _info;
        private ToolStripStatusLabel _statusText;

        private Button _btnAdd;
        private Button _btnDuplicate;
        private Button _btnDelete;
        private Button _btnUp;
        private Button _btnDown;
        private Button _btnRename;
        private Button _btnAddSection;
        private Button _btnGroup;
        private Button _btnSort;
        private Button _btnSave;
        private Button _btnCancel;

        private DataGridViewTextBoxColumn _colGroup;
        private DataGridViewTextBoxColumn _colArticle;
        private DataGridViewTextBoxColumn _colName;
        private DataGridViewComboBoxColumn _colUnit;
        private DataGridViewTextBoxColumn _colPrice;

        private const int ColToggle = 0;    // знак «свернуть/развернуть раздел»
        private const int ColGroup = 1;     // раздел
        private const int ColArticle = 2;   // артикул
        private const int ColName = 3;      // наименование услуги
        private const int ColUnit = 4;      // единица измерения (выбор из перечня)
        private const int ColPrice = 5;     // цена
        private const string ClearWord = "ПОЛНОСТЬЮ";   // слово подтверждения очистки

        private Font _baseFont;
        private Font _boldFont;
        private bool _loading;
        private bool _dirty;


        private static readonly Color HeaderBack = Color.FromArgb(232, 236, 242);
        private static readonly Color HeaderFore = Color.FromArgb(28, 40, 60);
        private static readonly Color Splitter = Color.FromArgb(198, 206, 218);
        private static readonly Color NewBack = Color.FromArgb(255, 253, 235);
        private static readonly Color BadBack = Color.FromArgb(255, 228, 228);

        public PriceEditorForm()
        {
            _baseFont = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            _boldFont = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);

            BuildInterface();          // создаёт таблицу внутри себя
            LoadFromDisk();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_baseFont != null) _baseFont.Dispose();
                if (_boldFont != null) _boldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>Позиции, сохранённые в файл (для передачи в главное окно).</summary>
        public List<ServiceItem> SavedItems { get; private set; }

        /// <summary>Готовый ответ для переименования раздела (для автотестов).</summary>
        public string PendingRenameAnswer;

        /// <summary>Готовое название нового раздела (для автотестов).</summary>
        public string PendingSectionAnswer;

        /// <summary>Готовый ответ для подтверждения очистки (для автотестов).</summary>
        public string PendingClearAnswer;

        /// <summary>Ответ на вопрос о заводском прайсе (для автотестов).</summary>
        public bool? PendingRestoreAnswer;

        // ------------------------------------------------------- интерфейс

        private void BuildInterface()
        {
            Text = "Прайс-лист — Расчет сметы";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(940, 560);
            ClientSize = new Size(1040, 660);
            Font = _baseFont;
            ShowInTaskbar = false;
            MaximizeBox = true;
            MinimizeBox = false;

            // ---------- нижняя панель ----------
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 112;
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Splitter))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            _info = new Label();
            _info.AutoSize = true;
            _info.ForeColor = Color.FromArgb(90, 100, 115);
            _info.Location = new Point(14, 10);
            _info.Text = "позиций: 0";
            bottom.Controls.Add(_info);

            Label hint = new Label();
            hint.AutoSize = false;
            hint.ForeColor = Color.FromArgb(110, 118, 130);
            hint.Location = new Point(16, 26);
            hint.Size = new Size(430, 20);
            hint.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            hint.Text = "Раздел переименовывается целиком — услуги правятся в таблице.";
            bottom.Controls.Add(hint);

            // Слева — действия со всем прайс-листом целиком.
            Button btnClear = MakeButton("Очистить прайс", 160);
            btnClear.Location = new Point(14, 62);
            btnClear.ForeColor = Color.FromArgb(160, 40, 40);
            btnClear.Click += delegate { ClearAllRows(); };
            bottom.Controls.Add(btnClear);

            Button btnFactory = MakeButton("Вернуть заводской", 190);
            btnFactory.Location = new Point(182, 62);
            btnFactory.Click += delegate { RestoreFactoryPrices(); };
            bottom.Controls.Add(btnFactory);

            _btnSave = MakeButton("Сохранить", 118);
            _btnSave.Font = _boldFont;
            _btnSave.Click += delegate { SaveChanges(); };

            _btnCancel = MakeButton("Отмена", 100);
            _btnCancel.Click += delegate { Close(); };

            Button btnImport = MakeButton("Загрузить из файла", 158);
            btnImport.Click += delegate { ImportFromFile(); };

            Button btnExport = MakeButton("Выгрузить в файл", 154);
            btnExport.Click += delegate { ExportCurrent(); };

            Button btnSection = MakeButton("Новый раздел", 136);
            btnSection.Click += delegate { AddSection(); };
            _btnAddSection = btnSection;

            _btnGroup = MakeButton("Группировать", 124);
            _btnGroup.Click += delegate { GroupByGroup(); };

            _btnSort = MakeButton("Сортировать", 114);
            _btnSort.Click += delegate { SortByName(); };

            _btnRename = MakeButton("Переименовать раздел", 178);
            _btnRename.Click += delegate
            {
                if (string.IsNullOrEmpty(PendingRenameAnswer)) RenameGroup();
                else
                {
                    PriceRow pick = null;
                    List<PriceRow> sel = SelectedRows();
                    if (sel.Count > 0) pick = sel[0];
                    else if (_grid.CurrentRow != null) pick = _grid.CurrentRow.Tag as PriceRow;
                    if (pick != null) RenameGroupTo(pick.Group, PendingRenameAnswer);
                    PendingRenameAnswer = null;
                }
            };

            // Кнопок много, поэтому раскладываем их двумя рядами фиксированной высоты:
            // так они не «плывут» при изменении размера окна.
            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Right;
            actions.ColumnCount = 4;
            actions.RowCount = 2;
            actions.AutoSize = true;
            actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actions.Padding = new Padding(0, 6, 12, 0);
            actions.Margin = new Padding(0);

            FlowLayoutPanel rowTop = new FlowLayoutPanel();
            rowTop.FlowDirection = FlowDirection.RightToLeft;
            rowTop.WrapContents = false;
            rowTop.AutoSize = true;
            rowTop.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rowTop.Margin = new Padding(0, 0, 0, 3);
            rowTop.Controls.Add(_btnSave);
            rowTop.Controls.Add(_btnCancel);
            rowTop.Controls.Add(btnExport);
            rowTop.Controls.Add(btnImport);

            FlowLayoutPanel rowBottom = new FlowLayoutPanel();
            rowBottom.FlowDirection = FlowDirection.RightToLeft;
            rowBottom.WrapContents = false;
            rowBottom.AutoSize = true;
            rowBottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rowBottom.Margin = new Padding(0);
            rowBottom.Controls.Add(btnSection);
            rowBottom.Controls.Add(_btnRename);
            rowBottom.Controls.Add(_btnGroup);
            rowBottom.Controls.Add(_btnSort);

            actions.Controls.Add(rowTop, 0, 0);
            actions.SetColumnSpan(rowTop, 4);
            actions.Controls.Add(rowBottom, 0, 1);
            actions.SetColumnSpan(rowBottom, 4);
            bottom.Controls.Add(actions);

            // ---------- верхняя панель ----------
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 46;

            Label lblFind = new Label();
            lblFind.Text = "Поиск:";
            lblFind.AutoSize = true;
            lblFind.Location = new Point(14, 15);
            top.Controls.Add(lblFind);

            _find = new TextBox();
            _find.Location = new Point(70, 11);
            _find.Width = 200;
            _find.TextChanged += delegate { ApplySearch(); };
            top.Controls.Add(_find);

            _btnAdd = MakeButton("Добавить", 104);
            _btnAdd.Click += delegate { AddRows(); };
            top.Controls.Add(_btnAdd);

            _btnDuplicate = MakeButton("Дублировать", 118);
            _btnDuplicate.Click += delegate { DuplicateRows(); };
            top.Controls.Add(_btnDuplicate);

            _btnDelete = MakeButton("Удалить", 96);
            _btnDelete.Click += delegate { DeleteRows(); };
            top.Controls.Add(_btnDelete);

            _btnUp = MakeButton("Выше", 76);
            _btnUp.Click += delegate { MoveRows(-1); };
            top.Controls.Add(_btnUp);

            _btnDown = MakeButton("Ниже", 76);
            _btnDown.Click += delegate { MoveRows(1); };
            top.Controls.Add(_btnDown);


            // Кнопки строк выстраиваются слева направо после поля поиска
            EventHandler placeTop = delegate
            {
                int x = 284;                       // после поля поиска
                _btnAdd.Location = new Point(x, 10); x = _btnAdd.Right + 8;
                _btnDuplicate.Location = new Point(x, 10); x = _btnDuplicate.Right + 8;
                _btnDelete.Location = new Point(x, 10); x = _btnDelete.Right + 8;
                _btnUp.Location = new Point(x, 10); x = _btnUp.Right + 8;
                _btnDown.Location = new Point(x, 10);
            };
            top.Resize += placeTop;

            // ---------- строка состояния ----------
            StatusStrip status = new StatusStrip();
            status.SizingGrip = false;
            _statusText = new ToolStripStatusLabel(
                "Прайс-лист хранится в приложении: " + _storePath);
            status.Items.Add(_statusText);

            _grid = BuildGrid();               // таблица создаётся ровно один раз
            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(top);
            Controls.Add(status);

            _grid.BringToFront();

            placeTop(null, EventArgs.Empty);
        }

        private DataGridView BuildGrid()
        {
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AllowUserToOrderColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;
            _grid.GridColor = Splitter;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.RowTemplate.Height = 30;
            _grid.ScrollBars = ScrollBars.Vertical;
            _grid.StandardTab = true;
            TryEnableDoubleBuffer(_grid);

            DataGridViewTextBoxColumn colToggle = new DataGridViewTextBoxColumn();
            colToggle.Name = "toggle";
            colToggle.HeaderText = "\u2013";
            colToggle.Width = 34;
            colToggle.MinimumWidth = 34;
            colToggle.FillWeight = 5f;
            colToggle.ReadOnly = true;
            colToggle.SortMode = DataGridViewColumnSortMode.NotSortable;
            colToggle.Resizable = DataGridViewTriState.False;
            colToggle.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colToggle.DefaultCellStyle.ForeColor = Color.FromArgb(40, 70, 120);
            colToggle.DefaultCellStyle.Font = new Font("Segoe UI", 11f, FontStyle.Bold);

            _colGroup = new DataGridViewTextBoxColumn();
            _colGroup.Name = "group";
            _colGroup.HeaderText = "Раздел";
            _colGroup.FillWeight = 24f;
            _colGroup.SortMode = DataGridViewColumnSortMode.NotSortable;

            _colArticle = new DataGridViewTextBoxColumn();
            _colArticle.Name = "article";
            _colArticle.HeaderText = "Артикул";
            _colArticle.FillWeight = 13f;
            _colArticle.SortMode = DataGridViewColumnSortMode.NotSortable;

            _colName = new DataGridViewTextBoxColumn();
            _colName.Name = "name";
            _colName.HeaderText = "Наименование услуги";
            _colName.FillWeight = 41f;
            _colName.SortMode = DataGridViewColumnSortMode.NotSortable;
            _colName.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            _colUnit = new DataGridViewComboBoxColumn();
            _colUnit.Name = "unit";
            _colUnit.HeaderText = "Ед. изм.";
            _colUnit.FillWeight = 13f;
            _colUnit.SortMode = DataGridViewColumnSortMode.NotSortable;
            _colUnit.FlatStyle = FlatStyle.Flat;
            _colUnit.DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox;
            _colUnit.DisplayStyleForCurrentCellOnly = false;
            _colUnit.Items.AddRange(Uom.List);
            _colPrice = new DataGridViewTextBoxColumn();
            _colPrice.Name = "price";
            _colPrice.HeaderText = "Цена, \u20BD";
            _colPrice.FillWeight = 14f;
            _colPrice.SortMode = DataGridViewColumnSortMode.NotSortable;
            _colPrice.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            _grid.Columns.AddRange(new DataGridViewColumn[]
                { colToggle, _colGroup, _colArticle, _colName, _colUnit, _colPrice });

            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellParsing += Grid_CellParsing;
            _grid.CellValidating += Grid_CellValidating;
            _grid.CellClick += Grid_CellClick;      // надёжно срабатывает и на строках-заголовках
            _grid.RowHeightInfoNeeded += Grid_RowHeightInfoNeeded;
            _grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e)
            {
                e.ThrowException = false;
            };

            return _grid;
        }

        private static Button MakeButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 28;
            button.UseVisualStyleBackColor = true;
            return button;
        }

        private static void TryEnableDoubleBuffer(DataGridView grid)
        {
            try
            {
                System.Reflection.PropertyInfo property = typeof(DataGridView)
                    .GetProperty("DoubleBuffered",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                if (property != null) property.SetValue(grid, true, null);
            }
            catch { /* не критично */ }
        }
        // ---------------------------------------------- загрузка и запись

        // ------------------------------------- очистка и возврат завода

        /// <summary>Удаление всех позиций прайс-листа с подтверждением.</summary>
        private void ClearAllRows()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            int total = _rows.Count;
            if (total == 0)
            {
                SetStatus("Прайс-лист и так пуст.");
                return;
            }

            string answer;
            if (!ConfirmClear(total, out answer))
            {
                SetStatus(answer == null
                    ? "Очистка отменена."
                    : "Очистка отменена: слово подтверждения введено неверно.");
                return;
            }

            _rows.Clear();
            _newRows.Clear();
            _newSections.Clear();

            RefreshGrid();
            SetInfo();
            MarkDirty(true);

            SetStatus("Прайс-лист очищен: удалено позиций — " + total +
                      ". Нажмите «Сохранить», чтобы записать пустой прайс-лист.");
        }

        /// <summary>Возврат заводского прайс-листа с подтверждением.</summary>
        private void RestoreFactoryPrices()
        {
            int total = _rows.Count;

            bool confirmed;
            if (PendingRestoreAnswer.HasValue)
            {
                confirmed = PendingRestoreAnswer.Value;
                PendingRestoreAnswer = null;
            }
            else
            {
                confirmed = MessageBox.Show(this,
                    total == 0
                        ? "Загрузить заводской прайс-лист?"
                        : "Заменить текущий прайс-лист (" + total + " позиций) заводским?",
                    "Заводской прайс-лист", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                    != DialogResult.Cancel;
            }

            if (confirmed)
            {
                // восстанавливаем заводской набор: PriceBook сразу записывает его в хранилище
                List<ServiceItem> items = PriceBook.RestoreDefaults();

                if (items == null || items.Count == 0)
                {
                    MessageBox.Show(this, "Заводской прайс-лист пуст.",
                        "Заводской прайс-лист", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _rows.Clear();
                foreach (ServiceItem item in items)
                    _rows.Add(new PriceRow(item.Group, item.Article, item.Name, item.Unit, item.Price));

                _newRows.Clear();
                _newSections.Clear();

                RefreshGrid();
                SetInfo();
                MarkDirty(true);

                SetStatus("Заводской прайс-лист восстановлен и записан: позиций — " + items.Count + ".");
            }
        }

        /// <summary>
        /// Подтверждение очистки: нужно ввести слово ПОЛНОСТЬЮ.
        /// Для автотестов ответ можно задать заранее.
        /// </summary>
        private bool ConfirmClear(int total, out string answer)
        {
            if (PendingClearAnswer != null)
            {
                answer = PendingClearAnswer;
                PendingClearAnswer = null;
                return string.Equals(answer.Trim(), ClearWord, StringComparison.CurrentCultureIgnoreCase);
            }

            using (Form dialog = new Form())
            {
                dialog.Text = "Очистка прайс-листа";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.Font = _baseFont;
                dialog.ClientSize = new Size(500, 208);

                Label message = new Label();
                message.AutoSize = false;
                message.Location = new Point(16, 14);
                message.Size = new Size(468, 76);
                message.Text = "Будут удалены все позиции прайс-листа: " + total +
                               ". Вместе с ними пропадут цены и разделы." + Environment.NewLine +
                               Environment.NewLine +
                               "Сначала прайс-лист сохраняется в резервную копию, поэтому " +
                               "его можно будет вернуть. Но записи в сметах на удалённые " +
                               "позиции станут пустыми.";
                dialog.Controls.Add(message);

                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(16, 98);
                prompt.Text = "Для подтверждения введите слово " + ClearWord + ":";
                dialog.Controls.Add(prompt);

                TextBox input = new TextBox();
                input.Location = new Point(16, 122);
                input.Width = 468;
                input.Font = _boldFont;
                dialog.Controls.Add(input);

                Button ok = MakeButton("Очистить", 140);
                ok.Location = new Point(200, 160);
                ok.ForeColor = Color.FromArgb(160, 40, 40);
                ok.Enabled = false;
                dialog.Controls.Add(ok);

                Button cancel = MakeButton("Отмена", 110);
                cancel.Location = new Point(350, 160);
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(cancel);

                input.TextChanged += delegate
                {
                    ok.Enabled = string.Equals(input.Text.Trim(), ClearWord,
                                               StringComparison.CurrentCultureIgnoreCase);
                };

                ok.Click += delegate { dialog.DialogResult = DialogResult.OK; };

                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    answer = input.Text;
                    return true;
                }

                answer = null;
                return false;
            }
        }

        private void LoadFromDisk()
        {
            string error;
            List<ServiceItem> items = PriceBook.Load(out error);

            _rows.Clear();
            foreach (ServiceItem item in items)
                _rows.Add(new PriceRow(item.Group, item.Article, item.Name, item.Unit, item.Price));

            _dirty = false;
            _newRows.Clear();
            _newSections.Clear();
            RefreshGrid();
            SetInfo();

            if (!string.IsNullOrEmpty(error)) SetStatus(error);
            else SetStatus("Загружено позиций: " + items.Count);
        }

        /// <summary>Полная перерисовка таблицы по списку _rows.</summary>
        public void RefreshGrid()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            _loading = true;
            try
            {
                _grid.Rows.Clear();

                string lastGroup = null;
                foreach (PriceRow row in _rows)
                {
                    if (!string.Equals(lastGroup, row.Group, StringComparison.CurrentCultureIgnoreCase))
                    {
                        lastGroup = row.Group;
                        // В строке-заголовке раздела только название раздела:
                        // наименование услуги здесь не показывается.
                        int header = _grid.Rows.Add(new object[]
                            { "\u2013", row.Group, "", "", "", null });
                        DataGridViewRow headerRow = _grid.Rows[header];
                        headerRow.ReadOnly = true;
                        headerRow.Cells[ColToggle].ReadOnly = false;   // только переключатель активен

                        DataGridViewCellStyle headerStyle = new DataGridViewCellStyle();
                        headerStyle.BackColor = HeaderBack;
                        headerStyle.ForeColor = HeaderFore;
                        headerStyle.Font = _boldFont;
                        headerStyle.SelectionBackColor = HeaderBack;
                        headerStyle.SelectionForeColor = HeaderFore;
                        for (int i = 0; i < headerRow.Cells.Count; i++) headerRow.Cells[i].Style = headerStyle;

                        DataGridViewCellStyle toggleStyle = new DataGridViewCellStyle(headerStyle);
                        toggleStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        headerRow.Cells[ColToggle].Style = toggleStyle;

                        // в строке-заголовке раздела список единиц измерения не показываем
                        ((DataGridViewComboBoxCell)headerRow.Cells[ColUnit]).DisplayStyle =
                            DataGridViewComboBoxDisplayStyle.Nothing;
                    }

                    int index = _grid.Rows.Add(new object[]
                        { "", row.Group, row.Article, row.Name, row.Unit, row.Price });
                    DataGridViewRow gridRow = _grid.Rows[index];
                    gridRow.Tag = row;

                    if (_newRows.Contains(row) || _newSections.Contains(row.Group))
                    {
                        DataGridViewCellStyle style = new DataGridViewCellStyle();
                        style.BackColor = NewBack;
                        style.SelectionBackColor = Color.FromArgb(250, 232, 160);
                        for (int i = 0; i < gridRow.Cells.Count; i++) gridRow.Cells[i].Style = style;
                    }
                }
            }
            finally
            {
                _loading = false;
            }

            EnsureUnitsInList();
            ApplySearch();
            SyncCurrentRow();
            MarkDirty(_dirty);
        }

        /// <summary>
        /// Добавляет в выпадающий список единицы измерения, которые уже есть в данных,
        /// но не входят в стандартный перечень (например, оставшиеся от прежних версий).
        /// </summary>
        private void EnsureUnitsInList()
        {
            foreach (PriceRow row in _rows)
            {
                string unit = row.Unit;
                if (string.IsNullOrEmpty(unit)) continue;
                if (_colUnit.Items.Contains(unit)) continue;
                _colUnit.Items.Add(unit);
            }
        }

        /// <summary>
        /// Строка, на которую смотрит таблица, должна соответствовать данным:
        /// иначе программная правка ячейки попадёт в другую позицию списка.
        /// </summary>
        private void SyncCurrentRow()
        {
            DataGridViewRow current = _grid.CurrentRow;
            if (current != null && current.Visible && current.Tag is PriceRow) return;

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!gridRow.Visible || !(gridRow.Tag is PriceRow)) continue;
                _grid.CurrentCell = gridRow.Cells[ColName];
                return;
            }

            _grid.ClearSelection();
        }

        private void ApplySearch()
        {
            string query = _find == null ? "" : _find.Text.Trim();
            bool filtering = query.Length > 0;

            // какие разделы подходят под поиск
            HashSet<string> matching = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                PriceRow row = gridRow.Tag as PriceRow;
                if (row == null) continue;
                if (!filtering || Matches(row, query)) matching.Add(row.Group);
            }

            _grid.SuspendLayout();
            try
            {
                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    DataGridViewRow gridRow = _grid.Rows[i];
                    PriceRow row = gridRow.Tag as PriceRow;

                    // строка-заголовок раздела: показываем всегда, чтобы раздел
                    // можно было снова развернуть; знак подсказывает состояние
                    if (row == null)
                    {
                        string group = GroupHeaderAt(gridRow, i);
                        if (group == null) { gridRow.Visible = false; continue; }

                        bool collapsed = _collapsed.Contains(group);
                        gridRow.Visible = !filtering || matching.Contains(group);
                        gridRow.Cells[ColToggle].Value = collapsed ? "+" : "\u2013";
                        gridRow.Cells[ColToggle].ToolTipText = collapsed
                            ? "Развернуть раздел"
                            : "Свернуть раздел";
                        continue;
                    }

                    bool inCollapsed = _collapsed.Contains(row.Group);
                    bool visible = filtering
                        ? matching.Contains(row.Group) && !inCollapsed
                        : !inCollapsed;
                    gridRow.Visible = visible;
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }

            _grid.Invalidate();
        }

        /// <summary>
        /// Название раздела для строки-заголовка идёт от первой позиции под ней:
        /// сами позиции порядок не меняют, поэтому ищем ближайшую следующую.
        /// </summary>
        private string GroupHeaderAt(DataGridViewRow headerRow, int index)
        {
            for (int i = index + 1; i < _grid.Rows.Count; i++)
            {
                PriceRow next = _grid.Rows[i].Tag as PriceRow;
                if (next != null) return next.Group;
            }
            for (int i = index - 1; i >= 0; i--)
            {
                PriceRow previous = _grid.Rows[i].Tag as PriceRow;
                if (previous != null) return previous.Group;
            }
            return null;
        }

        private static bool Matches(PriceRow row, string query)
        {
            return row.Group.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.Unit.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>Свернуть или развернуть группу по щелчку на её названии.</summary>
        private void ToggleGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return;

            // Строки, которые сейчас скрываются, не должны быть текущими: DataGridView
            // не разрешает делать текущую ячейку невидимой и падает с исключением.
            DataGridViewRow current = _grid.CurrentRow;
            if (current != null)
            {
                string currentGroup = GroupOfRow(current.Index);
                if (string.Equals(currentGroup, group, StringComparison.CurrentCultureIgnoreCase))
                    _grid.CurrentCell = null;
            }

            if (_collapsed.Contains(group)) _collapsed.Remove(group);
            else _collapsed.Add(group);

            ApplySearch();

            // курсор мог остаться на только что скрытой строке — уводим его на видимую
            if (_grid.CurrentCell == null || !_grid.CurrentRow.Visible) SyncCurrentRow();

            SetInfo();
        }

        private void MarkDirty(bool dirty)
        {
            _dirty = dirty;
            if (_btnSave != null)
                _btnSave.Text = dirty ? "Сохранить *" : "Сохранить";
            SetInfo();
        }

        private void SetInfo()
        {
            if (_info == null) return;

            int collapsedGroups = 0;
            foreach (string group in _collapsed)
            {
                foreach (PriceRow row in _rows)
                {
                    if (!string.Equals(row.Group, group, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    collapsedGroups++;
                    break;
                }
            }

            int groups = 0;
            string last = null;
            foreach (PriceRow row in _rows)
            {
                if (!string.Equals(last, row.Group, StringComparison.CurrentCultureIgnoreCase))
                {
                    last = row.Group;
                    groups++;
                }
            }

            string text = "позиций: " + _rows.Count + "   •   групп: " + groups;
            if (collapsedGroups > 0) text += "   •   свёрнуто разделов: " + collapsedGroups;
            if (_dirty) text += "   •   есть несохранённые изменения";
            _info.Text = text;
        }

        private void SetStatus(string text)
        {
            _statusText.Text = text;
        }

        // -------------------------------------------------- правка списка

        private List<PriceRow> SelectedRows()
        {
            List<PriceRow> selected = new List<PriceRow>();
            foreach (DataGridViewRow gridRow in _grid.SelectedRows)
            {
                PriceRow row = gridRow.Tag as PriceRow;
                if (row != null) selected.Add(row);
            }
            return selected;
        }

        private void AddRows()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            // раздел берём у выделенной позиции, иначе — у последнего раздела списка
            string group = null;
            List<PriceRow> selected = SelectedRows();
            if (selected.Count > 0) group = selected[0].Group;
            else if (_grid.CurrentRow != null)
            {
                PriceRow current = _grid.CurrentRow.Tag as PriceRow;
                if (current != null) group = current.Group;
            }
            if (string.IsNullOrEmpty(group) && _rows.Count > 0)
                group = _rows[_rows.Count - 1].Group;
            if (string.IsNullOrEmpty(group)) group = "Прочее";

            AddPositionToSection(group);

            SetStatus("Добавлена позиция в раздел «" + group +
                      "». Впишите наименование, единицу измерения и цену.");
        }

        /// <summary>Добавляет позицию в конец указанного раздела.</summary>
        private void AddPositionToSection(string group)
        {
            int insertAt = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Group, group, StringComparison.CurrentCultureIgnoreCase))
                    insertAt = i;
            }

            PriceRow created = new PriceRow(group, "Новая услуга", "услуга", 0m);
            if (insertAt < 0) _rows.Add(created);
            else _rows.Insert(insertAt + 1, created);
            _newRows.Add(created);

            RefreshGrid();
            SelectRow(created);

            DataGridViewRow createdRow = FindGridRow(created);
            if (createdRow != null && createdRow.Visible)
            {
                _grid.CurrentCell = createdRow.Cells[ColName];
                _grid.BeginEdit(true);
            }

            MarkDirty(true);
        }

        /// <summary>
        /// Создание нового раздела. В таблице появляется строка с названием раздела;
        /// наименование услуги в ней не показывается.
        /// </summary>
        private void AddSection()
        {
            if (_grid.IsCurrentCellInEditMode)
            {
                SetStatus("Сначала завершите правку ячейки (нажмите Enter).");
                return;
            }

            // в автотестах ответ передаётся напрямую, в программе спрашиваем у пользователя
            string name = PendingSectionAnswer;
            PendingSectionAnswer = null;
            if (name == null) name = AskGroupName("");
            if (name == null) return;

            name = name.Trim();
            if (name.Length == 0)
            {
                SetStatus("Название раздела не может быть пустым.");
                return;
            }

            foreach (PriceRow existing in _rows)
            {
                if (!string.Equals(existing.Group, name, StringComparison.CurrentCultureIgnoreCase))
                    continue;

                // такой раздел уже есть — просто добавляем в него позицию
                AddPositionToSection(name);
                SetStatus("Раздел «" + name + "» уже есть — в него добавлена новая позиция.");
                return;
            }

            PriceRow first = new PriceRow(name, "Новая услуга", "услуга", 0m);
            _rows.Add(first);
            _newRows.Add(first);
            _newSections.Add(name);

            RefreshGrid();

            DataGridViewRow created = FindGridRow(first);
            if (created != null && created.Visible)
            {
                _grid.CurrentCell = created.Cells[ColName];
                _grid.BeginEdit(true);
            }

            MakeLastRowAvailable();

            MarkDirty(true);
            SetStatus("Создан раздел «" + name + "». Впишите первую услугу раздела.");
        }

        /// <summary>
        /// После добавления в конец таблицы последняя строка иногда попадает в область
        /// закреплённых строк и перестаёт быть доступной. Перерисовка это снимает.
        /// </summary>
        private void MakeLastRowAvailable()
        {
            if (_grid.Rows.Count == 0) return;
            if (_grid.Rows[_grid.Rows.Count - 1].Visible) return;

            _grid.Refresh();
            Application.DoEvents();
        }

        private void SelectRow(PriceRow row)
        {
            _grid.ClearSelection();
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!ReferenceEquals(gridRow.Tag, row)) continue;

                // сначала переводим курсор (это снимает выделение), затем выделяем строку
                _grid.CurrentCell = gridRow.Cells[ColName];
                gridRow.Selected = true;
                return;
            }
        }

        private DataGridViewRow FindGridRow(PriceRow row)
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
                if (ReferenceEquals(gridRow.Tag, row)) return gridRow;
            return null;
        }

        private void DuplicateRows()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            List<PriceRow> selected = SelectedRows();
            if (selected.Count == 0)
            {
                SetStatus("Сначала выделите позицию, которую нужно продублировать.");
                return;
            }

            int lastIndex = -1;
            List<PriceRow> copies = new List<PriceRow>();
            foreach (PriceRow row in selected)
            {
                PriceRow copy = row.Clone();
                copies.Add(copy);
                _newRows.Add(copy);                 // копия ещё не сохранена — тоже подсветим
                lastIndex = Math.Max(lastIndex, _rows.IndexOf(row));
            }

            _rows.InsertRange(lastIndex + 1, copies);
            RefreshGrid();
            if (copies.Count > 0) SelectRow(copies[copies.Count - 1]);
            MarkDirty(true);
            SetStatus("Продублировано позиций: " + copies.Count);
        }

        private void DeleteRows()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            List<PriceRow> selected = SelectedRows();
            if (selected.Count == 0)
            {
                SetStatus("Сначала выделите позиции для удаления.");
                return;
            }

            string question = selected.Count == 1
                ? "Удалить позицию «" + selected[0].Name + "»?"
                : "Удалить выделенные позиции (" + selected.Count + " шт.)?";

            if (MessageBox.Show(this, question, "Удаление позиций",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (PriceRow row in selected) _rows.Remove(row);
            RefreshGrid();
            MarkDirty(true);
            SetStatus("Удалено позиций: " + selected.Count);
        }

        /// <summary>Удаление выделенных позиций без запроса подтверждения.</summary>
        public bool DeleteRowsConfirmed()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            List<PriceRow> selected = SelectedRows();
            if (selected.Count == 0) return false;

            foreach (PriceRow row in selected) _rows.Remove(row);
            RefreshGrid();
            MarkDirty(true);
            SetStatus("Удалено позиций: " + selected.Count);
            return true;
        }

        private void MoveRows(int direction)
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            List<PriceRow> selected = SelectedRows();
            if (selected.Count == 0)
            {
                SetStatus("Сначала выделите позиции для перемещения.");
                return;
            }

            // при движении вверх идём от начала списка, вниз — от конца
            selected.Sort(delegate(PriceRow a, PriceRow b)
            {
                return direction < 0 ? _rows.IndexOf(a).CompareTo(_rows.IndexOf(b))
                                     : _rows.IndexOf(b).CompareTo(_rows.IndexOf(a));
            });

            int moved = 0;
            foreach (PriceRow row in selected)
            {
                int index = _rows.IndexOf(row);
                int target = index + direction;
                if (index < 0 || target < 0 || target >= _rows.Count) continue;

                _rows.RemoveAt(index);
                _rows.Insert(target, row);
                moved++;
            }

            if (moved == 0)
            {
                SetStatus("Дальше двигать некуда.");
                return;
            }

            RefreshGrid();
            _grid.ClearSelection();
            foreach (PriceRow row in selected)
            {
                foreach (DataGridViewRow gridRow in _grid.Rows)
                    if (ReferenceEquals(gridRow.Tag, row)) gridRow.Selected = true;
            }
            MarkDirty(true);
        }

        /// <summary>
        /// Переименование всего раздела: новое название получают все позиции,
        /// у которых раздел совпадает с выделенной строкой.
        /// </summary>
        private void RenameGroup()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            PriceRow current = null;
            List<PriceRow> selected = SelectedRows();
            if (selected.Count > 0) current = selected[0];
            else if (_grid.CurrentRow != null) current = _grid.CurrentRow.Tag as PriceRow;

            if (current == null)
            {
                SetStatus("Выделите любую позицию раздела, который нужно переименовать.");
                return;
            }

            string oldName = current.Group;
            string answer = AskGroupName(oldName);
            if (answer == null) return;

            RenameGroupTo(oldName, answer);
        }

        /// <summary>Переименование раздела во всех его позициях.</summary>
        public bool RenameGroupTo(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || newName == null) return false;

            newName = newName.Trim();
            if (newName.Length == 0)
            {
                SetStatus("Название раздела не может быть пустым.");
                return false;
            }
            if (string.Equals(newName, oldName, StringComparison.CurrentCulture))
            {
                SetStatus("Название раздела не изменилось.");
                return false;
            }

            int count = 0;
            foreach (PriceRow row in _rows)
            {
                if (!string.Equals(row.Group, oldName, StringComparison.CurrentCultureIgnoreCase))
                    continue;
                row.Group = newName;
                count++;
            }

            if (count == 0)
            {
                SetStatus("Раздел «" + oldName + "» не найден.");
                return false;
            }

            if (_collapsed.Remove(oldName)) _collapsed.Add(newName);

            RefreshGrid();
            MarkDirty(true);
            SetStatus("Раздел «" + oldName + "» переименован в «" + newName +
                      "» — позиций: " + count);
            return true;
        }

        /// <summary>Простой запрос нового названия раздела.</summary>
        private string AskGroupName(string current)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Переименование раздела";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.Font = _baseFont;
                dialog.ClientSize = new Size(440, 132);

                Label caption = new Label();
                caption.Text = "Новое название раздела:";
                caption.AutoSize = true;
                caption.Location = new Point(14, 14);
                dialog.Controls.Add(caption);

                TextBox input = new TextBox();
                input.Text = current;
                input.Location = new Point(16, 38);
                input.Width = 404;
                input.SelectAll();
                dialog.Controls.Add(input);

                Button ok = MakeButton("Переименовать", 140);
                ok.Location = new Point(146, 82);
                ok.DialogResult = DialogResult.OK;
                dialog.Controls.Add(ok);

                Button cancel = MakeButton("Отмена", 110);
                cancel.Location = new Point(294, 82);
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(cancel);

                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (input.Text.Trim().Length > 0) return input.Text;

                    MessageBox.Show(dialog, "Название раздела не может быть пустым.",
                        "Переименование раздела", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    input.Focus();
                    input.SelectAll();
                }
                return null;
            }
        }

        private void GroupByGroup()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            _rows.Sort(delegate(PriceRow a, PriceRow b)
            {
                int byGroup = string.Compare(a.Group, b.Group,
                    StringComparison.CurrentCultureIgnoreCase);
                if (byGroup != 0) return byGroup;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            _collapsed.Clear();
            RefreshGrid();
            MarkDirty(true);
            SetStatus("Позиции сгруппированы по разделам.");
        }

        private void SortByName()
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

            _rows.Sort(delegate(PriceRow a, PriceRow b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            RefreshGrid();
            MarkDirty(true);
            SetStatus("Позиции отсортированы по наименованию.");
        }

        // -------------------------------------------------- события таблицы

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0) return;

            DataGridViewRow gridRow = _grid.Rows[e.RowIndex];
            PriceRow row = gridRow.Tag as PriceRow;
            if (row == null) return;

            switch (e.ColumnIndex)
            {
                case ColGroup:
                    row.Group = Convert.ToString(gridRow.Cells[ColGroup].Value).Trim();
                    break;
                case ColArticle:
                    row.Article = Convert.ToString(gridRow.Cells[ColArticle].Value).Trim();
                    break;
                case ColName:
                    row.Name = Convert.ToString(gridRow.Cells[ColName].Value).Trim();
                    break;
                case ColUnit:
                    row.Unit = Convert.ToString(gridRow.Cells[ColUnit].Value).Trim();
                    if (row.Unit.Length == 0) row.Unit = Uom.List[0];
                    break;
                case ColPrice:
                    decimal price;
                    if (Fmt.TryParseDecimal(Convert.ToString(gridRow.Cells[ColPrice].Value), out price)
                        && price >= 0m)
                    {
                        row.Price = price;
                    }
                    break;
            }

            MarkDirty(true);
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.Value == null) return;

            if (e.ColumnIndex == ColPrice)
            {
                decimal price;
                if (Fmt.TryParseDecimal(Convert.ToString(e.Value), out price))
                {
                    e.Value = Fmt.MoneyPlain(price);
                    e.FormattingApplied = true;
                }
            }
        }

        private void Grid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (e.ColumnIndex != ColPrice) return;

            decimal price;
            if (Fmt.TryParseDecimal(Convert.ToString(e.Value), out price) && price >= 0m)
            {
                e.Value = price;
                e.ParsingApplied = true;
            }
        }

        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != ColPrice) return;
            if (e.FormattedValue == null) return;
            if (string.IsNullOrEmpty(Convert.ToString(e.FormattedValue).Trim())) return;

            decimal price;
            if (Fmt.TryParseDecimal(Convert.ToString(e.FormattedValue), out price) && price >= 0m)
                return;

            MessageBox.Show(this,
                "Цена должна быть неотрицательным числом, например: 1500 или 1500,50",
                "Проверка цены", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        /// <summary>Переключение раздела по щелчку на знаке «–/+» в первой колонке.</summary>
        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColToggle) return;
            ToggleGroup(GroupOfRow(e.RowIndex));
        }

        /// <summary>Раздел строки: у позиции — свой, у заголовка — ближайшей позиции.</summary>
        private string GroupOfRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return null;

            PriceRow row = _grid.Rows[rowIndex].Tag as PriceRow;
            if (row != null) return row.Group;

            return GroupHeaderAt(_grid.Rows[rowIndex], rowIndex);
        }

        private void Grid_RowHeightInfoNeeded(object sender, DataGridViewRowHeightInfoNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;

            string text = Convert.ToString(_grid.Rows[e.RowIndex].Cells[ColName].Value);
            int width = Math.Max(80, _grid.Columns[ColName].Width - 12);
            Size measured = TextRenderer.MeasureText(text, _grid.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak);

            e.Height = Math.Max(30, measured.Height + 12);
            e.MinimumHeight = 30;
        }

        // ------------------------------------------------- проверка и запись

        /// <summary>Проверка списка перед записью. Возвращает число найденных проблем.</summary>
        private int ValidateRows()
        {
            int problems = 0;


            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                foreach (DataGridViewCell cell in gridRow.Cells) cell.ErrorText = "";

                PriceRow row = gridRow.Tag as PriceRow;
                if (row == null) continue;

                row.Group = (row.Group ?? "").Trim();
                row.Article = (row.Article ?? "").Trim();
                row.Name = (row.Name ?? "").Trim();
                row.Unit = Uom.Normalize(row.Unit);

                // Ячейки намеренно не трогаем: источник истины — данные, а ячейка
                // обновляет их при завершении правки. Иначе устаревшая ячейка
                // затирала бы правильное значение.

                if (row.Name.Length == 0)
                {
                    gridRow.Cells[ColName].ErrorText = "Укажите наименование услуги.";
                    problems++;
                }
                if (row.Group.Length == 0)
                {
                    gridRow.Cells[ColGroup].ErrorText = "Укажите раздел (группу).";
                    problems++;
                }
                if (row.Price < 0m)
                {
                    gridRow.Cells[ColPrice].ErrorText = "Цена не может быть отрицательной.";
                    problems++;
                }
            }

            return problems;
        }


        private void SaveChanges()
        {
            // Завершаем правку ячейки: её значение попадёт в данные и пройдёт проверку.
            // Если правку отменила проверка цены, сохранять нечего.
            if (_grid.IsCurrentCellInEditMode && !_grid.EndEdit())
            {
                SetStatus("Правка ячейки не завершена — проверьте введённое значение.");
                return;
            }

            int problems = ValidateRows();
            if (problems > 0)
            {
                MessageBox.Show(this,
                    "Найдено позиций с ошибками: " + problems +
                    "\n\nЗаполните наименование и раздел, проверьте цены — " +
                    "ошибочные ячейки помечены красным значком.",
                    "Проверка прайс-листа", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_rows.Count == 0)
            {
                if (MessageBox.Show(this,
                        "Список позиций пуст. Сохранить пустой прайс-лист?",
                        "Сохранение", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes)
                    return;
            }

            List<ServiceItem> items = new List<ServiceItem>();
            foreach (PriceRow row in _rows)
                items.Add(new ServiceItem(row.Group, row.Article, row.Name, row.Unit, row.Price));


            try
            {
                PriceBook.Save(items);
                SavedItems = items;
                _newRows.Clear();                   // всё записано — подсветка больше не нужна
                _newSections.Clear();
                MarkDirty(false);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Не удалось сохранить прайс-лист:\n" + ex.Message +
                    "\n\nВыгрузить прайс-лист в файл, чтобы не потерять правки?",
                    "Ошибка сохранения", MessageBoxButtons.YesNo, MessageBoxIcon.Error);

                if (answer == DialogResult.Yes) ExportToFile(items);
            }
        }

        /// <summary>Выгрузка прайс-листа в файл TSV — для Excel или резервной копии.</summary>
        private void ExportToFile(IList<ServiceItem> items)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "Выгрузить прайс-лист в файл";
            dialog.Filter = "Прайс-лист (*.tsv)|*.tsv|Все файлы (*.*)|*.*";
            dialog.InitialDirectory = PriceBook.StoreFolder;
            dialog.FileName = "Прайс-лист_" + DateTime.Now.ToString("yyyy-MM-dd") + ".tsv";

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                PriceBook.ExportTsv(dialog.FileName, items);
                SetStatus("Прайс-лист выгружен в файл: " + dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось выгрузить прайс-лист:\n" + ex.Message,
                    "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Выгрузка текущего списка (кнопка «Выгрузить в файл»).</summary>
        private void ExportCurrent()
        {
            if (_grid.IsCurrentCellInEditMode)
            {
                SetStatus("Сначала завершите правку ячейки (нажмите Enter).");
                return;
            }

            List<ServiceItem> items = new List<ServiceItem>();
            foreach (PriceRow row in _rows)
                items.Add(new ServiceItem(row.Group, row.Article, row.Name, row.Unit, row.Price));

            ExportToFile(items);
        }

        /// <summary>Загрузка прайс-листа из файла TSV (замена текущего списка).</summary>
        private void ImportFromFile()
        {
            if (_grid.IsCurrentCellInEditMode)
            {
                SetStatus("Сначала завершите правку ячейки (нажмите Enter).");
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Загрузить прайс-лист из файла";
            dialog.Filter = "Прайс-лист (*.tsv;*.txt)|*.tsv;*.txt|Все файлы (*.*)|*.*";
            dialog.InitialDirectory = PriceBook.StoreFolder;

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            List<ServiceItem> items;
            try
            {
                items = PriceBook.ImportTsv(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось прочитать файл:\n" + ex.Message,
                    "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (items.Count == 0)
            {
                MessageBox.Show(this,
                    "В файле не найдено ни одной позиции. Ожидаемый формат: " +
                    "Раздел, наименование, единица измерения и цена, разделённые знаком табуляции.",
                    "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(this,
                    "Заменить текущий прайс-лист (" + _rows.Count + " поз.) данными из файла (" +
                    items.Count + " поз.)?",
                    "Импорт прайс-листа", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
                return;

            _rows.Clear();
            foreach (ServiceItem item in items)
                _rows.Add(new PriceRow(item.Group, item.Article, item.Name, item.Unit, item.Price));

            _collapsed.Clear();
            _newRows.Clear();
            RefreshGrid();
            MarkDirty(true);
            SetStatus("Загружено из файла позиций: " + items.Count +
                      ". Не забудьте сохранить прайс-лист.");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty && DialogResult != DialogResult.OK)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Есть несохранённые изменения. Сохранить их?",
                    "Закрытие редактора", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes)
                {
                    e.Cancel = true;             // сохранит и закроет сам SaveChanges
                    SaveChanges();
                    return;
                }
            }

            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveChanges();
                return true;
            }
            if (keyData == (Keys.Control | Keys.N))
            {
                AddRows();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D))
            {
                DuplicateRows();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.N))
            {
                AddSection();
                return true;
            }
            if (keyData == (Keys.Control | Keys.R))
            {
                RenameGroup();
                return true;
            }
            if (keyData == Keys.Delete && _grid.Focused)
            {
                DeleteRows();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
