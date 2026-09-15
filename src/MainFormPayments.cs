// ---------------------------------------------------------------------------
//  Оплата заявки, кассы и панель показателей в главном окне.
//
//  При открытии формы оплаты заявка НЕ сохраняется: программа только подбирает
//  номер. Запись в архив происходит после подтверждения оплаты.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed partial class MainForm
    {
        private CashBook _cash;                  // кассы, оплаты и операции
        private bool _cashLoaded;

        /// <summary>Чтение касс и оплат из выбранного хранилища.</summary>
        private CashBook Cash()
        {
            if (!_cashLoaded || _cash == null)
            {
                try
                {
                    _cash = ConnectionSettings.CashBook.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось прочитать кассы и оплаты:\n" + ex.Message,
                        "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _cash = new CashBook();
                }

                _cashLoaded = true;
            }

            return _cash;
        }

        /// <summary>Запись касс и оплат в выбранное хранилище.</summary>
        private void SaveCash()
        {
            try
            {
                ConnectionSettings.CashBook.Save(_cash);
                _cashLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить кассы и оплаты:\n" + ex.Message,
                    "Кассы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------- оплата

        /// <summary>
        /// Оплата текущей заявки. Заявка записывается в архив только после того,
        /// как оплата подтверждена в окне оплаты.
        /// </summary>
        private void RegisterPayment()
        {
            if (CountPicked() == 0)
            {
                MessageBox.Show(this,
                    "Отметьте услуги — оплата записывается по заявке с позициями.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CashBook cash = Cash();

            // номер подбираем заранее, чтобы показать его в окне оплаты,
            // но заявку при этом не сохраняем
            string number;

            try
            {
                string wanted = _fields.Number.Trim();
                number = UniqueEstimateNumber(wanted.Length > 0 ? wanted : NextEstimateNumber());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось определить номер заявки:\n" + ex.Message,
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Payment existing = cash.FindPayment(number);

            decimal due = 0m;
            foreach (EstimateRow row in _rows)
                if (row.Selected && row.Quantity > 0m) due += row.Sum;

            if (due <= 0m)
            {
                MessageBox.Show(this,
                    "Не отмечено ни одной услуги, поэтому сумму заявки определить нельзя.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Payment result;

            using (PaymentForm dialog = new PaymentForm(cash, number, _fields.Customer, due, existing))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                result = dialog.Result;
            }

            // оплата подтверждена: сначала сохраняем заявку, затем записываем оплату
            SavedEstimate estimate;

            try
            {
                _fields.Number = number;
                estimate = SaveCurrentEstimate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Оплата не записана: не удалось сохранить заявку.\n\n" + ex.Message,
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (estimate == null)
            {
                MessageBox.Show(this, "Оплата не записана: заявка пуста.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // номер мог измениться, если его успели занять
            result.Number = estimate.Number;
            cash.AddPayment(result);
            SaveCash();

            string message = "Оплата по заявке № " + estimate.Number + ": " +
                             PaymentKinds.Title(result.Kind) + " " + Fmt.Money(result.Total) +
                             "   •   касса: " + result.DeskTitle;

            if (result.Remaining > 0m)
                message += "   •   осталось доплатить: " + Fmt.Money(result.Remaining);

            message += "   •   заявка сохранена, позиций: " + estimate.Items.Count;

            // после оплаты заявка закрыта: отметки снимаются, программа готова к следующей
            StartNewEstimate(false);

            SetStatus(message + "   •   можно оформлять следующую.");
        }

        // -------------------------------------------------------------- кассы

        /// <summary>Справочник касс с балансами.</summary>
        private void OpenCashDesks()
        {
            _cashLoaded = false;
            CashBook cash = Cash();

            using (CashDeskForm dialog = new CashDeskForm(cash, ConnectionSettings.CashBook))
            {
                dialog.ShowDialog(this);
            }

            _cashLoaded = false;

            decimal total = 0m;
            foreach (CashDesk desk in Cash().Desks) total += _cash.Balance(desk.Name);

            SetStatus("Касс: " + _cash.Desks.Count + "   •   денег во всех кассах: " +
                      Fmt.Money(total) + "   •   оплат: " + _cash.Payments.Count);
        }

        // -------------------------------------------------------- показатели

        /// <summary>Панель показателей.</summary>
        private void OpenDashboard()
        {
            _cashLoaded = false;

            List<SavedEstimate> estimates;

            try
            {
                estimates = ConnectionSettings.Archive.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось прочитать список заявок:\n" + ex.Message,
                    "Показатели", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                estimates = new List<SavedEstimate>();
            }

            using (DashboardForm dialog = new DashboardForm(Cash(), estimates))
            {
                dialog.ShowDialog(this);
            }
        }
    }
}
