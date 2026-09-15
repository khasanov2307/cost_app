// Снимки окон оплаты, касс и показателей — наглядная проверка.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using KotovCalc;

internal static class RenderForms
{
    private const System.Reflection.BindingFlags Hidden =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, Hidden).GetValue(target);
    }

    private static void Save(Form form, string output)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Show();
        Application.DoEvents();
        form.Refresh();
        Application.DoEvents();

        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(output, ImageFormat.Png);
        }

        Console.WriteLine("saved: " + Path.GetFullPath(output));
        form.Close();
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string mode = args.Length > 0 ? args[0] : "payment";
        string output = args.Length > 1 ? args[1] : "form.png";

        string store = Path.Combine(Path.GetTempPath(), "kotov-forms");
        if (Directory.Exists(store)) Directory.Delete(store, true);
        Directory.CreateDirectory(store);
        PriceBook.StorePath = Path.Combine(store, "prices.xml");
        PriceBook.Save(PriceBook.ReadSeed());

        CashBook cash = new CashBook();
        cash.Ensure("Основная касса");
        cash.Ensure("Касса мастеров");
        cash.Ensure("Расчётный счёт");
        cash.Find("Основная касса").Note = "наличные на ресепшене";

        if (mode == "desks")
        {
            using (CashDeskForm form = new CashDeskForm(cash, new FileCashBook()))
                Save(form, output);
            return;
        }

        if (mode == "dashboard")
        {
            Payment payment = new Payment();
            payment.Number = "1/2026";
            payment.Customer = "ООО Ромашка";
            payment.Desk = "Расчётный счёт";
            payment.CashDesk = "Основная касса";
            payment.Kind = PaymentKind.Mixed;
            payment.Cash = 1500m;
            payment.Cashless = 3000m;
            payment.Due = 4500m;
            payment.Saved = DateTime.Now;
            cash.AddPayment(payment);

            DeskOperation income = new DeskOperation();
            income.Desk = "Основная касса";
            income.Amount = 10000m;
            income.Saved = DateTime.Now;
            income.Note = "размен";
            cash.Operations.Add(income);

            List<SavedEstimate> estimates = new List<SavedEstimate>();
            string[] names = new string[]
            {
                "Компьютерная диагностика двигателя", "Замена моторного масла и масляного фильтра",
                "Диагностика ходовой части", "Замена свечей зажигания (4 шт.)", "Промывка форсунок на стенде"
            };

            for (int i = 0; i < 6; i++)
            {
                SavedEstimate estimate = new SavedEstimate();
                estimate.Number = (i + 1) + "/2026";
                estimate.Year = 2026;
                estimate.Sequence = i + 1;
                estimate.Saved = DateTime.Now.AddDays(-i);
                estimate.Customer = "Заказчик " + (i + 1);
                estimate.Discount = i == 0 ? 5m : 0m;

                for (int k = 0; k <= i && k < names.Length; k++)
                {
                    EstimateItem item = new EstimateItem();
                    item.Group = "Диагностика";
                    item.Name = names[k];
                    item.Unit = "услуга";
                    item.Quantity = 1m + (k % 3);
                    item.Price = 1500m - k * 100m;
                    estimate.Items.Add(item);
                }

                estimates.Add(estimate);
            }

            using (DashboardForm form = new DashboardForm(cash, estimates))
                Save(form, output);
            return;
        }

        // по умолчанию — окно оплаты при смешанной оплате
        Payment existing = null;
        decimal due = 4500m;

        using (PaymentForm form = new PaymentForm(cash, "1/2026", "ООО Ромашка", due, existing))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(0, 0);
            form.Show();
            Application.DoEvents();

            ComboBox kind = (ComboBox)Field(form, "_kind");
            TextBox cashBox = (TextBox)Field(form, "_cashBox");
            TextBox cashlessBox = (TextBox)Field(form, "_cashlessBox");
            ComboBox cashDesk = (ComboBox)Field(form, "_cashDeskList");
            ComboBox cashlessDesk = (ComboBox)Field(form, "_cashlessDeskList");

            kind.SelectedIndex = (int)PaymentKind.Mixed;
            cashBox.Text = "1500";
            cashlessBox.Text = "3000";
            cashDesk.SelectedItem = "Основная касса";
            cashlessDesk.SelectedItem = "Расчётный счёт";

            // подпись пересчитывается при любом изменении; в снимке вызываем явно
            typeof(PaymentForm).GetMethod("UpdateSummary", Hidden).Invoke(form, null);

            // проверяем, что запись оплаты разложит части по выбранным кассам
            typeof(PaymentForm).GetMethod("Accept", Hidden).Invoke(form, null);

            Application.DoEvents();
            form.Refresh();
            Application.DoEvents();

            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(output, ImageFormat.Png);
            }

            Console.WriteLine("saved: " + Path.GetFullPath(output));
            Console.WriteLine("итог: " + ((Label)Field(form, "_summary")).Text.Replace(Environment.NewLine, " | "));
            Console.WriteLine("запись: " + ((Label)Field(form, "_summary")).Text.Replace(Environment.NewLine, " | "));
            form.Close();
        }
    }
}

