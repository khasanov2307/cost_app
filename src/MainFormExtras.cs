// ---------------------------------------------------------------------------
//  Заказчики, склад, чек об оплате, копирование прошлой заявки
//  и горячие клавиши в главном окне.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed partial class MainForm
    {
        private CustomerBook _customers;
        private bool _customersLoaded;
        private Warehouse _warehouse;
        private bool _warehouseLoaded;

        // --------------------------------------------------------- заказчики

        /// <summary>Справочник заказчиков.</summary>
        private CustomerBook Customers()
        {
            if (!_customersLoaded || _customers == null)
            {
                try
                {
                    _customers = ConnectionSettings.Customers.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось прочитать справочник заказчиков:\n" + ex.Message,
                        "Заказчики", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _customers = new CustomerBook();
                }

                _customersLoaded = true;
            }

            return _customers;
        }

        /// <summary>Окно справочника заказчиков.</summary>
        private void OpenCustomers()
        {
            _customersLoaded = false;
            CustomerBook book = Customers();

            List<SavedEstimate> estimates;

            try
            {
                estimates = ConnectionSettings.Archive.Load();
            }
            catch { estimates = new List<SavedEstimate>(); }

            using (CustomerForm dialog = new CustomerForm(book, ConnectionSettings.Customers,
                                                          estimates, Cash()))
            {
                dialog.ShowDialog(this);
            }

            _customersLoaded = false;
            SetStatus("Заказчиков в справочнике: " + Customers().Customers.Count);
        }

        // -------------------------------------------------------------- склад

        /// <summary>Склад: остатки и движения.</summary>
        private Warehouse Warehouse()
        {
            if (!_warehouseLoaded || _warehouse == null)
            {
                try
                {
                    _warehouse = ConnectionSettings.Warehouse.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось прочитать склад:\n" + ex.Message,
                        "Склад", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _warehouse = new Warehouse();
                }

                _warehouseLoaded = true;
            }

            return _warehouse;
        }

        /// <summary>Окно склада.</summary>
        private void OpenWarehouse()
        {
            _warehouseLoaded = false;
            Warehouse warehouse = Warehouse();

            using (WarehouseForm dialog = new WarehouseForm(warehouse, ConnectionSettings.Warehouse, _catalog))
            {
                dialog.ShowDialog(this);
            }

            _warehouseLoaded = false;

            decimal value = 0m;
            foreach (ServiceItem item in _catalog)
                value += Warehouse().Stock(item.Article, item.Name) *
                         (item.Cost > 0m ? item.Cost : Warehouse().AverageCost(item.Article, item.Name));

            SetStatus("Склад: движений " + Warehouse().Moves.Count +
                      "   •   на сумму " + Fmt.Money(value));
        }

        /// <summary>Списание позиций заявки со склада.</summary>
        private void WriteOffEstimate(SavedEstimate estimate)
        {
            if (estimate == null) return;

            Warehouse warehouse = Warehouse();

            if (warehouse.HasWriting(estimate.Number))
            {
                MessageBox.Show(this, "По заявке № " + estimate.Number + " уже списано со склада.",
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> missing = new List<string>();

            foreach (EstimateItem item in estimate.Items)
            {
                decimal stock = warehouse.Stock(item.Article, item.Name);
                if (stock < item.Quantity) missing.Add(item.Name + " (" + Fmt.Qty(stock) + ")");
            }

            string question = "Списать со склада позиции заявки № " + estimate.Number + "?";

            if (missing.Count > 0)
                question += "\n\nНе хватает по позициям: " + missing.Count +
                            "\n" + string.Join(", ", missing.ToArray());

            if (MessageBox.Show(this, question, "Склад",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            warehouse.WriteOff(estimate);

            try
            {
                ConnectionSettings.Warehouse.Save(warehouse);
                SetStatus("Со склада списано позиций: " + estimate.Items.Count +
                          " по заявке № " + estimate.Number);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить склад:\n" + ex.Message,
                    "Склад", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // --------------------------------------------------------------- чек

        /// <summary>Чек об оплате: показать и при желании сохранить файлом.</summary>
        private void ShowReceipt(string number)
        {
            CashBook cash = Cash();
            Payment payment = cash.FindPayment(number);

            if (payment == null)
            {
                MessageBox.Show(this, "По заявке № " + number + " оплата не зафиксирована.",
                    "Чек", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Receipt receipt = new Receipt();
            receipt.Number = number;
            receipt.Saved = payment.Saved;
            receipt.Customer = payment.Customer;
            receipt.Cashier = ConnectionSettings.User.Length > 0 ? ConnectionSettings.User : "";
            receipt.Payment = payment;

            string text = receipt.ToText(42);

            using (TextForm dialog = new TextForm("Чек по заявке № " + number, text,
                                                  receipt.FileName, PriceBook.StoreFolder))
            {
                dialog.ShowDialog(this);
            }

            SetStatus("Чек по заявке № " + number + " готов.");
        }

        // ------------------------------------------------- прошлая заявка

        /// <summary>Повторить прошлую заявку: отметки, количества и цены.</summary>
        private void CopyLastEstimate()
        {
            List<SavedEstimate> saved;

            try
            {
                saved = ConnectionSettings.Archive.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось прочитать список заявок:\n" + ex.Message,
                    "Копирование заявки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (saved.Count == 0)
            {
                MessageBox.Show(this, "Сохранённых заявок пока нет — копировать нечего.",
                    "Копирование заявки", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> captions = new List<string>();
            foreach (SavedEstimate estimate in saved) captions.Add(estimate.Caption);

            string chosen;

            using (PickForm pick = new PickForm("Какую заявку повторить", captions))
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                chosen = pick.Selected;
            }

            SavedEstimate source = null;
            foreach (SavedEstimate estimate in saved)
                if (estimate.Caption == chosen) { source = estimate; break; }

            if (source == null) return;

            // заявка переносится как новая: номер присвоится при сохранении
            StartNewEstimate(false, true);
            ApplyEstimate(source, false);

            _fields.Number = "";
            _restoring = true;
            try { _numberBox.Text = ""; }
            finally { _restoring = false; }
            SaveSettings();
            Recalculate();

            SetStatus("Повторена заявка № " + source.Number + ": позиций " + source.Items.Count +
                      ". Номер присвоится при сохранении.");
        }

        // --------------------------------------------------- горячие клавиши

        /// <summary>Сочетания клавиш для частых действий.</summary>
        private void ApplyHotKeys()
        {
            KeyPreview = true;

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                // Ctrl+O — оплата, Ctrl+S — сохранить, Ctrl+E — показатели
                if (e.Control && e.KeyCode == Keys.O) { RegisterPayment(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.S) { SaveEstimate(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.E) { OpenDashboard(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.W) { OpenWarehouse(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.K) { OpenCustomers(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.R) { CopyLastEstimate(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.L) { ToggleOnlySelected(); e.Handled = true; return; }
                if (e.Control && e.KeyCode == Keys.F) { _find.Focus(); _find.SelectAll(); e.Handled = true; return; }

                // F2 — оплата, F3 — открыть заявку, F4 — каталог, F5 — обновить
                if (e.KeyCode == Keys.F2) { RegisterPayment(); e.Handled = true; return; }
                if (e.KeyCode == Keys.F3) { OpenEstimate(); e.Handled = true; return; }
                if (e.KeyCode == Keys.F4) { OpenCatalogEditor(); e.Handled = true; return; }
                if (e.KeyCode == Keys.F5) { ReloadPricesPreserving(); e.Handled = true; return; }
            };
        }

        /// <summary>Справка по сочетаниям клавиш.</summary>
        private void ShowHotKeys()
        {
            string text =
                "Горячие клавиши" + Environment.NewLine +
                new string('=', 42) + Environment.NewLine + Environment.NewLine +
                "Ctrl+F   поиск по каталогу" + Environment.NewLine +
                "Ctrl+L   показать только выбранные" + Environment.NewLine +
                "Ctrl+S   сохранить заявку" + Environment.NewLine +
                "Ctrl+O   оплата (или F2)" + Environment.NewLine +
                "Ctrl+R   повторить прошлую заявку" + Environment.NewLine +
                "Ctrl+K   справочник заказчиков" + Environment.NewLine +
                "Ctrl+W   склад" + Environment.NewLine +
                "Ctrl+E   показатели" + Environment.NewLine + Environment.NewLine +
                "F2   оплата" + Environment.NewLine +
                "F3   открыть сохранённую заявку" + Environment.NewLine +
                "F4   редактор каталога" + Environment.NewLine +
                "F5   обновить каталог" + Environment.NewLine + Environment.NewLine +
                "В таблице: пробел — отметить позицию, F2 — править цену," + Environment.NewLine +
                "двойной щелчок по строке — отметить, по разделу — свернуть.";

            using (TextForm dialog = new TextForm("Горячие клавиши", text, null, null))
            {
                dialog.ShowDialog(this);
            }
        }
    }
}
