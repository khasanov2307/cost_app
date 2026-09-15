// ---------------------------------------------------------------------------
//  Строка реквизитов заявки: номер, статус, скидка, заказчик, телефон,
//  автомобиль и госномер.
//
//  Заказчика можно выбрать из справочника (список с подсказками) или ввести
//  строкой — тогда карточка создаётся при сохранении заявки.
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
        private ComboBox _customerPick;
        private PhoneBox _phoneBox;
        private TextBox _carBox;
        private TextBox _plateBox;
        private Button _btnStock;
        private Button _btnCopyLast;              // повторить прошлую заявку

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

            _customerPick = new ComboBox();
            _customerPick.Location = new Point(110, 124);
            _customerPick.Width = 280;
            _customerPick.DropDownStyle = ComboBoxStyle.DropDown;      // можно выбрать или ввести
            _customerPick.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _customerPick.AutoCompleteSource = AutoCompleteSource.ListItems;
            _customerPick.TextChanged += CustomerTyped;
            _customerPick.SelectedIndexChanged += CustomerPicked;
            _customerPick.DropDown += delegate { RefreshCustomerList(); };
            top.Controls.Add(_customerPick);

            Label lblPhone = MakeLabel("Телефон:");
            lblPhone.Location = new Point(404, 128);
            top.Controls.Add(lblPhone);

            _phoneBox = new PhoneBox();
            _phoneBox.Location = new Point(470, 124);
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
            lblPlate.Location = new Point(404, 162);
            top.Controls.Add(lblPlate);

            _plateBox = new TextBox();
            _plateBox.Location = new Point(470, 158);
            _plateBox.Width = 148;
            _plateBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_plateBox);
        }

        /// <summary>Заполнение списка заказчиков для выбора и подсказок.</summary>
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


        private void RefreshCustomerList()
        {
            if (_customerPick == null) return;

            string typed = _customerPick.Text;

            _customerPick.BeginUpdate();
            try
            {
                _customerPick.Items.Clear();

                string query = _customerPick.Text.Trim();

                // если введены цифры — ищем по телефону, иначе по имени, машине и номеру
                foreach (Customer customer in CustomerSearch(query))
                    _customerPick.Items.Add(customer.Caption);
            }
            finally
            {
                _customerPick.EndUpdate();
            }

            // текст возвращаем после наполнения списка
            _customerPick.Text = typed;
        }

        /// <summary>Оператор выбрал заказчика из списка: заполняем данные.</summary>
        private void CustomerPicked(object sender, EventArgs e)
        {
            // при перезаполнении списка событие приходит с пустым выбором — пропускаем
            if (_restoring) return;
            string caption = Convert.ToString(_customerPick.SelectedItem);
            if (caption.Length == 0) return;

            Customer customer = null;

                foreach (Customer item in CustomerSearch(""))
                if (item.Caption == caption) { customer = item; break; }

            if (customer == null) return;

            _restoring = true;
            try
            {
                _customerId = customer.Id;
                _fields.Customer = customer.Name;
                _fields.CustomerPhone = customer.Phone;

                _phoneBox.Text = customer.Phone;
                _carBox.Text = customer.Car;
                _plateBox.Text = customer.Plate;
            }
            finally
            {
                _restoring = false;
            }

            SaveSettings();
        }

        /// <summary>Оператор вводит заказчика строкой.</summary>
        private void CustomerTyped(object sender, EventArgs e)
        {
            if (_restoring) return;

            _fields.Customer = _customerPick.Text;
            _customerId = "";
        }

        /// <summary>Данные заказчика в заявке: телефон, автомобиль, госномер.</summary>
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

            RefreshCustomerList();
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
