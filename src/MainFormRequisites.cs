// ---------------------------------------------------------------------------
//  Строка реквизитов заявки: номер, статус, скидка, заказчик, телефон,
//  автомобиль и госномер.
//
//  Заказчика можно выбрать из справочника или ввести строкой. Список
//  наполняется заранее — при входе в поле и после ввода, но не в момент
//  раскрытия: если очистить список при раскрытии, WinForms закрывает его.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed partial class MainForm
    {
        private ComboBox _statusBox;
        private TextBox _customerPick;             // заказчик: выбирается или вводится строкой
        private PhoneBox _phoneBox;
        private TextBox _carBox;
        private TextBox _plateBox;
        private Button _btnStock;
        private Button _btnCopyLast;              // повторить прошлую заявку
        private Button _btnClearCustomer;         // очистить выбранного заказчика
        private Button _btnPickCustomer;          // выбрать заказчика из справочника

        private string _customerId = "";
        private bool _showStock;

        /// <summary>Строка реквизитов: номер, статус, заказчик, автомобиль.</summary>
        private void BuildRequisiteRow(Panel top)
        {
            // --- первая строка: номер, статус, скидка
            Label lblNumber = MakeLabel("Номер заявки:");
            lblNumber.Location = new Point(14, 94);
            top.Controls.Add(lblNumber);

            _numberBox = new TextBox();
            _numberBox.Location = new Point(110, 90);
            _numberBox.Width = 90;
            _numberBox.TextChanged += DocumentFields_Changed;
            top.Controls.Add(_numberBox);

            Label lblStatus = MakeLabel("Статус:");
            lblStatus.Location = new Point(214, 94);
            top.Controls.Add(lblStatus);

            _statusBox = new ComboBox();
            _statusBox.Location = new Point(268, 90);
            _statusBox.Width = 130;
            _statusBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _statusBox.Items.AddRange(EstimateStatuses.List);
            _statusBox.SelectedIndex = 0;
            _statusBox.SelectedIndexChanged += delegate { SaveSettings(); };
            top.Controls.Add(_statusBox);

            Label lblDiscount = MakeLabel("Скидка, %:");
            lblDiscount.Location = new Point(414, 94);
            top.Controls.Add(lblDiscount);

            _discountBox = new NumericUpDown();
            _discountBox.Location = new Point(490, 90);
            _discountBox.Width = 70;
            _discountBox.DecimalPlaces = 1;
            _discountBox.Maximum = 90m;
            _discountBox.Increment = 5m;
            _discountBox.TextAlign = HorizontalAlignment.Right;
            _discountBox.ValueChanged += DocumentFields_Changed;
            top.Controls.Add(_discountBox);

            // показ остатков склада в таблице
            _btnStock = MakeButton("Показывать остатки", 180);
            _btnStock.Location = new Point(580, 89);
            _btnStock.Click += delegate { ToggleStockColumn(); };
            top.Controls.Add(_btnStock);

            // --- вторая строка: заказчик и телефон
            Label lblCustomer = MakeLabel("Заказчик:");
            lblCustomer.Location = new Point(14, 128);
            top.Controls.Add(lblCustomer);

            // Заказчик: обычное поле ввода плюс кнопка выбора из справочника.
            // Выпадающий список не используем: он перезаполняется при вводе
            // и сбрасывает место курсора, поэтому курсор прыгал в начало.
            _customerPick = new TextBox();
            _customerPick.Location = new Point(110, 124);
            _customerPick.Width = 220;
            _customerPick.TextChanged += CustomerTyped;
            top.Controls.Add(_customerPick);

            _btnPickCustomer = MakeButton("Найти", 100);
            _btnPickCustomer.Location = new Point(338, 123);
            _btnPickCustomer.Click += delegate { PickCustomer(); };   // поиск и выбор заказчика
            top.Controls.Add(_btnPickCustomer);

            _btnClearCustomer = MakeButton("Очистить", 100);
            _btnClearCustomer.Location = new Point(446, 123);
            _btnClearCustomer.Click += delegate { ClearCustomer(); };
            top.Controls.Add(_btnClearCustomer);

            Label lblPhone = MakeLabel("Телефон:");
            lblPhone.Location = new Point(560, 128);
            top.Controls.Add(lblPhone);

            _phoneBox = new PhoneBox();
            _phoneBox.Location = new Point(626, 124);
            _phoneBox.Width = 148;
            _phoneBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_phoneBox);

            // --- третья строка: автомобиль и госномер
            Label lblCar = MakeLabel("Автомобиль:");
            lblCar.Location = new Point(14, 162);
            top.Controls.Add(lblCar);

            _carBox = new TextBox();
            _carBox.Location = new Point(110, 158);
            _carBox.Width = 280;
            _carBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_carBox);

            Label lblPlate = MakeLabel("Госномер:");
            lblPlate.Location = new Point(560, 162);
            top.Controls.Add(lblPlate);

            _plateBox = new TextBox();
            _plateBox.Location = new Point(626, 158);
            _plateBox.Width = 148;
            _plateBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_plateBox);
        }

        /// <summary>Поиск по ФИО, телефону, автомобилю или номеру.</summary>
        private List<Customer> CustomerSearch(string query)
        {
            List<Customer> found = new List<Customer>();

            try
            {
                found = Customers().Search(query);
            }
            catch (Exception ex)
            {
                // справочник может быть недоступен: показываем причину, но не падаем
                SetStatus("Справочник заказчиков недоступен: " + ex.Message);
            }

            return found;
        }

        /// <summary>Выбор заказчика из справочника: поиск по ФИО или телефону.</summary>
        private void PickCustomer()
        {
            Dictionary<string, Customer> byCaption = new Dictionary<string, Customer>();
            List<string> captions = new List<string>();

            foreach (Customer customer in CustomerSearch(_customerPick.Text))
            {
                captions.Add(customer.Caption);
                byCaption[customer.Caption] = customer;
            }

            if (captions.Count == 0)
            {
                MessageBox.Show(this,
                    "В справочнике нет подходящих заказчиков.\\n\\n" +
                    "Откройте «Данные…» → «Заказчики…», чтобы завести карточку, " +
                    "или просто введите заказчика строкой.",
                    "Выбор заказчика", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string chosen;

            using (PickForm pick = new PickForm("Поиск заказчика", captions))
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                chosen = pick.Selected;
            }

            Customer selected;
            if (!byCaption.TryGetValue(chosen, out selected)) return;

            ApplyCustomer(selected);
        }

        /// <summary>Подстановка данных выбранного заказчика в реквизиты.</summary>
        private void ApplyCustomer(Customer customer)
        {
            if (customer == null) return;

            _restoring = true;
            try
            {
                _customerId = customer.Id;
                _fields.Customer = customer.Name;
                _fields.CustomerPhone = customer.Phone;
                _fields.Car = customer.Car;
                _fields.Plate = customer.Plate;

                _customerPick.Text = customer.Name;
                _phoneBox.Text = customer.Phone;
                _carBox.Text = customer.Car;
                _plateBox.Text = customer.Plate;

                _customerPick.SelectionStart = customer.Name.Length;
            }
            finally
            {
                _restoring = false;
            }

            SaveSettings();
            SetStatus("Выбран заказчик: " + customer.Caption);
        }

        /// <summary>Очистка выбранного заказчика: поле, телефон, автомобиль и номер.</summary>
        private void ClearCustomer()
        {
            _restoring = true;
            try
            {
                _customerId = "";
                _fields.Customer = "";
                _fields.CustomerPhone = "";
                _fields.Car = "";
                _fields.Plate = "";

                _customerPick.Text = "";
                _phoneBox.Text = "";
                _carBox.Text = "";
                _plateBox.Text = "";
            }
            finally
            {
                _restoring = false;
            }

            SaveSettings();
            _customerPick.Focus();

            SetStatus("Заказчик очищен: можно выбрать другого или ввести вручную.");
        }

        /// <summary>Оператор вводит заказчика строкой.</summary>
        private void CustomerTyped(object sender, EventArgs e)
        {
            if (_restoring) return;

            _fields.Customer = _customerPick.Text;
            _customerId = "";
        }

        private void CustomerFieldsChanged()
        {
            if (_restoring) return;

            _fields.CustomerPhone = PhoneMask.Format(_phoneBox.Text);
            _fields.Car = _carBox.Text.Trim();
            _fields.Plate = _plateBox.Text.Trim();
        }

        /// <summary>Показ заказчика и его данных в реквизитах.</summary>
        private void RestoreCustomerFields()
        {
            if (_customerPick == null) return;

            _restoring = true;
            try
            {
                _customerPick.Text = _fields.Customer;
                _phoneBox.Text = _fields.CustomerPhone;
                _carBox.Text = _fields.Car;
                _plateBox.Text = _fields.Plate;
            }
            finally
            {
                _restoring = false;
            }

        }

        // ---------------------------------------------------- остатки склада

        /// <summary>Колонка остатков: показывать или скрыть.</summary>
        private void ToggleStockColumn()
        {
            _showStock = !_showStock;

            if (_colStock != null) _colStock.Visible = _showStock;

            if (_btnStock != null)
            {
                _btnStock.FlatStyle = _showStock ? FlatStyle.Standard : FlatStyle.System;
                _btnStock.Font = new Font("Segoe UI", 9.75f,
                    _showStock ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                _btnStock.Text = _showStock ? "Остатки показаны" : "Показывать остатки";
            }

            RefreshStockValues();

            SetStatus(_showStock
                ? "Показаны остатки склада в таблице. Повторное нажатие скрывает колонку."
                : "Колонка остатков скрыта.");
        }

        /// <summary>Обновление значений в колонке остатков.</summary>
        private void RefreshStockValues()
        {
            if (_colStock == null || !_colStock.Visible) return;

            Warehouse warehouse = Warehouse();

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                EstimateRow row = gridRow.Tag as EstimateRow;

                if (row == null)
                {
                    gridRow.Cells[ColStock].Value = "";
                    continue;
                }

                decimal stock = warehouse.Stock(row.Item.Article, row.Item.Name);
                gridRow.Cells[ColStock].Value = Fmt.Qty(stock);

                // чего нет на складе или мало — показываем цветом
                if (stock <= 0m)
                    gridRow.Cells[ColStock].Style.ForeColor = Color.FromArgb(170, 60, 40);
                else if (row.Item.MinStock > 0m && stock < row.Item.MinStock)
                    gridRow.Cells[ColStock].Style.ForeColor = Color.FromArgb(150, 95, 0);
                else
                    gridRow.Cells[ColStock].Style.ForeColor = Color.FromArgb(40, 70, 40);
            }
        }
    }
}
