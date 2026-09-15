// ---------------------------------------------------------------------------
//  Окно оплаты заявки и справочник касс.
//
//  В окне оплаты указывается касса, способ оплаты (наличные, безналичные,
//  смешанная) и суммы. Программа показывает остаток к доплате и не даёт
//  записать оплату больше суммы заявки.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Окно фиксации оплаты по заявке.</summary>
    internal sealed class PaymentForm : Form
    {
        private readonly CashBook _cash;
        private readonly decimal _due;
        private readonly string _number;
        private readonly string _customer;

        private ComboBox _kind;
        private ComboBox _deskList;
        private TextBox _deskNew;
        private TextBox _cashBox;
        private TextBox _cashlessBox;
        private TextBox _note;
        private Label _summary;
        private Button _ok;

        /// <summary>Готовая оплата.</summary>
        public Payment Result { get; private set; }

        public PaymentForm(CashBook cash, string number, string customer, decimal due, Payment existing)
        {
            _cash = cash ?? new CashBook();
            _number = number ?? "";
            _customer = customer ?? "";
            _due = due;

            Build();

            if (existing != null)
            {
                _kind.SelectedIndex = (int)existing.Kind;
                _cashBox.Text = existing.Cash > 0m ? Fmt.MoneyPlain(existing.Cash) : "";
                _cashlessBox.Text = existing.Cashless > 0m ? Fmt.MoneyPlain(existing.Cashless) : "";
                _note.Text = existing.Note;

                if (_deskList.Items.Contains(existing.Desk)) _deskList.SelectedItem = existing.Desk;
                else if (existing.Desk.Length > 0) _deskNew.Text = existing.Desk;
            }

            UpdateSummary();
        }

        private void Build()
        {
            Text = "Оплата заявки";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 400);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(488, 42);
            header.Location = new Point(16, 12);
            header.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = "Заявка № " + (_number.Length == 0 ? "без номера" : _number) +
                          (_customer.Length > 0 ? "   " + _customer : "") + Environment.NewLine +
                          "Сумма к оплате: " + Fmt.Money(_due) + " \u20BD";
            Controls.Add(header);

            int top = 66;

            AddLabel("Способ оплаты:", 16, top);
            _kind = new ComboBox();
            _kind.Location = new Point(150, top - 3);
            _kind.Width = 180;
            _kind.DropDownStyle = ComboBoxStyle.DropDownList;
            _kind.Items.AddRange(PaymentKinds.List);
            _kind.SelectedIndex = 0;
            _kind.SelectedIndexChanged += delegate { UpdateSummary(); };
            Controls.Add(_kind);

            top += 36;
            AddLabel("Касса:", 16, top);
            _deskList = new ComboBox();
            _deskList.Location = new Point(150, top - 3);
            _deskList.Width = 180;
            _deskList.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (CashDesk desk in _cash.Desks)
                if (!desk.Archive) _deskList.Items.Add(desk.Name);
            if (_deskList.Items.Count > 0) _deskList.SelectedIndex = 0;
            Controls.Add(_deskList);

            Button addDesk = MakeButton("Новая касса…", 140);
            addDesk.Location = new Point(340, top - 4);
            addDesk.Click += delegate { AddDesk(); };
            Controls.Add(addDesk);

            top += 36;
            AddLabel("Или название новой:", 16, top);
            _deskNew = new TextBox();
            _deskNew.Location = new Point(190, top - 3);
            _deskNew.Width = 140;
            Controls.Add(_deskNew);

            top += 40;
            AddLabel("Наличными, \u20BD:", 16, top);
            _cashBox = new TextBox();
            _cashBox.Location = new Point(190, top - 3);
            _cashBox.Width = 140;
            _cashBox.TextAlign = HorizontalAlignment.Right;
            _cashBox.TextChanged += delegate { UpdateSummary(); };
            Controls.Add(_cashBox);

            top += 32;
            AddLabel("Безналичными, \u20BD:", 16, top);
            _cashlessBox = new TextBox();
            _cashlessBox.Location = new Point(190, top - 3);
            _cashlessBox.Width = 140;
            _cashlessBox.TextAlign = HorizontalAlignment.Right;
            _cashlessBox.TextChanged += delegate { UpdateSummary(); };
            Controls.Add(_cashlessBox);

            top += 32;
            Button fill = MakeButton("Вся сумма наличными", 200);
            fill.Location = new Point(16, top - 4);
            fill.Click += delegate
            {
                _kind.SelectedIndex = (int)PaymentKind.Cash;
                _cashBox.Text = Fmt.MoneyPlain(Remaining());
                _cashlessBox.Text = "";
                UpdateSummary();
            };
            Controls.Add(fill);

            Button fillCashless = MakeButton("Вся сумма безналичными", 210);
            fillCashless.Location = new Point(226, top - 4);
            fillCashless.Click += delegate
            {
                _kind.SelectedIndex = (int)PaymentKind.Cashless;
                _cashlessBox.Text = Fmt.MoneyPlain(Remaining());
                _cashBox.Text = "";
                UpdateSummary();
            };
            Controls.Add(fillCashless);

            top += 40;
            AddLabel("Примечание:", 16, top);
            _note = new TextBox();
            _note.Location = new Point(150, top - 3);
            _note.Width = 330;
            Controls.Add(_note);

            top += 40;
            _summary = new Label();
            _summary.AutoSize = false;
            _summary.Size = new Size(488, 46);
            _summary.Location = new Point(16, top);
            _summary.Font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
            Controls.Add(_summary);

            _ok = MakeButton("Записать оплату", 170);
            _ok.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            _ok.Location = new Point(ClientSize.Width - 170 - 120, ClientSize.Height - 44);
            _ok.Click += delegate { Accept(); };
            Controls.Add(_ok);

            Button cancel = MakeButton("Отмена", 110);
            cancel.Location = new Point(ClientSize.Width - 110 - 16, ClientSize.Height - 44);
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
            label.Location = new Point(x, y);
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

        /// <summary>Сколько ещё можно внести.</summary>
        private decimal Remaining()
        {
            decimal left = _due - Amount(_cashBox.Text) - Amount(_cashlessBox.Text);
            return left > 0m ? left : 0m;
        }

        private static decimal Amount(string text)
        {
            decimal value;
            return Fmt.TryParseDecimal(text, out value) && value > 0m ? value : 0m;
        }

        private PaymentKind Kind()
        {
            return (PaymentKind)Math.Max(0, _kind.SelectedIndex);
        }

        private void UpdateSummary()
        {
            decimal cash = Amount(_cashBox.Text);
            decimal cashless = Amount(_cashlessBox.Text);
            decimal total = cash + cashless;
            decimal left = _due - total;

            PaymentKind kind = Kind();
            if (kind == PaymentKind.Cash && cashless > 0m && cash == 0m) kind = PaymentKind.Cashless;
            if (kind == PaymentKind.Cashless && cash > 0m && cashless == 0m) kind = PaymentKind.Cash;

            string text = "К оплате: " + Fmt.Money(total) + " \u20BD";

            if (left > 0.005m) text += "   •   осталось доплатить: " + Fmt.Money(left) + " \u20BD";
            else if (left < -0.005m) text += "   •   больше суммы заявки на " + Fmt.Money(-left) + " \u20BD";
            else if (_due > 0m) text += "   •   заявка оплачена полностью";

            _summary.Text = text;

            bool wrongKind = (kind == PaymentKind.Cash && cashless > 0m) ||
                             (kind == PaymentKind.Cashless && cash > 0m);

            _ok.Enabled = total > 0m && left >= -0.005m && !wrongKind;

            if (wrongKind)
                _summary.Text = text + Environment.NewLine +
                    "Для двух сумм выберите смешанную оплату.";
            else if (left < -0.005m)
                _summary.Text = text + Environment.NewLine +
                    "Сумма больше заявки: уменьшите платёж.";
        }

        private void AddDesk()
        {
            string name = _deskNew.Text.Trim();

            if (name.Length == 0)
            {
                MessageBox.Show(this, "Введите название кассы в поле «Или название новой».",
                    "Касса", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _deskNew.Focus();
                return;
            }

            CashDesk created = _cash.Ensure(name);
            if (!_deskList.Items.Contains(created.Name)) _deskList.Items.Add(created.Name);
            _deskList.SelectedItem = created.Name;
            _deskNew.Text = "";
        }

        private void Accept()
        {
            string desk = _deskList.SelectedItem == null ? "" : Convert.ToString(_deskList.SelectedItem);

            if (_deskNew.Text.Trim().Length > 0)
            {
                AddDesk();
                desk = _deskList.SelectedItem == null ? desk : Convert.ToString(_deskList.SelectedItem);
            }

            if (desk.Length == 0)
            {
                MessageBox.Show(this, "Выберите кассу или создайте новую.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            decimal cash = Amount(_cashBox.Text);
            decimal cashless = Amount(_cashlessBox.Text);

            if (cash + cashless <= 0m)
            {
                MessageBox.Show(this, "Введите сумму оплаты.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Payment payment = new Payment();
            payment.Number = _number;
            payment.Customer = _customer;
            payment.Saved = DateTime.Now;
            payment.Desk = desk;
            payment.Kind = Kind();
            payment.Cash = cash;
            payment.Cashless = cashless;
            payment.Due = _due;
            payment.Note = _note.Text.Trim();

            Result = payment;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Справочник касс с балансами.</summary>
    internal sealed class CashDeskForm : Form
    {
        private readonly CashBook _cash;
        private readonly ICashBookStore _store;

        private ListView _list;
        private Label _info;

        public CashDeskForm(CashBook cash, ICashBookStore store)
        {
            _cash = cash ?? new CashBook();
            _store = store;

            Text = "Кассы — Заявки на расчет";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 420);
            ClientSize = new Size(820, 480);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(ClientSize.Width - 32, 22);
            header.Location = new Point(16, 14);
            header.Font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = "Кассы и балансы";
            Controls.Add(header);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Location = new Point(16, 44);
            _list.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 150);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.Columns.Add("Касса", 240);
            _list.Columns.Add("Баланс, ₽", 150, HorizontalAlignment.Right);
            _list.Columns.Add("Оплат", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Примечание", 280);
            _list.SelectedIndexChanged += delegate { UpdateInfo(); };
            Controls.Add(_list);

            _info = new Label();
            _info.AutoSize = false;
            _info.Size = new Size(ClientSize.Width - 32, 22);
            _info.Location = new Point(16, ClientSize.Height - 66);
            _info.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            _info.ForeColor = Color.FromArgb(110, 118, 130);
            Controls.Add(_info);

            Button add = MakeButton("Добавить кассу", 160);
            add.Location = new Point(16, ClientSize.Height - 40);
            add.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            add.Click += delegate { AddDesk(); };
            Controls.Add(add);

            Button rename = MakeButton("Переименовать", 150);
            rename.Location = new Point(186, ClientSize.Height - 40);
            rename.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            rename.Click += delegate { RenameDesk(); };
            Controls.Add(rename);

            Button operation = MakeButton("Внести или изъять", 170);
            operation.Location = new Point(346, ClientSize.Height - 40);
            operation.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            operation.Click += delegate { AddOperation(); };
            Controls.Add(operation);

            Button remove = MakeButton("Удалить", 110);
            remove.Location = new Point(526, ClientSize.Height - 40);
            remove.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            remove.Click += delegate { RemoveDesk(); };
            Controls.Add(remove);

            Button close = MakeButton("Закрыть", 110);
            close.Location = new Point(ClientSize.Width - 110 - 16, ClientSize.Height - 40);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);

            CancelButton = close;
            Fill();
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

            foreach (CashDesk desk in _cash.Desks)
            {
                decimal balance = _cash.Balance(desk.Name);
                int payments = 0;
                foreach (Payment payment in _cash.Payments)
                    if (string.Equals(payment.Desk, desk.Name, StringComparison.CurrentCultureIgnoreCase))
                        payments++;

                ListViewItem row = new ListViewItem(desk.Name);
                row.SubItems.Add(Fmt.Money(balance));
                row.SubItems.Add(payments.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(desk.Note);
                row.Tag = desk;
                _list.Items.Add(row);
            }

            if (_list.Items.Count > 0) _list.Items[0].Selected = true;

            UpdateInfo();
        }

        private CashDesk Current()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as CashDesk;
        }

        private void UpdateInfo()
        {
            decimal total = 0m;
            foreach (CashDesk desk in _cash.Desks) total += _cash.Balance(desk.Name);

            _info.Text = "Касс: " + _cash.Desks.Count + "   •   денег во всех кассах: " +
                         Fmt.Money(total) + " \u20BD   •   оплат записано: " + _cash.Payments.Count;
        }

        private void Save()
        {
            try
            {
                if (_store != null) _store.Save(_cash);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить кассы:\n" + ex.Message,
                    "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddDesk()
        {
            string name = Ask("Название новой кассы:", "");

            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;

            if (_cash.Find(name) != null)
            {
                MessageBox.Show(this, "Касса с таким названием уже есть.",
                    "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _cash.Ensure(name);
            Save();
            Fill();
        }

        private void RenameDesk()
        {
            CashDesk desk = Current();
            if (desk == null) return;

            string name = Ask("Новое название кассы:", desk.Name);
            if (name == null) return;

            name = name.Trim();
            if (name.Length == 0 || name == desk.Name) return;

            if (_cash.Find(name) != null)
            {
                MessageBox.Show(this, "Касса с таким названием уже есть.",
                    "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // переносим оплаты и операции на новое название
            foreach (Payment payment in _cash.Payments)
                if (string.Equals(payment.Desk, desk.Name, StringComparison.CurrentCultureIgnoreCase))
                    payment.Desk = name;

            foreach (DeskOperation operation in _cash.Operations)
                if (string.Equals(operation.Desk, desk.Name, StringComparison.CurrentCultureIgnoreCase))
                    operation.Desk = name;

            desk.Name = name;
            Save();
            Fill();
        }

        private void RemoveDesk()
        {
            CashDesk desk = Current();
            if (desk == null) return;

            decimal balance = _cash.Balance(desk.Name);
            int payments = 0;
            foreach (Payment payment in _cash.Payments)
                if (string.Equals(payment.Desk, desk.Name, StringComparison.CurrentCultureIgnoreCase))
                    payments++;

            if (payments > 0)
            {
                MessageBox.Show(this,
                    "В кассе «" + desk.Name + "» есть оплаты: " + payments +
                    " на сумму " + Fmt.Money(balance) + " \u20BD.\n\n" +
                    "Сначала перенесите оплаты в другую кассу или удалите их.",
                    "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, "Удалить кассу «" + desk.Name + "»?",
                    "Кассы", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _cash.Desks.Remove(desk);
            Save();
            Fill();
        }

        private void AddOperation()
        {
            CashDesk desk = Current();
            if (desk == null)
            {
                MessageBox.Show(this, "Выберите кассу в списке.",
                    "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (CashOperationForm dialog = new CashOperationForm(desk.Name, _cash.Balance(desk.Name)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _cash.Operations.Add(dialog.Result);
                Save();
                Fill();
            }
        }

        /// <summary>Простой запрос строки.</summary>
        private string Ask(string question, string value)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Кассы";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(420, 130);
                dialog.Font = Font;

                Label label = new Label();
                label.Text = question;
                label.AutoSize = true;
                label.Location = new Point(16, 16);
                dialog.Controls.Add(label);

                TextBox box = new TextBox();
                box.Location = new Point(16, 44);
                box.Width = 388;
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

    /// <summary>Внесение или изъятие денег из кассы.</summary>
    internal sealed class CashOperationForm : Form
    {
        private TextBox _amount;
        private TextBox _note;
        private RadioButton _income;
        private RadioButton _expense;
        private Label _summary;

        public DeskOperation Result { get; private set; }

        public CashOperationForm(string desk, decimal balance)
        {
            Text = "Операция по кассе";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 250);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(428, 40);
            header.Location = new Point(16, 12);
            header.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = "Касса «" + desk + "»" + Environment.NewLine +
                          "сейчас в кассе: " + Fmt.Money(balance) + " \u20BD";
            Controls.Add(header);

            _income = new RadioButton();
            _income.Text = "Внести деньги";
            _income.AutoSize = true;
            _income.Checked = true;
            _income.Location = new Point(20, 62);
            Controls.Add(_income);

            _expense = new RadioButton();
            _expense.Text = "Изъять деньги";
            _expense.AutoSize = true;
            _expense.Location = new Point(200, 62);
            Controls.Add(_expense);

            Label amountLabel = new Label();
            amountLabel.Text = "Сумма, \u20BD:";
            amountLabel.AutoSize = true;
            amountLabel.Location = new Point(16, 98);
            Controls.Add(amountLabel);

            _amount = new TextBox();
            _amount.Location = new Point(120, 95);
            _amount.Width = 140;
            _amount.TextAlign = HorizontalAlignment.Right;
            _amount.TextChanged += delegate { UpdateSummary(desk, balance); };
            Controls.Add(_amount);

            Label noteLabel = new Label();
            noteLabel.Text = "Примечание:";
            noteLabel.AutoSize = true;
            noteLabel.Location = new Point(16, 132);
            Controls.Add(noteLabel);

            _note = new TextBox();
            _note.Location = new Point(120, 129);
            _note.Width = 320;
            Controls.Add(_note);

            _summary = new Label();
            _summary.AutoSize = false;
            _summary.Size = new Size(428, 22);
            _summary.Location = new Point(16, 164);
            _summary.ForeColor = Color.FromArgb(110, 118, 130);
            Controls.Add(_summary);

            Button ok = new Button();
            ok.Text = "Записать";
            ok.Width = 140;
            ok.Height = 28;
            ok.FlatStyle = FlatStyle.System;
            ok.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            ok.Location = new Point(ClientSize.Width - 140 - 120, ClientSize.Height - 44);
            ok.Click += delegate { Accept(desk, balance); };
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

            UpdateSummary(desk, balance);
        }

        private void UpdateSummary(string desk, decimal balance)
        {
            decimal amount;
            bool parsed = Fmt.TryParseDecimal(_amount.Text, out amount) && amount > 0m;

            if (!parsed) { _summary.Text = "Введите сумму."; return; }

            decimal after = _income.Checked ? balance + amount : balance - amount;

            _summary.Text = "После операции в кассе будет: " + Fmt.Money(after) + " \u20BD";

            if (after < 0m)
                _summary.Text += "   •   изъятие больше остатка";
        }

        private void Accept(string desk, decimal balance)
        {
            decimal amount;
            if (!Fmt.TryParseDecimal(_amount.Text, out amount) || amount <= 0m)
            {
                MessageBox.Show(this, "Введите сумму операции.",
                    "Операция", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_expense.Checked && amount > balance)
            {
                if (MessageBox.Show(this,
                        "Изъятие " + Fmt.Money(amount) + " \u20BD больше остатка " +
                        Fmt.Money(balance) + " \u20BD. Всё равно записать?",
                        "Операция", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            DeskOperation operation = new DeskOperation();
            operation.Desk = desk;
            operation.Saved = DateTime.Now;
            operation.Amount = _income.Checked ? amount : -amount;
            operation.Note = _note.Text.Trim();

            Result = operation;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
