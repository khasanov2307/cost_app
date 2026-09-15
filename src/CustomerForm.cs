// ---------------------------------------------------------------------------
//  Окна справочника заказчиков и склада.
//
//  Заказчики: карточки с именем, телефоном, автомобилем и номером; видно,
//  сколько заявок у заказчика и на какую сумму он оплатил.
//  Склад: движения по позициям, приход, расход и инвентаризация.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Справочник заказчиков.</summary>
    internal sealed class CustomerForm : Form
    {
        private readonly CustomerBook _book;
        private readonly ICustomerStore _store;
        private readonly List<SavedEstimate> _estimates;
        private readonly CashBook _cash;

        private TextBox _find;
        private ListView _list;
        private Label _info;
        private TextBox _name;
        private TextBox _phone;
        private TextBox _car;
        private TextBox _plate;
        private TextBox _note;

        private Customer _current;

        public CustomerForm(CustomerBook book, ICustomerStore store,
                            List<SavedEstimate> estimates, CashBook cash)
        {
            _book = book ?? new CustomerBook();
            _store = store;
            _estimates = estimates ?? new List<SavedEstimate>();
            _cash = cash;

            Text = "Заказчики — Расчет заявки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 520);
            ClientSize = new Size(1020, 580);
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
            header.Text = "Заказчики";
            Controls.Add(header);

            Label findLabel = new Label();
            findLabel.Text = "Поиск:";
            findLabel.AutoSize = true;
            findLabel.Location = new Point(16, 46);
            Controls.Add(findLabel);

            _find = new TextBox();
            _find.Location = new Point(80, 43);
            _find.Width = 300;
            _find.TextChanged += delegate { Fill(); };
            Controls.Add(_find);

            Button add = MakeButton("Добавить", 110);
            add.Location = new Point(392, 42);
            add.Click += delegate { AddCustomer(); };
            Controls.Add(add);

            Button remove = MakeButton("Удалить", 110);
            remove.Location = new Point(510, 42);
            remove.Click += delegate { RemoveCustomer(); };
            Controls.Add(remove);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Location = new Point(16, 78);
            _list.Size = new Size(600, ClientSize.Height - 130);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            _list.Columns.Add("Заказчик", 210);
            _list.Columns.Add("Телефон", 140);
            _list.Columns.Add("Автомобиль", 150);
            _list.Columns.Add("Заявок", 70, HorizontalAlignment.Right);
            _list.SelectedIndexChanged += delegate { ShowCurrent(); };
            Controls.Add(_list);

            // карточка выбранного заказчика
            int left = 632;

            AddLabel("Имя или организация:", left, 78);
            _name = new TextBox();
            _name.Location = new Point(left, 98);
            _name.Width = 356;
            _name.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _name.TextChanged += delegate { MarkChanged(); };
            Controls.Add(_name);

            AddLabel("Телефон:", left, 132);
            _phone = new TextBox();
            _phone.Location = new Point(left, 152);
            _phone.Width = 356;
            _phone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _phone.TextChanged += delegate { MarkChanged(); };
            Controls.Add(_phone);

            AddLabel("Автомобиль:", left, 186);
            _car = new TextBox();
            _car.Location = new Point(left, 206);
            _car.Width = 356;
            _car.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _car.TextChanged += delegate { MarkChanged(); };
            Controls.Add(_car);

            AddLabel("Госномер:", left, 240);
            _plate = new TextBox();
            _plate.Location = new Point(left, 260);
            _plate.Width = 356;
            _plate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _plate.TextChanged += delegate { MarkChanged(); };
            Controls.Add(_plate);

            AddLabel("Примечание:", left, 294);
            _note = new TextBox();
            _note.Location = new Point(left, 314);
            _note.Width = 356;
            _note.Height = 60;
            _note.Multiline = true;
            _note.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _note.TextChanged += delegate { MarkChanged(); };
            Controls.Add(_note);

            Button save = MakeButton("Сохранить карточку", 180);
            save.Location = new Point(left, 386);
            save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            save.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            save.Click += delegate { SaveCard(); };
            Controls.Add(save);

            Button fromEstimate = MakeButton("Взять из заявки", 170);
            fromEstimate.Location = new Point(left + 190, 386);
            fromEstimate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            fromEstimate.Click += delegate { SuggestFromEstimates(); };
            Controls.Add(fromEstimate);

            _info = new Label();
            _info.AutoSize = false;
            _info.Dock = DockStyle.Bottom;
            _info.Height = 30;
            _info.ForeColor = Color.FromArgb(110, 118, 130);
            _info.Padding = new Padding(16, 8, 16, 0);
            Controls.Add(_info);

            Button close = MakeButton("Закрыть", 110);
            close.Location = new Point(ClientSize.Width - 126, ClientSize.Height - 42);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.DialogResult = DialogResult.OK;
            close.Click += delegate { SaveCard(); };
            Controls.Add(close);

            CancelButton = close;
        }

        private void AddLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(label);
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

            foreach (Customer customer in _book.Search(_find.Text))
            {
                ListViewItem row = new ListViewItem(customer.Name);
                row.SubItems.Add(customer.Phone);
                row.SubItems.Add(customer.Car);
                row.SubItems.Add(CustomerBook.CountEstimates(customer, _estimates)
                    .ToString(CultureInfo.InvariantCulture));
                row.Tag = customer;
                _list.Items.Add(row);
            }

            if (_list.Items.Count > 0 && _list.SelectedItems.Count == 0) _list.Items[0].Selected = true;

            _info.Text = "Заказчиков: " + _book.Customers.Count;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            if (_list.SelectedItems.Count == 0)
            {
                _current = null;
                _name.Text = _phone.Text = _car.Text = _plate.Text = _note.Text = "";
                return;
            }

            Customer customer = _list.SelectedItems[0].Tag as Customer;
            if (customer == null) return;

            _current = customer;
            _name.Text = customer.Name;
            _phone.Text = customer.Phone;
            _car.Text = customer.Car;
            _plate.Text = customer.Plate;
            _note.Text = customer.Note;

            int count = CustomerBook.CountEstimates(customer, _estimates);
            decimal paid = CustomerBook.PaidTotal(customer, _estimates, _cash);

            _info.Text = "Заказчиков: " + _book.Customers.Count + "   •   выбран " + customer.Caption +
                         "   •   заявок: " + count + "   •   оплачено: " + Fmt.Money(paid);
        }

        /// <summary>Правка полей карточки: переносим в карточку по мере ввода.</summary>
        private void MarkChanged()
        {
            if (_current == null) return;

            _current.Name = _name.Text.Trim();
            _current.Phone = _phone.Text.Trim();
            _current.Car = _car.Text.Trim();
            _current.Plate = _plate.Text.Trim();
            _current.Note = _note.Text;
        }

        private void SaveCard()
        {
            if (_current == null) return;
            MarkChanged();

            // имя на экране в списке должно обновиться
            if (_list.SelectedItems.Count > 0)
            {
                ListViewItem row = _list.SelectedItems[0];
                row.Text = _current.Name;
                row.SubItems[1].Text = _current.Phone;
                row.SubItems[2].Text = _current.Car;
            }

            Save();
        }

        private void Save()
        {
            try
            {
                if (_store != null) _store.Save(_book);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить заказчиков:\n" + ex.Message,
                    "Заказчики", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddCustomer()
        {
            string name = Ask("Имя или организация нового заказчика:", "");
            if (name == null) return;

            name = name.Trim();
            if (name.Length == 0) return;

            Customer created = _book.Ensure(name, "");
            Save();
            Fill();

            foreach (ListViewItem row in _list.Items)
                if (row.Tag == created) { row.Selected = true; row.EnsureVisible(); break; }

            _phone.Focus();
        }

        private void RemoveCustomer()
        {
            if (_current == null) return;

            int count = CustomerBook.CountEstimates(_current, _estimates);
            string question = "Удалить заказчика «" + _current.Name + "»?";

            if (count > 0)
                question += "\n\nУ него есть заявки: " + count +
                            ". Сами заявки останутся, но связь с карточкой потеряется.";

            if (MessageBox.Show(this, question, "Заказчики",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _book.Customers.Remove(_current);
            _current = null;
            Save();
            Fill();
        }

        /// <summary>Подсказка: заказчики из уже сохранённых заявок.</summary>
        private void SuggestFromEstimates()
        {
            List<string> names = new List<string>();

            foreach (SavedEstimate estimate in _estimates)
            {
                if (estimate.Customer.Length == 0) continue;
                if (_book.Match(estimate.Customer, estimate.CustomerPhone) != null) continue;
                if (names.Contains(estimate.Customer)) continue;

                names.Add(estimate.Customer);
            }

            if (names.Count == 0)
            {
                MessageBox.Show(this, "Все заказчики из заявок уже есть в справочнике.",
                    "Заказчики", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (PickForm pick = new PickForm("Заказчики из заявок", names))
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;

                Customer created = _book.Ensure(pick.Selected, "");
                Save();
                Fill();

                foreach (ListViewItem row in _list.Items)
                    if (row.Tag == created) { row.Selected = true; row.EnsureVisible(); break; }
            }
        }

        private string Ask(string question, string value)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Заказчики";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(440, 130);
                dialog.Font = Font;

                Label label = new Label();
                label.Text = question;
                label.AutoSize = true;
                label.Location = new Point(16, 16);
                dialog.Controls.Add(label);

                TextBox box = new TextBox();
                box.Location = new Point(16, 44);
                box.Width = 408;
                box.Text = value;
                dialog.Controls.Add(box);

                Button ok = MakeButton("Сохранить", 130);
                ok.Location = new Point(dialog.ClientSize.Width - 130 - 120, 84);
                ok.DialogResult = DialogResult.OK;
                dialog.Controls.Add(ok);

                Button cancel = MakeButton("Отмена", 110);
                cancel.Location = new Point(dialog.ClientSize.Width - 110 - 16, 84);
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(cancel);

                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                box.SelectAll();

                return dialog.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }
    }

    /// <summary>Простой выбор одной строки из списка.</summary>
    internal sealed class PickForm : Form
    {
        private readonly ListBox _list;

        public string Selected { get; private set; }

        public PickForm(string title, List<string> items)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 340);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            _list = new ListBox();
            _list.Location = new Point(16, 16);
            _list.Size = new Size(428, 270);
            _list.DoubleClick += delegate { Accept(); };
            foreach (string item in items) _list.Items.Add(item);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            Controls.Add(_list);

            Button ok = new Button();
            ok.Text = "Выбрать";
            ok.Width = 130;
            ok.Height = 28;
            ok.FlatStyle = FlatStyle.System;
            ok.Location = new Point(ClientSize.Width - 130 - 120, 296);
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.Width = 110;
            cancel.Height = 28;
            cancel.FlatStyle = FlatStyle.System;
            cancel.Location = new Point(ClientSize.Width - 110 - 16, 296);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            if (_list.SelectedItem == null) return;

            Selected = Convert.ToString(_list.SelectedItem);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
