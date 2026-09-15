// ---------------------------------------------------------------------------
//  Склад: движения по позициям каталога, приход, расход и инвентаризация.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed class WarehouseForm : Form
    {
        private readonly Warehouse _warehouse;
        private readonly IWarehouseStore _store;
        private readonly List<ServiceItem> _catalog;

        private TextBox _find;
        private ListView _list;
        private Label _info;
        private CheckBox _lowOnly;

        public WarehouseForm(Warehouse warehouse, IWarehouseStore store, List<ServiceItem> catalog)
        {
            _warehouse = warehouse ?? new Warehouse();
            _store = store;
            _catalog = catalog ?? new List<ServiceItem>();

            Text = "Склад — Расчет заявки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 480);
            ClientSize = new Size(940, 560);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Build();
            Fill();
        }

        private void Build()
        {
            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(ClientSize.Width - 32, 22);
            header.Location = new Point(16, 12);
            header.Font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = "Склад: остатки и движения";
            Controls.Add(header);

            Label findLabel = new Label();
            findLabel.Text = "Поиск:";
            findLabel.AutoSize = true;
            findLabel.Location = new Point(16, 46);
            Controls.Add(findLabel);

            _find = new TextBox();
            _find.Location = new Point(80, 43);
            _find.Width = 280;
            _find.TextChanged += delegate { Fill(); };
            Controls.Add(_find);

            _lowOnly = new CheckBox();
            _lowOnly.Text = "только ниже минимума";
            _lowOnly.AutoSize = true;
            _lowOnly.Location = new Point(372, 46);
            _lowOnly.CheckedChanged += delegate { Fill(); };
            Controls.Add(_lowOnly);

            Button income = MakeButton("Приход", 110);
            income.Location = new Point(560, 42);
            income.Click += delegate { AddMove(true); };
            Controls.Add(income);

            Button expense = MakeButton("Расход", 110);
            expense.Location = new Point(678, 42);
            expense.Click += delegate { AddMove(false); };
            Controls.Add(expense);

            Button adjust = MakeButton("Инвентаризация", 150);
            adjust.Location = new Point(796, 42);
            adjust.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            adjust.Click += delegate { Adjust(); };
            Controls.Add(adjust);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Location = new Point(16, 78);
            _list.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 170);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.Columns.Add("Позиция", 300);
            _list.Columns.Add("Артикул", 100);
            _list.Columns.Add("Единица", 80);
            _list.Columns.Add("Остаток", 100, HorizontalAlignment.Right);
            _list.Columns.Add("Минимум", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Закупка, ₽", 100, HorizontalAlignment.Right);
            _list.Columns.Add("Продажа, ₽", 100, HorizontalAlignment.Right);
            _list.DoubleClick += delegate { ShowMoves(); };
            _list.SelectedIndexChanged += delegate { UpdateInfo(); };
            Controls.Add(_list);

            Button moves = MakeButton("Движения по позиции", 190);
            moves.Location = new Point(16, ClientSize.Height - 42);
            moves.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            moves.Click += delegate { ShowMoves(); };
            Controls.Add(moves);

            Button refresh = MakeButton("Обновить", 120);
            refresh.Location = new Point(214, ClientSize.Height - 42);
            refresh.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            refresh.Click += delegate { Fill(); };
            Controls.Add(refresh);

            _info = new Label();
            _info.AutoSize = false;
            _info.Size = new Size(ClientSize.Width - 32, 22);
            _info.Location = new Point(16, ClientSize.Height - 72);
            _info.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            _info.ForeColor = Color.FromArgb(110, 118, 130);
            Controls.Add(_info);

            Button close = MakeButton("Закрыть", 110);
            close.Location = new Point(ClientSize.Width - 126, ClientSize.Height - 42);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);

            CancelButton = close;
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

        private void Fill()
        {
            _list.Items.Clear();
            string query = _find.Text.Trim();
            List<string> low = _warehouse.BelowMinimum(_catalog);

            foreach (ServiceItem item in _catalog)
            {
                decimal stock = _warehouse.Stock(item.Article, item.Name);
                bool belowMinimum = item.MinStock > 0m && stock < item.MinStock;

                if (_lowOnly.Checked && !belowMinimum) continue;

                if (query.Length > 0 &&
                    item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    (item.Article ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;

                ListViewItem row = new ListViewItem(item.Name);
                row.SubItems.Add(item.Article);
                row.SubItems.Add(item.Unit);
                row.SubItems.Add(Fmt.Qty(stock));
                row.SubItems.Add(item.MinStock > 0m ? Fmt.Qty(item.MinStock) : "");
                row.SubItems.Add(item.Cost > 0m ? Fmt.Money(item.Cost) : "");
                row.SubItems.Add(Fmt.Money(item.Price));
                row.Tag = item;

                if (belowMinimum) row.ForeColor = Color.FromArgb(170, 60, 40);

                _list.Items.Add(row);
            }

            if (_list.Items.Count > 0) _list.Items[0].Selected = true;

            UpdateInfo();
        }

        private ServiceItem Current()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as ServiceItem;
        }

        private void UpdateInfo()
        {
            decimal stockValue = 0m;
            int zero = 0;
            List<string> low = _warehouse.BelowMinimum(_catalog);

            foreach (ServiceItem item in _catalog)
            {
                decimal stock = _warehouse.Stock(item.Article, item.Name);
                decimal cost = item.Cost > 0m ? item.Cost : _warehouse.AverageCost(item.Article, item.Name);

                if (stock > 0m) stockValue += stock * cost;
                if (stock <= 0m) zero++;
            }

            _info.Text = "Позиций каталога: " + _catalog.Count +
                         "   •   движений: " + _warehouse.Moves.Count +
                         "   •   склад на сумму: " + Fmt.Money(stockValue) +
                         "   •   без остатка: " + zero +
                         "   •   ниже минимума: " + low.Count;
        }

        private void AddMove(bool income)
        {
            ServiceItem item = Current();

            if (item == null)
            {
                MessageBox.Show(this, "Выберите позицию в списке.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            decimal stock = _warehouse.Stock(item.Article, item.Name);
            decimal cost = item.Cost > 0m ? item.Cost : _warehouse.AverageCost(item.Article, item.Name);

            using (StockMoveForm dialog = new StockMoveForm(item.Name, item.Unit, income, stock, cost))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                if (income)
                    _warehouse.Income(item.Article, item.Name, dialog.Quantity, dialog.Cost, dialog.Note);
                else
                    _warehouse.Expense(item.Article, item.Name, dialog.Quantity, dialog.Cost, dialog.Note);

                Save();
                Fill();
            }
        }

        private void Adjust()
        {
            ServiceItem item = Current();

            if (item == null)
            {
                MessageBox.Show(this, "Выберите позицию в списке.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            decimal stock = _warehouse.Stock(item.Article, item.Name);
            decimal cost = item.Cost > 0m ? item.Cost : _warehouse.AverageCost(item.Article, item.Name);

            using (StockMoveForm dialog = new StockMoveForm(item.Name, item.Unit, stock, cost))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _warehouse.Adjust(item.Article, item.Name, dialog.Quantity, dialog.Cost, dialog.Note);
                Save();
                Fill();
            }
        }

        private void ShowMoves()
        {
            ServiceItem item = Current();

            if (item == null)
            {
                MessageBox.Show(this, "Выберите позицию в списке.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<StockMove> moves = _warehouse.MovesFor(item.Article, item.Name);

            using (StockListForm dialog = new StockListForm(item.Name, moves,
                                                            _warehouse.Stock(item.Article, item.Name)))
            {
                dialog.ShowDialog(this);
            }
        }

        private void Save()
        {
            try
            {
                if (_store != null) _store.Save(_warehouse);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить склад:\n" + ex.Message,
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>Приход, расход или инвентаризация по позиции.</summary>
    internal sealed class StockMoveForm : Form
    {
        private readonly bool _adjust;
        private TextBox _quantity;
        private TextBox _cost;
        private TextBox _note;
        private Label _summary;

        public decimal Quantity { get; private set; }
        public decimal Cost { get; private set; }
        public string Note { get; private set; }

        /// <summary>Приход или расход.</summary>
        public StockMoveForm(string name, string unit, bool income, decimal stock, decimal cost)
        {
            _adjust = false;
            Build(income ? "Приход на склад" : "Расход со склада",
                  name + "   •   сейчас на складе: " + Fmt.Qty(stock) + " " + unit,
                  income, stock, cost);

            _quantity.Text = income ? "1" : "1";
            _cost.Text = Fmt.MoneyPlain(cost);
        }

        /// <summary>Инвентаризация: указывается фактический остаток.</summary>
        public StockMoveForm(string name, string unit, decimal stock, decimal cost)
        {
            _adjust = true;
            Build("Инвентаризация",
                  name + "   •   учётный остаток: " + Fmt.Qty(stock) + " " + unit,
                  true, stock, cost);

            _quantity.Text = Fmt.Qty(stock);
            _cost.Text = Fmt.MoneyPlain(cost);
        }

        private void Build(string title, string subtitle, bool income, decimal stock, decimal cost)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(480, 260);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(448, 42);
            header.Location = new Point(16, 12);
            header.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = title + Environment.NewLine + subtitle;
            Controls.Add(header);

            Label quantityLabel = new Label();
            quantityLabel.Text = _adjust ? "Фактический остаток:" : "Количество:";
            quantityLabel.AutoSize = true;
            quantityLabel.Location = new Point(16, 70);
            Controls.Add(quantityLabel);

            _quantity = new TextBox();
            _quantity.Location = new Point(200, 67);
            _quantity.Width = 120;
            _quantity.TextAlign = HorizontalAlignment.Right;
            _quantity.TextChanged += delegate { UpdateSummary(stock); };
            Controls.Add(_quantity);

            Label costLabel = new Label();
            costLabel.Text = "Цена за единицу, ₽:";
            costLabel.AutoSize = true;
            costLabel.Location = new Point(16, 104);
            Controls.Add(costLabel);

            _cost = new TextBox();
            _cost.Location = new Point(200, 101);
            _cost.Width = 120;
            _cost.TextAlign = HorizontalAlignment.Right;
            Controls.Add(_cost);

            Label noteLabel = new Label();
            noteLabel.Text = "Примечание:";
            noteLabel.AutoSize = true;
            noteLabel.Location = new Point(16, 138);
            Controls.Add(noteLabel);

            _note = new TextBox();
            _note.Location = new Point(200, 135);
            _note.Width = 264;
            Controls.Add(_note);

            _summary = new Label();
            _summary.AutoSize = false;
            _summary.Size = new Size(448, 40);
            _summary.Location = new Point(16, 168);
            _summary.ForeColor = Color.FromArgb(110, 118, 130);
            Controls.Add(_summary);

            Button ok = new Button();
            ok.Text = "Записать";
            ok.Width = 140;
            ok.Height = 28;
            ok.FlatStyle = FlatStyle.System;
            ok.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            ok.Location = new Point(ClientSize.Width - 140 - 120, ClientSize.Height - 44);
            ok.Click += delegate { Accept(stock); };
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

            UpdateSummary(stock);
        }

        private void UpdateSummary(decimal stock)
        {
            decimal quantity;

            if (!Fmt.TryParseDecimal(_quantity.Text, out quantity))
            {
                _summary.Text = _adjust ? "Введите фактический остаток." : "Введите количество.";
                return;
            }

            decimal cost;
            Fmt.TryParseDecimal(_cost.Text, out cost);

            if (_adjust)
            {
                _summary.Text = "Будет записана правка остатка до " + Fmt.Qty(quantity) +
                                " (сейчас " + Fmt.Qty(stock) + ") на сумму " +
                                Fmt.Money(quantity * cost);
                return;
            }

            _summary.Text = "Стоимость: " + Fmt.Money(quantity * cost);
        }

        private void Accept(decimal stock)
        {
            decimal quantity;

            if (!Fmt.TryParseDecimal(_quantity.Text, out quantity) || quantity < 0m)
            {
                MessageBox.Show(this, "Введите количество числом.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_adjust && quantity <= 0m)
            {
                MessageBox.Show(this, "Количество должно быть больше нуля.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_adjust && quantity == stock)
            {
                MessageBox.Show(this, "Остаток не изменился — записывать нечего.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            decimal cost;
            Fmt.TryParseDecimal(_cost.Text, out cost);

            Quantity = quantity;
            Cost = cost;
            Note = _note.Text.Trim();

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Движения по одной позиции.</summary>
    internal sealed class StockListForm : Form
    {
        public StockListForm(string name, List<StockMove> moves, decimal stock)
        {
            Text = "Движения: " + name;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 400);
            ClientSize = new Size(820, 460);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(ClientSize.Width - 32, 22);
            header.Location = new Point(16, 12);
            header.Font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = name + "   •   остаток: " + Fmt.Qty(stock);
            Controls.Add(header);

            ListView list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.Location = new Point(16, 44);
            list.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 100);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            list.Columns.Add("Дата", 140);
            list.Columns.Add("Движение", 90);
            list.Columns.Add("Количество", 100, HorizontalAlignment.Right);
            list.Columns.Add("Цена, ₽", 100, HorizontalAlignment.Right);
            list.Columns.Add("Сумма, ₽", 110, HorizontalAlignment.Right);
            list.Columns.Add("Заявка", 90);
            list.Columns.Add("Примечание", 180);
            Controls.Add(list);

            foreach (StockMove move in moves)
            {
                ListViewItem row = new ListViewItem(move.Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));
                row.SubItems.Add(move.IsIncome ? "приход" : "расход");
                row.SubItems.Add(Fmt.Qty(move.Quantity));
                row.SubItems.Add(Fmt.Money(move.Cost));
                row.SubItems.Add(Fmt.Money(move.Amount));
                row.SubItems.Add(move.Number);
                row.SubItems.Add(move.Note);

                if (!move.IsIncome) row.ForeColor = Color.FromArgb(150, 70, 40);

                list.Items.Add(row);
            }

            Button close = new Button();
            close.Text = "Закрыть";
            close.Width = 110;
            close.Height = 28;
            close.FlatStyle = FlatStyle.System;
            close.Location = new Point(ClientSize.Width - 126, ClientSize.Height - 42);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);

            CancelButton = close;
        }
    }
}
