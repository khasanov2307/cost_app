// ---------------------------------------------------------------------------
//  Панель показателей: деньги в кассах, оплаченные заявки за день, неделю
//  и месяц, самые популярные позиции и средний чек.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed class DashboardForm : Form
    {
        private readonly CashBook _cash;
        private readonly List<SavedEstimate> _estimates;

        public DashboardForm(CashBook cash, List<SavedEstimate> estimates)
        {
            _cash = cash ?? new CashBook();
            _estimates = estimates ?? new List<SavedEstimate>();

            Text = "Показатели — Заявки на расчет";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 560);
            ClientSize = new Size(940, 640);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Build();
            Fill();
        }

        private Font _big;
        private Font _bold;

        private Label _deskTotal;
        private Label _today;
        private Label _week;
        private Label _month;
        private Label _average;
        private ListView _desks;
        private ListView _popular;
        private Label _footer;

        private void Build()
        {
            _big = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Point);
            _bold = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 118;
            Controls.Add(top);

            _deskTotal = MakeCard(top, "Денег в кассах", 14);
            _today = MakeCard(top, "Оплачено за день", 240);
            _week = MakeCard(top, "За неделю", 466);
            _month = MakeCard(top, "За месяц", 692);

            Label averageLabel = new Label();
            averageLabel.Text = "Средний чек";
            averageLabel.AutoSize = true;
            averageLabel.ForeColor = Color.FromArgb(110, 118, 130);
            averageLabel.Location = new Point(14, 62);
            Controls.Add(averageLabel);

            _average = new Label();
            _average.AutoSize = true;
            _average.Font = _big;
            _average.ForeColor = Color.FromArgb(20, 70, 130);
            _average.Location = new Point(14, 78);
            Controls.Add(_average);

            // кассы и популярные позиции
            Panel middle = new Panel();
            middle.Dock = DockStyle.Fill;
            middle.Padding = new Padding(14, 6, 14, 6);
            Controls.Add(middle);
            middle.BringToFront();

            Label desksHeader = new Label();
            desksHeader.Text = "Деньги по кассам";
            desksHeader.Font = _bold;
            desksHeader.AutoSize = true;
            desksHeader.Location = new Point(14, 128);
            Controls.Add(desksHeader);
            desksHeader.BringToFront();

            _desks = new ListView();
            _desks.View = View.Details;
            _desks.FullRowSelect = true;
            _desks.MultiSelect = false;
            _desks.HideSelection = false;
            _desks.Location = new Point(14, 150);
            _desks.Size = new Size(430, 300);
            _desks.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            _desks.Columns.Add("Касса", 200);
            _desks.Columns.Add("Баланс, ₽", 130, HorizontalAlignment.Right);
            _desks.Columns.Add("Оплат", 70, HorizontalAlignment.Right);
            Controls.Add(_desks);
            _desks.BringToFront();

            Label popularHeader = new Label();
            popularHeader.Text = "Самые популярные позиции в заявках";
            popularHeader.Font = _bold;
            popularHeader.AutoSize = true;
            popularHeader.Location = new Point(460, 128);
            Controls.Add(popularHeader);
            popularHeader.BringToFront();

            _popular = new ListView();
            _popular.View = View.Details;
            _popular.FullRowSelect = true;
            _popular.MultiSelect = false;
            _popular.HideSelection = false;
            _popular.Location = new Point(460, 150);
            _popular.Size = new Size(466, 300);
            _popular.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            _popular.Columns.Add("Позиция", 226);
            _popular.Columns.Add("Заявок", 70, HorizontalAlignment.Right);
            _popular.Columns.Add("Кол-во", 70, HorizontalAlignment.Right);
            _popular.Columns.Add("Сумма, ₽", 96, HorizontalAlignment.Right);
            Controls.Add(_popular);
            _popular.BringToFront();

            _footer = new Label();
            _footer.AutoSize = false;
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 34;
            _footer.ForeColor = Color.FromArgb(110, 118, 130);
            _footer.Padding = new Padding(16, 8, 16, 0);
            Controls.Add(_footer);

            Button refresh = new Button();
            refresh.Text = "Обновить";
            refresh.Width = 120;
            refresh.Height = 28;
            refresh.FlatStyle = FlatStyle.System;
            refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            refresh.Location = new Point(ClientSize.Width - 120 - 140, ClientSize.Height - 38);
            refresh.Click += delegate { Fill(); };
            Controls.Add(refresh);
            refresh.BringToFront();

            Button close = new Button();
            close.Text = "Закрыть";
            close.Width = 120;
            close.Height = 28;
            close.FlatStyle = FlatStyle.System;
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Location = new Point(ClientSize.Width - 120 - 16, ClientSize.Height - 38);
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);
            close.BringToFront();

            CancelButton = close;
        }

        private Label MakeCard(Control parent, string title, int x)
        {
            Label caption = new Label();
            caption.Text = title;
            caption.AutoSize = true;
            caption.ForeColor = Color.FromArgb(110, 118, 130);
            caption.Location = new Point(x, 14);
            parent.Controls.Add(caption);

            Label value = new Label();
            value.AutoSize = true;
            value.Font = _big;
            value.ForeColor = Color.FromArgb(20, 70, 130);
            value.Location = new Point(x, 30);
            parent.Controls.Add(value);

            return value;
        }

        private void Fill()
        {
            DashboardInfo info = Dashboard.Build(_cash, _estimates, DateTime.Now, 15);

            _deskTotal.Text = Fmt.Money(info.DeskTotal) + " \u20BD";
            _today.Text = info.PaidToday + " / " + Fmt.Money(info.AmountToday) + " \u20BD";
            _week.Text = info.PaidWeek + " / " + Fmt.Money(info.AmountWeek) + " \u20BD";
            _month.Text = info.PaidMonth + " / " + Fmt.Money(info.AmountMonth) + " \u20BD";
            _average.Text = info.AverageCheck > 0m ? Fmt.Money(info.AverageCheck) + " \u20BD" : "нет оплат";

            _desks.Items.Clear();
            foreach (DashboardInfo.DeskRow row in info.Desks)
            {
                ListViewItem item = new ListViewItem(row.Desk);
                item.SubItems.Add(Fmt.Money(row.Balance));
                item.SubItems.Add(row.Payments.ToString(CultureInfo.InvariantCulture));
                _desks.Items.Add(item);
            }

            _popular.Items.Clear();
            foreach (DashboardInfo.ItemRow row in info.Popular)
            {
                ListViewItem item = new ListViewItem(row.Name);
                item.SubItems.Add(row.Requests.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(Fmt.Qty(row.Quantity));
                item.SubItems.Add(Fmt.Money(row.Amount));
                _popular.Items.Add(item);
            }

            _footer.Text = "Касс: " + info.Desks.Count +
                           "   •   оплат всего: " + info.PaidTotal +
                           " на сумму " + Fmt.Money(info.AmountTotal) + " \u20BD" +
                           "   •   заявок в архиве: " + _estimates.Count +
                           "   •   данные на " + info.To.ToString("dd.MM.yyyy HH:mm", Fmt.Ru);
        }
    }
}
