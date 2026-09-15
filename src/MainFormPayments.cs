// ---------------------------------------------------------------------------
//  Оплата заявки, кассы и панель показателей в главном окне.
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

        /// <summary>Фиксация оплаты по текущей заявке.</summary>
        private void RegisterPayment()
        {
            // оплата фиксируется по сохранённой заявке: если её ещё нет, сохраняем сейчас
            if (CountPicked() > 0)
            {
                try
                {
                    SavedEstimate savedNow = SaveCurrentEstimate();

                    if (savedNow != null)
                        SetStatus("Заявка сохранена перед оплатой: № " + savedNow.Number +
                                  "   •   позиций: " + savedNow.Items.Count +
                                  "   •   на сумму: " + Fmt.Money(savedNow.Total));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось сохранить заявку перед оплатой:\n" + ex.Message,
                        "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            string number = _fields.Number.Trim();

            if (number.Length == 0)
            {
                MessageBox.Show(this,
                    "Сначала отметьте услуги: заявка сохранится автоматически, затем её можно оплатить.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CashBook cash = Cash();
            Payment existing = cash.FindPayment(number);

            decimal due = 0m;
            foreach (EstimateRow row in _rows)
                if (row.Selected && row.Quantity > 0m) due += row.Sum;

            if (due <= 0m && existing != null) due = existing.Due;

            if (due <= 0m)
            {
                MessageBox.Show(this,
                    "Не отмечено ни одной услуги, поэтому сумму заявки определить нельзя.",
                    "Оплата", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (PaymentForm dialog = new PaymentForm(cash, number, _fields.Customer, due, existing))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                cash.AddPayment(dialog.Result);
                SaveCash();

                string message = "Оплата по заявке № " + number + ": " +
                                 PaymentKinds.Title(dialog.Result.Kind) + " " +
                                 Fmt.Money(dialog.Result.Total) +
                                 "   •   касса: " + dialog.Result.DeskTitle;

                if (dialog.Result.Remaining > 0m)
                    message += "   •   осталось доплатить: " + Fmt.Money(dialog.Result.Remaining);

                // после оплаты заявка закрыта: отметки снимаются, программа готова к следующей
                StartNewEstimate(false);

                SetStatus(message + "   •   заявка оплачена, можно оформлять следующую.");
            }
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
                      Fmt.Money(total) + " \u20BD   •   оплат: " + _cash.Payments.Count);
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
