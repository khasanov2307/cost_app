// ---------------------------------------------------------------------------
//  Сохранение и открытие смет.
//
//  Номер сметы присваивается автоматически и нумеруется в рамках года,
//  дата — дата сохранения. Из окна списка смету можно открыть для просмотра
//  или удалить.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Окно со списком сохранённых смет.</summary>
    internal sealed class EstimateListForm : Form
    {
        private readonly List<SavedEstimate> _estimates;
        private readonly ListView _list;
        private readonly Label _info;

        /// <summary>Выбранная смета.</summary>
        public SavedEstimate Selected { get; private set; }

        /// <summary>Нужно ли удалить выбранную смету.</summary>
        public bool DeleteRequested { get; private set; }

        public EstimateListForm(List<SavedEstimate> estimates, string archiveTitle)
        {
            _estimates = estimates ?? new List<SavedEstimate>();

            Text = "Сохранённые сметы";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 460);
            ClientSize = new Size(880, 520);
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            Label header = new Label();
            header.AutoSize = false;
            header.Size = new Size(ClientSize.Width - 32, 22);
            header.Location = new Point(16, 14);
            header.Font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
            header.Text = "Сохранённые сметы";
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
            _list.Columns.Add("Номер", 110);
            _list.Columns.Add("Дата сохранения", 150);
            _list.Columns.Add("Заказчик", 200);
            _list.Columns.Add("Позиций", 80, HorizontalAlignment.Right);
            _list.Columns.Add("Сумма, ₽", 130, HorizontalAlignment.Right);
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
            open.Location = new Point(ClientSize.Width - 200 - 200, ClientSize.Height - 42);
            open.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            open.Click += delegate { Accept(false); };
            Controls.Add(open);

            Button remove = MakeButton("Удалить", 110);
            remove.Location = new Point(ClientSize.Width - 110 - 78, ClientSize.Height - 42);
            remove.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            remove.Click += delegate { Accept(true); };
            Controls.Add(remove);

            Button close = MakeButton("Отмена", 70);
            close.Location = new Point(ClientSize.Width - 70 - 16, ClientSize.Height - 42);
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
                ListViewItem row = new ListViewItem(estimate.Number);
                row.SubItems.Add(estimate.Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru));
                row.SubItems.Add(estimate.Customer);
                row.SubItems.Add(estimate.Items.Count.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(Fmt.Money(estimate.Total));
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

        private void UpdateInfo()
        {
            int count = _estimates.Count;

            if (count == 0)
            {
                _info.Text = "Смет пока нет. Сохраните текущую смету — она появится в этом списке.";
                return;
            }

            SavedEstimate estimate = Current();
            if (estimate == null)
            {
                _info.Text = "Смет в списке: " + count;
                return;
            }

            _info.Text = "Смет в списке: " + count + "   •   выбрана " + estimate.Caption;
        }

        private SavedEstimate Current()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as SavedEstimate;
        }

        private void Accept(bool delete)
        {
            SavedEstimate estimate = Current();
            if (estimate == null)
            {
                MessageBox.Show(this, "Выберите смету в списке.", "Сохранённые сметы",
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
