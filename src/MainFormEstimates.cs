// ---------------------------------------------------------------------------
//  Сохранение заявки и открытие сохранённой заявки.
//
//  Номер присваивается автоматически и нумеруется в рамках года,
//  дата — дата сохранения. Открытая заявока восстанавливает отметки,
//  количества и цены позиций каталога.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed partial class MainForm
    {
        // ------------------------------------------------ сохранение заявки

        /// <summary>Следующий номер заявки в рамках текущего года.</summary>
        private string NextEstimateNumber()
        {
            int year = DateTime.Now.Year;

            SqlEstimateArchive sql = ConnectionSettings.Archive as SqlEstimateArchive;
            if (sql != null) return sql.NextNumber(year);

            return EstimateNumbering.Next(ConnectionSettings.Archive.Load(), year);
        }

        /// <summary>Сохранение текущей заявки в архив.</summary>
        private void SaveEstimate()
        {
            if (CountPicked() == 0)
            {
                MessageBox.Show(this, "Отметьте услуги — тогда заявку можно сохранить.",
                    "Сохранение заявки", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string number;
            try
            {
                number = NextEstimateNumber();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось определить номер заявки:\n" + ex.Message,
                    "Сохранение заявки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DateTime saved = DateTime.Now;
            string customer = _fields.Customer;

            DialogResult answer = MessageBox.Show(this,
                "Сохранить заявку?\n\n" +
                "Номер: " + number + "  (нумерация в рамках " + saved.Year + " года)\n" +
                "Дата: " + saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru) +
                (customer.Length > 0 ? "\nЗаказчик: " + customer : "") + "\n" +
                "Позиций: " + CountPicked() + "   •   скидка: " + Fmt.Qty(_fields.Discount) + " %",
                "Сохранение заявки", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (answer != DialogResult.OK) return;

            SavedEstimate estimate = BuildEstimate(number, saved);


            try
            {
                ConnectionSettings.Archive.Save(estimate);

                // номер заявки попадает в реквизиты документа
                _fields.Number = number;
                _restoring = true;
                try { _numberBox.Text = number; }
                finally { _restoring = false; }
                SaveSettings();

                SetStatus("Заявка сохранена: № " + number + " от " +
                          saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru) +
                          "   •   позиций: " + estimate.Items.Count +
                          "   •   на сумму: " + Fmt.Money(estimate.Total) + " \u20BD");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить заявку:\n" + ex.Message,
                    "Сохранение заявки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------- открытие заявки

        /// <summary>
        /// Новая заявка: снимаются все отметки, количества и правки цен,
        /// номер и заказчик очищаются. Так программа выглядит при запуске.
        /// </summary>
        /// <summary>
        /// Новая заявка: снимаются отметки, количества и правки цен.
        /// clearFields — очищать ли номер, заказчика и скидку (при запуске — да).
        /// </summary>
        private void StartNewEstimate(bool clearFields)
        {
            _restoring = true;
            try
            {
                foreach (EstimateRow row in _rows)
                {
                    row.Selected = false;
                    row.Quantity = 1m;
                    row.PriceOverride = null;
                }

                if (clearFields)
                {
                    _fields.Number = "";
                    _fields.Customer = "";
                    _fields.Discount = 0m;
                    _fields.Normalize();

                    if (_numberBox != null) _numberBox.Text = "";
                    if (_customerBox != null) _customerBox.Text = "";
                    if (_discountBox != null) _discountBox.Value = 0m;
                }
            }
            finally
            {
                _restoring = false;
            }

            SaveSettings();
            RestyleRows();
            Recalculate();

            SetStatus("Новая заявка. Номер присвоится автоматически при сохранении.");
        }

        /// <summary>Сборка заявки по текущим отметкам: номер, дата, заказчик, позиции.</summary>
        private SavedEstimate BuildEstimate(string number, DateTime saved)
        {
            SavedEstimate estimate = new SavedEstimate();
            estimate.Number = number;
            estimate.Saved = saved;
            estimate.Customer = _fields.Customer;
            estimate.Discount = AppSettings.ClampDiscount(_fields.Discount);

            int sequence, year;
            if (EstimateNumbering.Parse(number, out sequence, out year))
            {
                estimate.Sequence = sequence;
                estimate.Year = year;
            }
            else
            {
                estimate.Sequence = 1;
                estimate.Year = saved.Year;
            }

            foreach (EstimateRow row in _rows)
            {
                if (!row.Selected || row.Quantity <= 0m) continue;

                EstimateItem item = new EstimateItem();
                item.Group = row.Item.Group;
                item.Article = row.Item.Article;
                item.Name = row.Item.Name;
                item.Unit = row.Item.Unit;
                item.Quantity = row.Quantity;
                item.Price = row.Price;
                estimate.Items.Add(item);
            }

            return estimate;
        }

        /// <summary>
        /// Сохранение текущей заявки без вопросов: используется при оплате.
        /// Возвращает сохранённую заявку или null.
        /// </summary>
        private SavedEstimate SaveCurrentEstimate()
        {
            if (CountPicked() == 0) return null;

            string number;

            // если заявка уже открыта или сохранена, номер не меняем
            if (_fields.Number.Trim().Length > 0 &&
                ConnectionSettings.Archive.Load().FindIndex(delegate(SavedEstimate saved)
                {
                    return string.Equals(saved.Number, _fields.Number.Trim(), StringComparison.CurrentCultureIgnoreCase);
                }) >= 0)
            {
                number = _fields.Number.Trim();
            }
            else
            {
                number = NextEstimateNumber();
            }

            SavedEstimate estimate = BuildEstimate(number, DateTime.Now);
            ConnectionSettings.Archive.Save(estimate);

            _fields.Number = number;
            _restoring = true;
            try { _numberBox.Text = number; }
            finally { _restoring = false; }
            SaveSettings();

            return estimate;
        }

        /// <summary>Список сохранённых заявок и открытие выбранной.</summary>
        private void OpenEstimate()
        {
            List<SavedEstimate> saved;

            try
            {
                saved = ConnectionSettings.Archive.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось прочитать список заявок:\n" + ex.Message,
                    "Открытие заявки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            while (true)
            {
                using (EstimateListForm dialog = new EstimateListForm(saved, ConnectionSettings.Archive.Title, Cash()))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;

                    if (dialog.DeleteRequested)
                    {
                        if (MessageBox.Show(this, "Удалить заявку № " + dialog.Selected.Number + "?",
                                "Удаление заявки", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                            continue;

                        try
                        {
                            ConnectionSettings.Archive.Delete(dialog.Selected);
                            saved.Remove(dialog.Selected);
                            SetStatus("Заявка № " + dialog.Selected.Number + " удалена.");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, "Не удалось удалить заявку:\n" + ex.Message,
                                "Удаление заявки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }

                        continue;
                    }

                    ApplyEstimate(dialog.Selected);
                    return;
                }
            }
        }

        /// <summary>Восстановление отметок и количеств из сохранённой заявки.</summary>
        private void ApplyEstimate(SavedEstimate estimate)
        {
            if (estimate == null) return;

            List<string> missing = new List<string>();

            _restoring = true;
            try
            {
                // сначала снимаем все отметки
                foreach (EstimateRow row in _rows)
                {
                    row.Selected = false;
                    row.Quantity = 1m;
                    row.PriceOverride = null;
                }


                foreach (EstimateItem item in estimate.Items)
                {
                    EstimateRow target = FindRowFor(item);

                    if (target == null)
                    {
                        missing.Add(item.Name);
                        continue;
                    }

                    target.Selected = true;
                    target.Quantity = item.Quantity;
                    target.PriceOverride = item.Price == target.Item.Price ? (decimal?)null : item.Price;
                }

                _fields.Number = estimate.Number;
                if (estimate.Customer.Length > 0) _fields.Customer = estimate.Customer;
                _fields.Discount = AppSettings.ClampDiscount(estimate.Discount);
                _fields.Normalize();

                _numberBox.Text = _fields.Number;
                _customerBox.Text = _fields.Customer;
                _discountBox.Value = _fields.Discount;
            }
            finally
            {
                _restoring = false;
            }

            SaveSettings();
            RestyleRows();
            Recalculate();

            string message = "Открыта заявка № " + estimate.Number + " от " +
                             estimate.Saved.ToString("dd.MM.yyyy HH:mm", Fmt.Ru) +
                             "   •   позиций: " + estimate.Items.Count +
                             "   •   на сумму: " + Fmt.Money(estimate.Total) + " \u20BD";

            if (missing.Count > 0)
                message += "   •   не найдено в каталоге: " + missing.Count;

            SetStatus(message);

            if (missing.Count > 0)
            {
                MessageBox.Show(this,
                    "В заявке есть позиции, которых больше нет в каталоге номенклатуры: " +
                    missing.Count + ".\n\n" + Shorten(string.Join(", ", missing.ToArray()), 400),
                    "Открытие заявки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Поиск позиции каталога для позиции сохранённой заявки.</summary>
        private EstimateRow FindRowFor(EstimateItem item)
        {
            EstimateRow byName = null;

            foreach (EstimateRow row in _rows)
            {
                if (!string.IsNullOrEmpty(item.Article) && !string.IsNullOrEmpty(row.Item.Article) &&
                    string.Equals(row.Item.Article, item.Article, StringComparison.CurrentCultureIgnoreCase))
                    return row;

                if (byName == null &&
                    string.Equals(row.Item.Name, item.Name, StringComparison.CurrentCultureIgnoreCase))
                    byName = row;
            }

            return byName;
        }
    }
}
