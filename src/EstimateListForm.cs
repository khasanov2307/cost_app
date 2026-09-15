// ---------------------------------------------------------------------------
//  Список сохранённых заявок: номер, дата, заказчик, число позиций, сумма,
//  оплата и её состояние (оплачена полностью, частично или не оплачена).
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed class EstimateListForm : Form
    {
        private readonly List<SavedEstimate> _estimates;
        private readonly CashBook _cash;
        private readonly ListView _list;
        private readonly Label _info;

        /// <summary>Выбранная заявка.</summary>
        public SavedEstimate Selected { get; private set; }

        /// <summary>Нужно ли удалить выбранную заявку.</summary>
        public bool DeleteRequested { get; private set; }

        public EstimateListForm(List<SavedEstimate> estimates, string archiveTitle)
            : this(estimates, archiveTitle, null)
        {
        }

        public EstimateListForm(List<SavedEstimate> estimates, string archiveTitle, CashBook cash)
        {
            _estimates = estimates ?? new List<SavedEstimate>();
            _cash = cash;

            Text = "Сохранённые заявки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 460);
            ClientSize = new Size(1040, 520);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(ClientSize.Width - 32, 22);
            header.Location = new Point(16, 14);
            header.Font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = "Сохранённые заявки";
            Controls.Add(header);

            Label where = new Label();
            where.AutoSize = false;
            where.Size = new Size(ClientSize.Width - 32, 20);
            where.Location = new Point(16, 40);
            where.ForeColor = Color.FromArgb(110, 118, 130);
            where.Text = archiveTitle;
            Controls.Add(where);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Location = new Point(16, 68);
            _list.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 140);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.Columns.Add("Номер", 100);
            _list.Columns.Add("Дата сохранения", 140);
            _list.Columns.Add("Заказчик", 170);
            _list.Columns.Add("Позиций", 70, HorizontalAlignment.Right);
            _list.Columns.Add("Сумма, ₽", 120, HorizontalAlignment.Right);
            _list.Columns.Add("Оплачено, ₽", 120, HorizontalAlignment.Right);
            _list.Columns.Add("Оплата, %", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Состояние", 200);
            _list.DoubleClick += delegate { Accept(false); };
            _list.SelectedIndexChanged += delegate { UpdateInfo(); };
            Controls.Add(_list);

            _info = new Label();
            _info.AutoSize = false;
            _info.Size = new Size(ClientSize.Width - 32, 22);
            _info.Location = new Point(16, ClientSize.Height - 64);
            _info.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            _info.ForeColor = Color.FromArgb(110, 118, 130);
            Controls.Add(_info);

            Button open = MakeButton("Открыть для просмотра", 200);
            open.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            open.Location = new Point(ClientSize.Width - 200 - 340, ClientSize.Height - 42);
            open.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            open.Click += delegate { Accept(false); };
            Controls.Add(open);

            Button pay = MakeButton("Оплата", 110);
            pay.Location = new Point(ClientSize.Width - 110 - 220, ClientSize.Height - 42);
            pay.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            pay.Click += delegate { ShowPayment(); };
            Controls.Add(pay);

            Button remove = MakeButton("Удалить", 110);
            remove.Location = new Point(ClientSize.Width - 110 - 100, ClientSize.Height - 42);
            remove.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            remove.Click += delegate { Accept(true); };
            Controls.Add(remove);

            Button close = MakeButton("Отмена", 90);
            close.Location = new Point(ClientSize.Width - 90 - 16, ClientSize.Height - 42);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.DialogResult = DialogResult.Cancel;
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

            foreach (SavedEstimate estimate in _estimates)
            {
                Payment payment = PaymentOf(estimate.Number);
                decimal paid = payment == null ? 0m : payment.Total;
                decimal due = estimate.Total;

                int percent = 0;
                if (due > 0m) percent = (int)Math.Round(paid * 100m / due);
                if (percent > 100) percent = 100;

                ListViewItem row = new ListViewItem(estimate.Number);
                row.SubItems.Add(estimate.Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));
                row.SubItems.Add(estimate.Customer);
                row.SubItems.Add(estimate.Items.Count.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(Fmt.Money(due));
                row.SubItems.Add(paid > 0m ? Fmt.Money(paid) : "");
                row.SubItems.Add(paid > 0m ? percent + " %" : "");
                row.SubItems.Add(PaymentState(due, paid));

                if (paid > 0m && percent < 100)
                    row.ForeColor = Color.FromArgb(150, 95, 0);
                else if (percent >= 100)
                    row.ForeColor = Color.FromArgb(30, 110, 50);

                row.Tag = estimate;
                _list.Items.Add(row);
            }

            if (_list.Items.Count > 0)
            {
                _list.Items[0].Selected = true;
                _list.Select();
            }

            UpdateInfo();
        }

        /// <summary>Состояние оплаты словами.</summary>
        private static string PaymentState(decimal due, decimal paid)
        {
            if (paid <= 0m) return "не оплачена";

            if (due > 0m)
            {
                if (paid >= due - 0.005m) return "оплачена полностью";
                return "оплачена частично, долг " + Fmt.Money(due - paid);
            }

            return "оплачена";
        }

        private Payment PaymentOf(string number)
        {
            return _cash == null ? null : _cash.FindPayment(number);
        }

        private void UpdateInfo()
        {
            int count = _estimates.Count;

            if (count == 0)
            {
                _info.Text = "Заявок пока нет. Сохраните текущую заявку — она появится в этом списке.";
                return;
            }

            SavedEstimate estimate = Current();
            if (estimate == null)
            {
                _info.Text = "Заявок в списке: " + count;
                return;
            }

            Payment payment = PaymentOf(estimate.Number);

            string text = "Заявок в списке: " + count + "   •   выбрана " + estimate.Caption;

            if (payment != null) text += "   •   " + payment.Caption;

            _info.Text = text;
        }

        private SavedEstimate Current()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as SavedEstimate;
        }

        /// <summary>Оплата выбранной заявки прямо из списка.</summary>
        private void ShowPayment()
        {
            SavedEstimate estimate = Current();
            if (estimate == null)
            {
                MessageBox.Show(this, "Выберите заявку в списке.", "Сохранённые заявки",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_cash == null)
            {
                MessageBox.Show(this, "Кассы недоступны.", "Сохранённые заявки",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Payment existing = _cash.FindPayment(estimate.Number);

            using (PaymentForm dialog = new PaymentForm(_cash, estimate.Number, estimate.Customer,
                                                       estimate.Total, existing))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _cash.AddPayment(dialog.Result);

                try { ConnectionSettings.CashBook.Save(_cash); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось сохранить оплату:\n" + ex.Message,
                        "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            Fill();
        }

        private void Accept(bool delete)
        {
            SavedEstimate estimate = Current();
            if (estimate == null)
            {
                MessageBox.Show(this, "Выберите заявку в списке.", "Сохранённые заявки",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Selected = estimate;
            DeleteRequested = delete;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
