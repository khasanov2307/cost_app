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
        private Panel _middle;

        public DashboardForm(CashBook cash, List<SavedEstimate> estimates)
        {
            _cash = cash ?? new CashBook();
            _estimates = estimates ?? new List<SavedEstimate>();

            Text = "Показатели — Расчет заявки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 560);
            ClientSize = new Size(940, 640);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Build();
            Resize += delegate { LayoutLists(); };
            Fill();
            LayoutLists();
        }

        private void Build()
        {
            _big = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Point);
            _bold = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);

            // нижняя полоса: итоги и кнопки — добавляется первой, чтобы Fill её не закрыл
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 44;
            Controls.Add(bottom);

            _footer = new Label();
            _footer.AutoSize = false;
            _footer.Dock = DockStyle.Fill;
            _footer.ForeColor = Color.FromArgb(110, 118, 130);
            _footer.Padding = new Padding(16, 12, 16, 0);
            _footer.TextAlign = ContentAlignment.MiddleLeft;
            bottom.Controls.Add(_footer);

            Button close = new Button();
            close.Text = "Закрыть";
            close.Width = 120;
            close.Height = 28;
            close.FlatStyle = FlatStyle.System;
            close.Dock = DockStyle.Right;
            close.Margin = new Padding(6);
            close.DialogResult = DialogResult.OK;
            bottom.Controls.Add(close);
            close.BringToFront();

            Button refresh = new Button();
            refresh.Text = "Обновить";
            refresh.Width = 120;
            refresh.Height = 28;
            refresh.FlatStyle = FlatStyle.System;
            refresh.Dock = DockStyle.Right;
            refresh.Margin = new Padding(6);
            refresh.Click += delegate { Fill(); LayoutLists(); };
            bottom.Controls.Add(refresh);
            refresh.BringToFront();


            CancelButton = close;

            // верхняя полоса: карточки с суммами
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 122;
            Controls.Add(top);

            _deskTotal = MakeCard(top, "Денег в кассах", 16, 14);
            _today = MakeCard(top, "Оплачено за день", 250, 14);
            _week = MakeCard(top, "За неделю", 480, 14);
            _month = MakeCard(top, "За месяц", 700, 14);

            Label averageLabel = new Label();
            averageLabel.Text = "Средний чек";
            averageLabel.AutoSize = true;
            averageLabel.ForeColor = Color.FromArgb(110, 118, 130);
            averageLabel.Location = new Point(16, 66);
            top.Controls.Add(averageLabel);

            _average = new Label();
            _average.AutoSize = true;
            _average.Font = _big;
            _average.ForeColor = Color.FromArgb(20, 70, 130);
            _average.Location = new Point(16, 82);
            top.Controls.Add(_average);

            // середина: две таблицы, растягивается по размеру окна
            _middle = new Panel();
            _middle.Dock = DockStyle.Fill;
            Controls.Add(_middle);
            _middle.BringToFront();

            Label desksHeader = new Label();
            desksHeader.Text = "Деньги по кассам";
            desksHeader.Font = _bold;
            desksHeader.AutoSize = true;
            desksHeader.Location = new Point(16, 4);
            _middle.Controls.Add(desksHeader);

            _desks = new ListView();
            _desks.View = View.Details;
            _desks.FullRowSelect = true;
            _desks.MultiSelect = false;
            _desks.HideSelection = false;
            _desks.Location = new Point(16, 26);
            _desks.Columns.Add("Касса", 200);
            _desks.Columns.Add("Баланс, ₽", 130, HorizontalAlignment.Right);
            _desks.Columns.Add("Оплат", 70, HorizontalAlignment.Right);
            _middle.Controls.Add(_desks);

            Label popularHeader = new Label();
            popularHeader.Text = "Самые популярные позиции в заявках";
            popularHeader.Font = _bold;
            popularHeader.AutoSize = true;
            popularHeader.Location = new Point(470, 4);
            _middle.Controls.Add(popularHeader);

            _popular = new ListView();
            _popular.View = View.Details;
            _popular.FullRowSelect = true;
            _popular.MultiSelect = false;
            _popular.HideSelection = false;
            _popular.Location = new Point(470, 26);
            _popular.Columns.Add("Позиция", 300);
            _popular.Columns.Add("Заявок", 64, HorizontalAlignment.Right);
            _popular.Columns.Add("Кол-во", 64, HorizontalAlignment.Right);
            _popular.Columns.Add("Сумма, ₽", 110, HorizontalAlignment.Right);
            _middle.Controls.Add(_popular);
        }

        /// <summary>Таблицы занимают всю ширину и высоту средней части.</summary>
        private void LayoutLists()
        {
            if (_middle == null || _desks == null || _popular == null) return;

            int width = _middle.ClientSize.Width;
            int height = _middle.ClientSize.Height;
            if (width < 200 || height < 80) return;

            int gap = 22;
            int half = (width - 16 * 2 - gap) / 2;
            int listHeight = Math.Max(80, height - 26 - 10);

            _desks.Location = new Point(16, 26);
            _desks.Size = new Size(half, listHeight);

            _popular.Location = new Point(16 + half + gap, 26);
            _popular.Size = new Size(width - (16 + half + gap) - 16, listHeight);

            _desks.Columns[0].Width = Math.Max(120, half - 206);
            _popular.Columns[0].Width = Math.Max(160, _popular.Width - 254);
        }

        private Label MakeCard(Control parent, string title, int x, int y)
        {
            Label caption = new Label();
            caption.Text = title;
            caption.AutoSize = true;
            caption.ForeColor = Color.FromArgb(110, 118, 130);
            caption.Location = new Point(x, y);
            parent.Controls.Add(caption);

            Label value = new Label();
            value.AutoSize = true;
            value.Font = _big;
            value.ForeColor = Color.FromArgb(20, 70, 130);
            value.Location = new Point(x, y + 16);
            parent.Controls.Add(value);

            return value;
        }

        private void Fill()
        {
            DashboardInfo info = Dashboard.Build(_cash, _estimates, DateTime.Now, 15);

            _deskTotal.Text = Fmt.Money(info.DeskTotal);
            _today.Text = info.PaidToday + " / " + Fmt.Money(info.AmountToday);
            _week.Text = info.PaidWeek + " / " + Fmt.Money(info.AmountWeek);
            _month.Text = info.PaidMonth + " / " + Fmt.Money(info.AmountMonth);
            _average.Text = info.AverageCheck > 0m ? Fmt.Money(info.AverageCheck) : "нет оплат";

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
                           " на сумму " + Fmt.Money(info.AmountTotal) +
                           "   •   заявок в архиве: " + _estimates.Count +
                           "   •   данные на " + info.To.ToString("dd.MM.yyyy HH:mm", Fmt.Ru);
        }
    }
}
