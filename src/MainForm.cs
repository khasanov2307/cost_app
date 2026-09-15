// ---------------------------------------------------------------------------
//  Главная форма: «Калькулятор стоимости услуг».
//
//  Справочник услуг берётся из прайс-листа (файл Цены_услуг.tsv рядом с
//  программой). Нужные позиции отмечаются галочками, количество при
//  необходимости правится, итоговая сумма пересчитывается автоматически.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed partial class MainForm : Form
    {
        // ------------------------------------------------------------- поля

        private readonly List<ServiceItem> _catalog = new List<ServiceItem>();
        private readonly List<EstimateRow> _rows = new List<EstimateRow>();
        private Button _btnOnlySelected;         // показ только отмеченных позиций
        private bool _onlySelected;              // показывать только отмеченные позиции
        private readonly HashSet<string> _collapsed =
            new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        private bool _loading;          // защита от событий при заполнении таблицы
        private bool _filtering;        // включён ли поиск в данный момент

        private DataGridView _grid;
        private TextBox _find;
        private Label _totalLabel;
        private Label _countLabel;
        private ToolStripStatusLabel _statusText;
        private Button _btnGenerate;

        private DataGridViewTextBoxColumn _colName;
        private DataGridViewCheckBoxColumn _colCheck;

        private const int ColCheck = 0;
        private const int ColName = 1;
        private const int ColUnit = 2;
        private const int ColQty = 4;
        private const int ColSum = 5;
        private const int ColPrice = 3;          // цена из прайса (только чтение)

        private Font _baseFont;
        private Font _boldFont;
        private Font _totalFont;
        private Font _monoFont;

        private static readonly Color HeaderBack = Color.FromArgb(232, 236, 242);
        private static readonly Color HeaderFore = Color.FromArgb(28, 40, 60);
        private static readonly Color PickedBack = Color.FromArgb(226, 240, 253);
        private static readonly Color PickedFore = Color.FromArgb(12, 38, 68);
        private static readonly Color Splitter = Color.FromArgb(198, 206, 218);

        // ------------------------------------------------------ конструктор

        public MainForm()
        {
            _baseFont = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            _boldFont = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            _totalFont = new Font("Segoe UI", 13.5f, FontStyle.Bold, GraphicsUnit.Point);
            _monoFont = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            _settings = AppSettings.Load();
            _fields.Number = _settings.Number;
            _fields.Customer = _settings.Customer;
            _fields.Discount = _settings.Discount;
            _dark = _settings.IsDark;
            _templates = TemplateStore.Load();

            BuildInterface();
            LoadCatalog(true);

            RestoreFormState();
            ApplyThemeColors();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_baseFont != null) _baseFont.Dispose();
                if (_boldFont != null) _boldFont.Dispose();
                if (_totalFont != null) _totalFont.Dispose();
                if (_monoFont != null) _monoFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------- интерфейс

        /// <summary>Чтение прайс-листа из выбранного хранилища: файлы или база.</summary>
        private static List<ServiceItem> LoadPrices(out string error)
        {
            error = null;

            try
            {
                return ConnectionSettings.Store.LoadPrices();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new List<ServiceItem>();
            }
        }

        private void BuildInterface()
        {
            Text = "Расчет заявки";
            ApplyAppIcon();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 620);
            ClientSize = new Size(1120, 740);
            Font = _baseFont;

            // --- нижняя панель с итогом и кнопками (докbуем первой) ---
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 200;
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Splitter))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            _totalLabel = new Label();
            _totalLabel.AutoSize = true;
            _totalLabel.Font = _totalFont;
            _totalLabel.ForeColor = Color.FromArgb(20, 70, 130);
            _totalLabel.Location = new Point(14, 156);
            _totalLabel.Text = "ИТОГО: 0,00 \u20BD";
            bottom.Controls.Add(_totalLabel);

            _countLabel = new Label();
            _countLabel.AutoSize = true;
            _countLabel.ForeColor = Color.FromArgb(90, 100, 115);
            _countLabel.Location = new Point(18, 48);
            _countLabel.Text = "Отмечено позиций: 0";
            bottom.Controls.Add(_countLabel);

            _btnGenerate = MakeButton("Сформировать…", 150);
            _btnGenerate.Font = _boldFont;
            _btnGenerate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnGenerate.Click += delegate { GenerateDocument(); };
            bottom.Controls.Add(_btnGenerate);

            Button btnCopy = MakeButton("Копировать", 118);
            btnCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCopy.Click += delegate { CopyToClipboard(); };
            bottom.Controls.Add(btnCopy);

            Button btnReset = MakeButton("Сброс", 92);
            btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnReset.Click += delegate { ResetAll(); };
            bottom.Controls.Add(btnReset);

            // --- верхняя панель: поиск и работа со справочником ---
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 172;

            Label lblFind = new Label();
            lblFind.Text = "Поиск:";
            lblFind.AutoSize = true;
            lblFind.Location = new Point(14, 15);
            top.Controls.Add(lblFind);

            _find = new TextBox();
            _find.Location = new Point(70, 11);
            _find.Width = 250;
            _find.TextChanged += delegate { ApplyFilter(); };
            top.Controls.Add(_find);

            Button btnAll = MakeButton("Отметить всё", 118);
            btnAll.Location = new Point(336, 10);
            btnAll.Click += delegate { SetAllVisible(true); };
            top.Controls.Add(btnAll);

            Button btnNone = MakeButton("Снять отметки", 130);
            btnNone.Location = new Point(462, 10);
            btnNone.Click += delegate { SetAllVisible(false); };
            top.Controls.Add(btnNone);

            Button btnPrice = MakeButton("Каталог номенклатуры…", 122);   // встроенный редактор цен
            btnPrice.Location = new Point(600, 10);
            btnPrice.Click += delegate { OpenPriceEditor(); };
            top.Controls.Add(btnPrice);

            Button btnReload = MakeButton("Обновить", 104);
            btnReload.Location = new Point(730, 10);
            btnReload.Click += delegate { ReloadPricesPreserving(); };
            top.Controls.Add(btnReload);

            // --- вторая строка: реквизиты, скидка, наборы услуг ---
            BuildExtraToolbar(top, bottom);

            // --- таблица услуг ---
            _grid = CreateGrid();
            _grid.Dock = DockStyle.Fill;

            // --- строка состояния ---
            StatusStrip status = new StatusStrip();
            status.SizingGrip = false;
            _statusText = new ToolStripStatusLabel("");
            status.Items.Add(_statusText);

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(top);
            Controls.Add(status);

            _grid.BringToFront();   // таблица в середине: панели сверху и снизу остаются видны

            // Кнопки расставляются по правому краю панели:
            // верхняя строка — предпросмотр, копирование, сброс, формирование;
            // под «Сформировать» — «Оплата»;
            // ещё ниже правый край занимают «Сохранить заявку» и «Открыть заявку».
            _btnPreview = MakeButton("Предпросмотр", 150);
            _btnPreview.Click += delegate { ShowPreview(); };
            bottom.Controls.Add(_btnPreview);

            Button btnPayment = MakeButton("Оплата", 150);
            btnPayment.Click += delegate { RegisterPayment(); };
            bottom.Controls.Add(btnPayment);

            _btnCopyLast = MakeButton("Повторить заявку", 160);
            _btnCopyLast.Click += delegate { CopyLastEstimate(); };
            bottom.Controls.Add(_btnCopyLast);

            Button btnSaveEstimate = MakeButton("Сохранить заявку", 168);
            btnSaveEstimate.Click += delegate { SaveEstimate(); };
            bottom.Controls.Add(btnSaveEstimate);

            Button btnOpenEstimate = MakeButton("Открыть заявку", 158);
            btnOpenEstimate.Click += delegate { OpenEstimate(); };
            bottom.Controls.Add(btnOpenEstimate);

            EventHandler placeActions = delegate
            {
                int right = bottom.ClientSize.Width - 14;

                // верхняя строка
                _btnGenerate.Location = new Point(right - _btnGenerate.Width, 46);
                right -= _btnGenerate.Width + 8;
                _btnPreview.Location = new Point(right - _btnPreview.Width, 46);
                right -= _btnPreview.Width + 8;
                btnCopy.Location = new Point(right - btnCopy.Width, 46);
                right -= btnCopy.Width + 8;
                btnReset.Location = new Point(right - btnReset.Width, 46);

                // «Оплата» — под кнопкой «Сформировать»
                btnPayment.Location = new Point(bottom.ClientSize.Width - 14 - btnPayment.Width, 80);

                // кнопка фильтра — над таблицей, в верхней панели
                PlaceOnlySelectedButton();

                // заявки — справа, на уровне «Данные» и «Темная»
                int estimateRight = bottom.ClientSize.Width - 14;
                btnOpenEstimate.Location = new Point(estimateRight - btnOpenEstimate.Width, 10);
                estimateRight -= btnOpenEstimate.Width + 8;
                estimateRight -= btnSaveEstimate.Width + 8;
                btnSaveEstimate.Location = new Point(estimateRight, 10);

                // «Повторить заявку» — слева от «Сохранить заявку»
                estimateRight -= _btnCopyLast.Width + 8;
                _btnCopyLast.Location = new Point(estimateRight, 10);

                LayoutBottomLabels();
            };
            bottom.Resize += placeActions;
            ApplyHotKeys();
            if (_btnOnlySelected != null && _btnOnlySelected.Parent != null)
                _btnOnlySelected.Parent.Resize += delegate { PlaceOnlySelectedButton(); };
            placeActions(null, EventArgs.Empty);
        }

        private DataGridView CreateGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.RowHeadersVisible = false;
            grid.ColumnHeadersVisible = true;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderFore;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBack;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = HeaderFore;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Splitter;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowTemplate.Height = 30;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.StandardTab = true;
            TryEnableDoubleBuffer(grid);

            _colCheck = new DataGridViewCheckBoxColumn();
            _colCheck.HeaderText = "";                 // колонка галочек — без надписи
            _colCheck.Width = 44;
            _colCheck.FillWeight = 6f;
            _colCheck.SortMode = DataGridViewColumnSortMode.NotSortable;
            _colCheck.Resizable = DataGridViewTriState.False;

            _colName = new DataGridViewTextBoxColumn();
            _colName.HeaderText = "Наименование";
            _colName.FillWeight = 56f;
            _colName.ReadOnly = true;
            _colName.SortMode = DataGridViewColumnSortMode.NotSortable;
            _colName.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            DataGridViewTextBoxColumn colUnit = new DataGridViewTextBoxColumn();
            colUnit.HeaderText = "Единица измерения";
            colUnit.FillWeight = 15f;
            colUnit.ReadOnly = true;
            colUnit.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colPrice = new DataGridViewTextBoxColumn();
            colPrice.HeaderText = "Цена";
            colPrice.FillWeight = 14f;
            colPrice.ReadOnly = true;
            colPrice.SortMode = DataGridViewColumnSortMode.NotSortable;
            colPrice.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            DataGridViewTextBoxColumn colQty = new DataGridViewTextBoxColumn();
            colQty.HeaderText = "Количество";
            colQty.FillWeight = 13f;
            colQty.SortMode = DataGridViewColumnSortMode.NotSortable;
            colQty.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colQty.DefaultCellStyle.BackColor = Color.FromArgb(255, 253, 235);

            DataGridViewTextBoxColumn colSum = new DataGridViewTextBoxColumn();
            colSum.HeaderText = "Всего";
            colSum.FillWeight = 18f;
            colSum.ReadOnly = true;
            colSum.SortMode = DataGridViewColumnSortMode.NotSortable;
            colSum.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            // цена для этой заявки — колонка добавляется последней,
            // чтобы не менять порядок уже существующих
            _colPrice = new DataGridViewTextBoxColumn();
            _colPrice.HeaderText = "Цена в заявке";
            _colPrice.FillWeight = 15f;
            _colPrice.SortMode = DataGridViewColumnSortMode.NotSortable;
            _colPrice.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _colPrice.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 224);

            grid.Columns.AddRange(new DataGridViewColumn[]
                { _colCheck, _colName, colUnit, colPrice, colQty, colSum, _colPrice });

            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellParsing += Grid_CellParsing;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.RowHeightInfoNeeded += Grid_RowHeightInfoNeeded;
            grid.CellPainting += Grid_CellPainting;
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e)
            {
                e.ThrowException = false;
            };

            return grid;
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
                PropertyInfo property = typeof(DataGridView).GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (property != null) property.SetValue(grid, true, null);
            }
            catch { /* не критично */ }
        }
        // --------------------------------------------------- загрузка прайса

        private void LoadCatalog(bool firstRun)
        {
            string error;
            List<ServiceItem> loaded = LoadPrices(out error);

            _catalog.Clear();
            _catalog.AddRange(loaded);

            if (!string.IsNullOrEmpty(error)) SetStatus(error);

            SetStatus("Загружено позиций: " + _catalog.Count +
                      "  •  каталог хранится в приложении");

            _rows.Clear();
            foreach (ServiceItem item in _catalog) _rows.Add(new EstimateRow(item));

            // при запуске программа всегда начинает новую заявку:
            // отметки, количества и цены прошлого раза не восстанавливаются
            if (firstRun) StartNewEstimate(true);

            RebuildGrid();
        }

        /// <summary>Построение таблицы: строки-заголовки групп и позиции.</summary>
        private void RebuildGrid()
        {
            RebuildGrid(null);
        }

        /// <summary>Перестроить таблицу по текущим данным (без перечитывания файла).</summary>
        public void RefreshGrid()
        {
            RebuildGrid(null);
        }

        private void RebuildGrid(List<PreservedRow> preserved)
        {
            _loading = true;
            try
            {
                _grid.Rows.Clear();

                string lastGroup = null;
                foreach (EstimateRow row in _rows)
                {
                    if (!string.Equals(lastGroup, row.Item.Group,
                                       StringComparison.CurrentCultureIgnoreCase))
                    {
                        lastGroup = row.Item.Group;
                        AddGroupRow(lastGroup);
                    }
                    AddItemRow(row);
                }
            }
            finally
            {
                _loading = false;
            }

            if (preserved != null) ApplyPreserved(preserved);

            ApplyFilter();
            Recalculate();
        }

        /// <summary>Отметка и количество позиции — для сохранения при перечитывании цен.</summary>
        private sealed class PreservedRow
        {
            public string Group;
            public string Name;
            public bool Selected;
            public decimal Quantity;
        }

        private List<PreservedRow> SnapshotRows()
        {
            List<PreservedRow> snapshot = new List<PreservedRow>();
            foreach (EstimateRow row in _rows)
                snapshot.Add(new PreservedRow
                {
                    Group = row.Item.Group,
                    Name = row.Item.Name,
                    Selected = row.Selected,
                    Quantity = row.Quantity
                });
            return snapshot;
        }

        /// <summary>
        /// Перечитывает цены из файла, перенося отметки и количество на позиции
        /// с теми же разделом и наименованием. Используется кнопкой «Обновить»
        /// и после закрытия редактора прайс-листа.
        /// </summary>
        public void ReloadPricesPreserving()
        {
            List<PreservedRow> snapshot = SnapshotRows();

            string error;
            List<ServiceItem> loaded = LoadPrices(out error);

            _catalog.Clear();
            _catalog.AddRange(loaded);

            _rows.Clear();
            foreach (ServiceItem item in _catalog) _rows.Add(new EstimateRow(item));

            if (!string.IsNullOrEmpty(error)) SetStatus(error);
            else
                SetStatus("Каталог обновлён: " + _catalog.Count +
                          " поз.  •  отметки сохранены");

            RebuildGrid(snapshot);
        }

        private void ApplyPreserved(List<PreservedRow> preserved)
        {
            _loading = true;
            try
            {
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    EstimateRow row = gridRow.Tag as EstimateRow;
                    if (row == null) continue;

                    foreach (PreservedRow saved in preserved)
                    {
                        if (!string.Equals(saved.Group, row.Item.Group,
                                            StringComparison.CurrentCultureIgnoreCase)) continue;
                        if (!string.Equals(saved.Name, row.Item.Name,
                                            StringComparison.CurrentCultureIgnoreCase)) continue;

                        row.Selected = saved.Selected;
                        row.Quantity = saved.Quantity;
                        if (gridRow.Cells[ColCheck].Value != null)
                            gridRow.Cells[ColCheck].Value = saved.Selected;
                        gridRow.Cells[ColQty].Value = saved.Quantity;
                        gridRow.Cells[ColQty].Tag = saved.Quantity;
                        gridRow.Cells[ColSum].Value = row.Sum;
                        StyleItemRow(gridRow, saved.Selected);
                        break;
                    }
                }
            }
            finally
            {
                _loading = false;
            }
        }

        private void AddGroupRow(string group)
        {
            int index = _grid.Rows.Add(new object[] { null, group, "", null, null, null });
            DataGridViewRow row = _grid.Rows[index];
            row.Tag = group;                       // маркер строки-заголовка группы
            row.Height = 30;
            row.ReadOnly = true;
            row.Resizable = DataGridViewTriState.False;

            DataGridViewCellStyle style = new DataGridViewCellStyle();
            style.BackColor = HeaderBack;
            style.ForeColor = HeaderFore;
            style.Font = _boldFont;
            style.SelectionBackColor = HeaderBack;
            style.SelectionForeColor = HeaderFore;
            style.Padding = new Padding(4, 0, 0, 0);

            for (int i = 0; i < _grid.Columns.Count; i++) row.Cells[i].Style = style;
            row.Cells[ColName].Value = FormatGroupCaption(group);

            DataGridViewCellStyle checkStyle = new DataGridViewCellStyle(style);
            checkStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            row.Cells[ColCheck].Style = checkStyle;
        }

        private void AddItemRow(EstimateRow row)
        {
            int index = _grid.Rows.Add(new object[]
            {
                row.Selected,
                row.Item.Name,
                row.Item.Unit,
                row.Item.Price,
                row.Quantity,
                row.Sum,
                row.Price
            });

            DataGridViewRow gridRow = _grid.Rows[index];
            gridRow.Tag = row;
            gridRow.Cells[ColQty].Tag = row.Quantity;   // последнее корректное значение
            StyleItemRow(gridRow, row.Selected);
        }

        private void StyleItemRow(DataGridViewRow gridRow, bool picked)
        {
            DataGridViewCellStyle style = new DataGridViewCellStyle();
            style.BackColor = picked ? PickedBack : Color.White;
            style.ForeColor = picked ? PickedFore : Color.FromArgb(30, 30, 30);
            style.SelectionBackColor = picked ? Color.FromArgb(198, 224, 250)
                                              : Color.FromArgb(215, 228, 245);
            style.SelectionForeColor = Color.FromArgb(15, 25, 40);
            style.Padding = new Padding(4, 0, 4, 0);

            for (int i = 0; i < gridRow.Cells.Count; i++)
            {
                DataGridViewCellStyle cellStyle = new DataGridViewCellStyle(style);

                if (i == ColCheck)
                    cellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                if (i == ColUnit)
                    cellStyle.ForeColor = picked ? PickedFore : Color.FromArgb(95, 105, 120);
                if (i == ColQty)
                {
                    cellStyle.BackColor = picked ? Color.FromArgb(255, 250, 224)
                                                 : Color.FromArgb(255, 253, 235);
                    cellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                if (i == ColPrice || i == ColSum)
                    cellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                if (i == ColPrice)
                    cellStyle.BackColor = picked ? Color.FromArgb(255, 246, 200) : Color.FromArgb(255, 250, 224);
                if (i == ColSum && picked)
                    cellStyle.Font = _boldFont;

                gridRow.Cells[i].Style = cellStyle;
            }
        }

        private static string FormatGroupCaption(string group)
        {
            return "\u25BE  " + group.ToUpper(Fmt.Ru);
        }

        // ------------------------------------------------------- фильтрация

        private void ApplyFilter()
        {
            string query = _find == null ? "" : _find.Text.Trim();
            _filtering = query.Length > 0;

            int position = 0;
            if (_grid.CurrentCell != null) position = _grid.CurrentCell.RowIndex;

            _grid.SuspendLayout();
            try
            {
                // 1) какие группы содержат подходящие позиции
                HashSet<string> matching =
                    new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    DataGridViewRow gridRow = _grid.Rows[i];
                    if (gridRow.Tag is string) continue;             // заголовок группы

                    if (LimitOk(gridRow) && (!_filtering || Matches(gridRow, query)))
                        matching.Add(((EstimateRow)gridRow.Tag).Item.Group);
                }

                // 2) показать или скрыть строки
                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    DataGridViewRow gridRow = _grid.Rows[i];

                    if (gridRow.Tag is string)
                    {
                        string group = (string)gridRow.Tag;
                        gridRow.Visible = matching.Contains(group);
                        gridRow.Cells[ColName].Value = FormatGroupCaption(group);
                    }
                    else
                    {
                        EstimateRow row = (EstimateRow)gridRow.Tag;
                        gridRow.Visible = matching.Contains(row.Item.Group)
                                       && !_collapsed.Contains(row.Item.Group)
                                       && LimitOk(gridRow);
                    }
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }

            if (position >= 0 && position < _grid.Rows.Count && _grid.Rows[position].Visible)
            {
                _grid.CurrentCell = _grid.Rows[position].Cells[ColName];
            }
            else
            {
                DataGridViewRow first = FirstVisibleRow();
                if (first != null) _grid.CurrentCell = first.Cells[ColName];
                else if (_grid.Rows.Count > 0) _grid.ClearSelection();
            }

            _grid.Invalidate();
        }

        /// <summary>Подходит ли строка под режим «только выбранные».</summary>
        private bool LimitOk(DataGridViewRow gridRow)
        {
            if (!_onlySelected) return true;

            EstimateRow row = gridRow.Tag as EstimateRow;
            return row != null && row.Selected;
        }


        private static bool Matches(DataGridViewRow gridRow, string query)
        {
            string name = Convert.ToString(gridRow.Cells[ColName].Value);
            EstimateRow row = gridRow.Tag as EstimateRow;
            string group = row == null ? "" : row.Item.Group;

            return name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                || group.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private DataGridViewRow FirstVisibleRow()
        {
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Visible) return row;
            return null;
        }

        /// <summary>Кнопка фильтра — в нижней панели, на уровне «Данные» и «Тёмная».</summary>
        private void PlaceOnlySelectedButton()
        {
            if (_btnOnlySelected == null) return;
            if (_btnOnlySelected.Parent == null) return;

            _btnOnlySelected.Location = new Point(246, 10);

            UpdateOnlySelectedButton();
        }

        /// <summary>Вид кнопки: нажата, когда фильтр включён.</summary>
        private void UpdateOnlySelectedButton()
        {
            if (_btnOnlySelected == null) return;

            _btnOnlySelected.FlatStyle = _onlySelected ? FlatStyle.Standard : FlatStyle.System;
            _btnOnlySelected.Font = new Font("Segoe UI", 9.75f,
                _onlySelected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            _btnOnlySelected.Text = _onlySelected
                ? "Показаны только выбранные"
                : "Показать только выбранные";
            _btnOnlySelected.Refresh();
        }


        private void ToggleOnlySelected()
        {
            _onlySelected = !_onlySelected;

            UpdateOnlySelectedButton();
            ApplyFilter();

            int shown = 0;
            foreach (DataGridViewRow gridRow in _grid.Rows)
                if (gridRow.Visible && gridRow.Tag is EstimateRow) shown++;

            if (_onlySelected)
                SetStatus("Показаны только отмеченные позиции: " + shown +
                          ". Повторное нажатие вернёт весь каталог.");
            else
                SetStatus("Показан весь каталог: позиций " + shown + ".");
        }


        /// <summary>Сворачивание и разворачивание группы по щелчку на её заголовке.</summary>
        private void ToggleGroup(string group)
        {
            if (group == null) return;
            if (_collapsed.Contains(group)) _collapsed.Remove(group);
            else _collapsed.Add(group);
            ApplyFilter();
        }

        // --------------------------------------------------- отметки и итоги

        private void SetAllVisible(bool selected)
        {
            _loading = true;
            try
            {
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    EstimateRow row = gridRow.Tag as EstimateRow;
                    if (row == null) continue;                    // заголовок группы
                    if (!gridRow.Visible) continue;               // скрыто поиском или группой

                    row.Selected = selected;
                    gridRow.Cells[ColCheck].Value = selected;
                    gridRow.Cells[ColSum].Value = row.Sum;
                    StyleItemRow(gridRow, selected);
                }
            }
            finally
            {
                _loading = false;
            }

            Recalculate();
            _grid.Invalidate();
        }

        private void ResetAll()
        {
            if (MessageBox.Show(this,
                    "Снять все отметки, вернуть количество 1 и очистить список услуг?",
                    "Сброс",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (EstimateRow row in _rows)
            {
                row.Selected = false;
                row.Quantity = 1m;
            }

            RebuildGrid();
            SetStatus("Отметки сняты. Цены можно изменить кнопкой «Каталог номенклатуры…».");
        }

        private void Recalculate()
        {
            decimal total = 0m;
            decimal units = 0m;
            int picked = 0;

            foreach (EstimateRow row in _rows)
            {
                if (!row.Selected) continue;
                picked++;
                units += row.Quantity;
                total += row.Sum;
            }

            // скидка на всю заявку
            decimal discount = AppSettings.ClampDiscount(_fields.Discount);
            decimal discountAmount = 0m;
            if (discount > 0m && total > 0m)
                discountAmount = Math.Round(total * discount / 100m, 2, MidpointRounding.AwayFromZero);

            decimal payable = total - discountAmount;

            _totalLabel.Text = "ИТОГО: " + Fmt.Money(payable);
            _countLabel.Text = string.Format(Fmt.Ru,
                "Отмечено позиций: {0}   •   всего единиц: {1}",
                picked, Fmt.Qty(units));

            if (_subtotalLabel != null)
            {
                _subtotalLabel.Text = discountAmount > 0m
                    ? string.Format(Fmt.Ru, "Подытог: {0}   •   скидка {1} %: \u2212{2}",
                        Fmt.Money(total), Fmt.Qty(discount), Fmt.Money(discountAmount))
                    : "Скидка не задана";
            }

            _btnGenerate.Enabled = picked > 0;
            if (_btnPreview != null) _btnPreview.Enabled = picked > 0;

            SaveSettings();
            LayoutBottomLabels();
        }

        /// <summary>
        /// Подписи слева в нижней панели: итог, счётчик позиций и подытог со скидкой.
        /// Кнопки занимают правую часть панели, поэтому ширина подписей ограничена.
        /// </summary>
        /// <summary>Значок окна — тот же, что у файла программы.</summary>
        private void ApplyAppIcon()
        {
            try
            {
                using (System.IO.Stream stream = GetType().Assembly
                           .GetManifestResourceStream("KotovCalc.App.ico"))
                {
                    if (stream != null) Icon = new Icon(stream);
                }
            }
            catch { /* значок не критичен: без него окно всё равно работает */ }
        }


        private void LayoutBottomLabels()
        {
            if (_totalLabel == null) return;

            Panel panel = _totalLabel.Parent as Panel;
            int panelWidth = panel != null ? panel.ClientSize.Width : ClientSize.Width;
            int limit = Math.Max(260, panelWidth - 760);

            // итоговая сумма — в правом нижнем углу панели
            Size totalSize = _totalLabel.PreferredSize;
            int totalLimit = Math.Max(200, panelWidth - 40);
            int totalWidth = Math.Min(totalSize.Width + 8, totalLimit);
            _totalLabel.Location = new Point(
                Math.Max(14, panelWidth - totalWidth - 18),
                Math.Max(14, panel.Height - totalSize.Height - 10));
            _totalLabel.AutoSize = false;
            _totalLabel.Size = new Size(totalWidth, totalSize.Height);
            _totalLabel.AutoEllipsis = true;

            // счётчик позиций и подытог со скидкой — в одной строке панели
            int line = Math.Max(14, panel.Height - 30);

            _countLabel.AutoSize = true;
            _countLabel.AutoEllipsis = false;
            _countLabel.Location = new Point(14, line);

            if (_subtotalLabel != null)
            {
                _subtotalLabel.AutoSize = true;
                _subtotalLabel.AutoEllipsis = false;
                _subtotalLabel.Visible = true;

                int subtotalLeft = _countLabel.Right + 24;
                int subtotalWidth = _subtotalLabel.PreferredSize.Width;

                // если места до итоговой суммы мало, переносим подытог ниже
                if (subtotalLeft + subtotalWidth > _totalLabel.Left - 16)
                    _subtotalLabel.Location = new Point(14, line + 20);
                else
                    _subtotalLabel.Location = new Point(subtotalLeft, line);
            }
        }

        private void UpdateRowSum(DataGridViewRow gridRow, EstimateRow row)
        {
            gridRow.Cells[ColSum].Value = row.Sum;
        }

        private void ToggleRow(DataGridViewRow gridRow, EstimateRow row)
        {
            _loading = true;
            try
            {
                gridRow.Cells[ColCheck].Value = row.Selected;
            }
            finally
            {
                _loading = false;
            }

            StyleItemRow(gridRow, row.Selected);
            UpdateRowSum(gridRow, row);
            Recalculate();
            _grid.InvalidateRow(gridRow.Index);
        }
        // --------------------------------------------------- события таблицы

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0) return;

            DataGridViewRow gridRow = _grid.Rows[e.RowIndex];
            EstimateRow row = gridRow.Tag as EstimateRow;
            if (row == null) return;                          // строка-заголовок группы

            if (e.ColumnIndex == ColCheck)
            {
                object value = gridRow.Cells[ColCheck].Value;
                row.Selected = value != null && Convert.ToBoolean(value);
                StyleItemRow(gridRow, row.Selected);
                UpdateRowSum(gridRow, row);
                Recalculate();
                _grid.InvalidateRow(e.RowIndex);
            }
            else if (e.ColumnIndex == ColQty)
            {
                decimal quantity;
                if (!Fmt.TryParseDecimal(Convert.ToString(gridRow.Cells[ColQty].Value), out quantity)
                    || quantity < 0m)
                {
                    _loading = true;
                    try
                    {
                        gridRow.Cells[ColQty].Value = gridRow.Cells[ColQty].Tag;
                    }
                    finally
                    {
                        _loading = false;
                    }

                    SetStatus("Количество должно быть неотрицательным числом, например: 1 или 2,5");
                    return;
                }

                row.Quantity = quantity;
                gridRow.Cells[ColQty].Tag = quantity;
                UpdateRowSum(gridRow, row);
                Recalculate();
            }
            else if (e.ColumnIndex == ColPrice)
            {
                decimal price;
                if (!TryParsePrice(Convert.ToString(gridRow.Cells[ColPrice].Value), out price))
                {
                    _loading = true;
                    try { gridRow.Cells[ColPrice].Value = row.Price; }
                    finally { _loading = false; }

                    SetStatus("Цена должна быть неотрицательным числом, например 1 500 или 1500,50");
                    return;
                }

                // цена, совпадающая с прайсом, не считается изменённой
                row.PriceOverride = price == row.Item.Price ? (decimal?)null : price;
                gridRow.Cells[ColPrice].Value = row.Price;
                UpdateRowSum(gridRow, row);
                Recalculate();
            }
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.Value == null) return;

            if (e.ColumnIndex == ColQty)
            {
                decimal quantity;
                if (Fmt.TryParseDecimal(Convert.ToString(e.Value), out quantity) && quantity == 1m)
                {
                    e.Value = "";                    // единицу не показываем — так нагляднее
                    e.FormattingApplied = true;
                }
                else if (quantity >= 0m)
                {
                    e.Value = Fmt.Qty(quantity);
                    e.FormattingApplied = true;
                }
            }
            else if (e.ColumnIndex == ColPrice || e.ColumnIndex == ColSum || e.ColumnIndex == 6)
            {
                decimal amount;
                if (Fmt.TryParseDecimal(Convert.ToString(e.Value), out amount))
                {
                    e.Value = Fmt.MoneyPlain(amount);
                    e.FormattingApplied = true;
                }
            }
        }

        private void Grid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (e.ColumnIndex == ColPrice)
            {
                decimal price;
                if (TryParsePrice(Convert.ToString(e.Value), out price))
                {
                    e.Value = price;
                    e.ParsingApplied = true;
                }
                return;
            }

            if (e.ColumnIndex != ColQty) return;

            decimal quantity;
            if (Fmt.TryParseDecimal(Convert.ToString(e.Value), out quantity))
            {
                e.Value = quantity;
                e.ParsingApplied = true;
            }
        }

        /// <summary>
        /// У строк-разделов каталога галочки нет: в колонке отметок ничего не рисуем,
        /// остаётся только название раздела. Сворачивание — щелчком по названию.
        /// </summary>
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColCheck) return;
            if (!(_grid.Rows[e.RowIndex].Tag is string)) return;   // не раздел каталога

            e.PaintBackground(e.CellBounds, true);
            e.Handled = true;
        }


        private void Grid_RowHeightInfoNeeded(object sender, DataGridViewRowHeightInfoNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            if (row.Tag is string) { e.Height = 30; e.MinimumHeight = 30; return; }

            string text = Convert.ToString(row.Cells[ColName].Value);
            int width = Math.Max(80, _grid.Columns[ColName].Width - 12);
            Size measured = TextRenderer.MeasureText(text, _grid.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak);

            e.Height = Math.Max(30, measured.Height + 12);
            e.MinimumHeight = 30;
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (!(_grid.Rows[e.RowIndex].Tag is string)) return;   // не заголовок группы

            ToggleGroup((string)_grid.Rows[e.RowIndex].Tag);
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            EstimateRow row = _grid.Rows[e.RowIndex].Tag as EstimateRow;
            if (row == null || e.ColumnIndex == ColCheck || e.ColumnIndex == ColQty) return;

            row.Selected = !row.Selected;               // двойной щелчок = галочка
            ToggleRow(_grid.Rows[e.RowIndex], row);
        }

        // --------------------------------------------- прайс-лист и документы

        /// <summary>Встроенный редактор прайс-листа.</summary>
        private void OpenPriceEditor()
        {
            using (PriceEditorForm editor = new PriceEditorForm())
            {
                editor.ShowDialog(this);

                if (editor.DialogResult == DialogResult.OK)
                {
                    ReloadPricesPreserving();
                    SetStatus("Каталог сохранён внутри программы" +
                              "  •  позиций: " + _catalog.Count);
                }
                else
                {
                    SetStatus("Редактор каталога закрыт без сохранения.");
                }
            }
        }

        /// <summary>Заявка на расчет текстом — для предпросмотра и текстового файла.</summary>
        private string BuildDocumentText()
        {
            return DocumentBuilder.ToText(BuildEstimateDocument());
        }


        private static string Positions(int count)
        {
            int last = count % 100;
            int lastDigit = count % 10;

            if (last >= 11 && last <= 14) return "позиций";
            if (lastDigit == 1) return "позиция";
            if (lastDigit >= 2 && lastDigit <= 4) return "позиции";
            return "позиций";
        }

        private static string Shorten(string text, int max)
        {
            if (text == null) return "";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "\u2026";
        }

        private int CountPicked()
        {
            int picked = 0;
            foreach (EstimateRow row in _rows) if (row.Selected) picked++;
            return picked;
        }

        private void CopyToClipboard()
        {
            if (CountPicked() == 0)
            {
                SetStatus("Сначала отметьте нужные услуги.");
                return;
            }

            try
            {
                Clipboard.SetText(BuildDocumentText());
                SetStatus("Заявка скопирована в буфер обмена — можно вставить в письмо или документ.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось скопировать в буфер обмена:\n" + ex.Message,
                    "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void GenerateDocument()
        {
            if (CountPicked() == 0)
            {
                MessageBox.Show(this, "Не отмечено ни одной услуги.", "Заявка на расчет",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "Сохранить заявку";
            dialog.Filter = "Документ Word (*.docx)|*.docx|Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*";
            dialog.InitialDirectory = PriceBook.StoreFolder;
            dialog.FileName = "Заявка_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".docx";
            dialog.AddExtension = true;

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            bool word = dialog.FilterIndex == 1 ||
                        Path.GetExtension(dialog.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (word)
                {
                    if (!Path.GetExtension(dialog.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                        dialog.FileName = dialog.FileName + ".docx";

                    EstimateDocx.Save(dialog.FileName, BuildEstimateDocument(), _settings.Logo);
                }
                else
                {
                    // UTF-8 с BOM — файл корректно открывают «Блокнот» и Word
                    File.WriteAllText(dialog.FileName, BuildDocumentText(), new UTF8Encoding(true));
                }

                SetStatus("Заявка сохранена: " + dialog.FileName);

                if (MessageBox.Show(this, "Заявка на расчет сохранена.\n\nОткрыть файл?",
                        "Заявка на расчет", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить файл:\n" + ex.Message,
                    "Заявка на расчет", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // -------------------------------------------- сохранение состояния

        private string SessionPath
        {
            get { return Path.Combine(PriceBook.StoreFolder, "session.tsv"); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSession();
            base.OnFormClosing(e);
        }

        private void SaveSession()
        {
            try
            {
                // если ничего не отмечено, файл состояния не трогаем —
                // иначе правка прайс-листа затирала бы прошлый сеанс
                if (CountPicked() == 0) return;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Отметки и количество сохраняются автоматически.");
                foreach (EstimateRow row in _rows)
                {
                    if (!row.Selected && row.Quantity == 1m) continue;
                    sb.Append(row.Selected ? "1" : "0").Append('\t')
                      .Append(row.Item.Group).Append('\t')
                      .Append(row.Item.Name).Append('\t')
                      .Append(row.Quantity.ToString("0.###", CultureInfo.InvariantCulture))
                      .AppendLine();
                }
                File.WriteAllText(SessionPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { /* сохранение состояния не критично */ }
        }

        private void RestoreSession()
        {
            try
            {
                if (!File.Exists(SessionPath)) return;

                Dictionary<string, EstimateRow> index =
                    new Dictionary<string, EstimateRow>(StringComparer.CurrentCultureIgnoreCase);
                foreach (EstimateRow row in _rows)
                    index[row.Item.Group + "\u0001" + row.Item.Name] = row;

                int restored = 0;
                foreach (string line in File.ReadAllLines(SessionPath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 4) continue;

                    EstimateRow row;
                    if (!index.TryGetValue(parts[1].Trim() + "\u0001" + parts[2].Trim(), out row))
                        continue;

                    row.Selected = parts[0].Trim() == "1";
                    decimal quantity;
                    if (Fmt.TryParseDecimal(parts[3], out quantity) && quantity >= 0m)
                        row.Quantity = quantity;
                    restored++;
                }

                if (restored > 0)
                    SetStatus("Восстановлены отметки прошлого сеанса: " + restored + " поз.");
            }
            catch { /* состояние не критично */ }
        }

        /// <summary>Публичная точка сохранения состояния (вызывается при закрытии).</summary>
        public void SaveStateOnClose()
        {
            SaveSession();
        }

        private void SetStatus(string text)
        {
            _statusText.Text = text;
        }
    }
}

