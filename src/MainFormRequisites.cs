// ---------------------------------------------------------------------------
//  Строка реквизитов заявки: статус, заказчик из справочника или вручную,
//  телефон, автомобиль и госномер.
//
//  Заказчика можно выбрать из справочника (список с подсказками) или просто
//  ввести строкой — тогда карточка создаётся при сохранении заявки.
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
        private TextBox _phoneBox;
        private TextBox _carBox;
        private TextBox _plateBox;
        private Button _btnCopyLast;

        private string _customerId = "";

        /// <summary>Строка реквизитов: статус, заказчик, телефон, автомобиль, номер.</summary>
        private void BuildRequisiteRow(Panel top)
        {
            // вторая строка: номер заявки, статус и скидка
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

            // третья строка: заказчик и его данные
            Label lblCustomer = MakeLabel("Заказчик:");
            lblCustomer.Location = new Point(14, 128);
            top.Controls.Add(lblCustomer);

            _customerPick = new ComboBox();
            _customerPick.Location = new Point(110, 124);
            _customerPick.Width = 300;
            _customerPick.DropDownStyle = ComboBoxStyle.DropDown;      // можно и выбрать, и ввести
            _customerPick.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _customerPick.AutoCompleteSource = AutoCompleteSource.ListItems;
            _customerPick.TextChanged += CustomerTyped;
            _customerPick.SelectedIndexChanged += CustomerPicked;
            top.Controls.Add(_customerPick);

            Label lblPhone = MakeLabel("Телефон:");
            lblPhone.Location = new Point(404, 128);
            top.Controls.Add(lblPhone);

            _phoneBox = new TextBox();
            _phoneBox.Location = new Point(470, 124);
            _phoneBox.Width = 150;
            _phoneBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_phoneBox);

            Label lblCar = MakeLabel("Автомобиль:");
            lblCar.Location = new Point(628, 128);
            top.Controls.Add(lblCar);

            _carBox = new TextBox();
            _carBox.Location = new Point(706, 124);
            _carBox.Width = 150;
            _carBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_carBox);

            Label lblPlate = MakeLabel("Госномер:");
            lblPlate.Location = new Point(866, 128);
            top.Controls.Add(lblPlate);

            _plateBox = new TextBox();
            _plateBox.Location = new Point(932, 124);
            _plateBox.Width = 100;
            _plateBox.TextChanged += delegate { CustomerFieldsChanged(); DocumentFields_Changed(this, EventArgs.Empty); };
            top.Controls.Add(_plateBox);

        }

        /// <summary>Заполнение списка заказчиков для подсказок.</summary>
        private void RefreshCustomerList()
        {
            if (_customerPick == null) return;

            string typed = _customerPick.Text;

            _customerPick.BeginUpdate();
            try
            {
                _customerPick.Items.Clear();

                foreach (Customer customer in Customers().Search(""))
                    _customerPick.Items.Add(customer.Caption);
            }
            finally
            {
                _customerPick.EndUpdate();
            }

            _customerPick.Text = typed;
        }

        /// <summary>Оператор выбрал заказчика из списка: заполняем данные.</summary>
        private void CustomerPicked(object sender, EventArgs e)
        {
            string caption = Convert.ToString(_customerPick.SelectedItem);
            if (caption.Length == 0) return;

            Customer customer = null;

            foreach (Customer item in Customers().Customers)
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

        // -------------------------------------------- данные заказчика в заявке

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


        private void CustomerFieldsChanged()
        {
            if (_restoring) return;

            _fields.CustomerPhone = _phoneBox.Text.Trim();
            _fields.Car = _carBox.Text.Trim();
            _fields.Plate = _plateBox.Text.Trim();
        }
    }
}
