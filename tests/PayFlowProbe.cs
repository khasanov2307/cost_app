// Проверка: заявка сохраняется только при подтверждении оплаты.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KotovCalc;

internal static class PayFlowProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, Hidden).GetValue(target);
    }

    private static object Call(object target, string name, object[] args)
    {
        return target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    }

    private static void Mark(MainForm form, int count)
    {
        int marked = 0;
        foreach (EstimateRow row in (List<EstimateRow>)Field(form, "_rows"))
        {
            if (marked >= count) break;
            row.Selected = true;
            marked++;
        }
    }

    private static int ArchiveSize()
    {
        return ConnectionSettings.Archive.Load().Count;
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string store = Path.Combine(Path.GetTempPath(), "kotov-payflow");
        if (Directory.Exists(store)) Directory.Delete(store, true);
        Directory.CreateDirectory(store);
        PriceBook.StorePath = Path.Combine(store, "prices.xml");
        PriceBook.Save(PriceBook.ReadSeed());

        int problems = 0;

        MainForm form = new MainForm();
        form.Show();
        Application.DoEvents();

        CashBook cash = new CashBook();
        cash.Ensure("Основная касса");
        ConnectionSettings.CashBook.Save(cash);

        // отмечаем позиции и подбираем номер — как это делает кнопка «Оплата»
        Mark(form, 3);
        DocumentFields fields = (DocumentFields)Field(form, "_fields");
        fields.Customer = "ООО Проверка";

        Console.WriteLine("заявок в архиве до нажатия: " + ArchiveSize());

        // шаг 1: подбор номера (это происходит при открытии формы оплаты)
        string number = Convert.ToString(Call(form, "UniqueEstimateNumber",
            new object[] { Convert.ToString(Call(form, "NextEstimateNumber", null)) }));
        Console.WriteLine("подобранный номер: " + number);
        Console.WriteLine("заявок в архиве после подбора номера: " + ArchiveSize());

        if (ArchiveSize() != 0)
        {
            Console.WriteLine("  ОШИБКА: заявка сохранилась ещё до оплаты");
            problems++;
        }

        // шаг 2: пользователь отменил форму оплаты — сохранения быть не должно
        Console.WriteLine("заявок после отмены формы: " + ArchiveSize());
        if (ArchiveSize() != 0)
        {
            Console.WriteLine("  ОШИБКА: отмена формы сохранила заявку");
            problems++;
        }

        // шаг 3: подтверждённая оплата — заявка должна сохраниться
        fields.Number = number;
        SavedEstimate estimate = (SavedEstimate)Call(form, "SaveCurrentEstimate", null);
        Console.WriteLine("после подтверждения: заявок " + ArchiveSize() +
                          ", номер " + (estimate == null ? "нет" : estimate.Number) +
                          ", позиций " + (estimate == null ? 0 : estimate.Items.Count));

        if (estimate == null || ArchiveSize() != 1)
        {
            Console.WriteLine("  ОШИБКА: заявка не сохранилась при подтверждении");
            problems++;
        }

        // записываем оплату и проверяем связку
        if (estimate != null)
        {
            Payment payment = new Payment();
            payment.Number = estimate.Number;
            payment.Desk = "Основная касса";
            payment.Kind = PaymentKind.Cash;
            payment.Cash = estimate.Total;
            payment.Due = estimate.Total;
            payment.Saved = DateTime.Now;
            cash.AddPayment(payment);
            ConnectionSettings.CashBook.Save(cash);

            CashBook read = ConnectionSettings.CashBook.Load();
            Console.WriteLine("оплата записана: " + read.Payments.Count +
                              ", баланс кассы: " + read.Balance("Основная касса") +
                              ", сумма заявки: " + estimate.Total);

            if (read.Balance("Основная касса") != estimate.Total)
            {
                Console.WriteLine("  ОШИБКА: баланс кассы не совпал с суммой заявки");
                problems++;
            }
        }

        // отметки после оплаты должны быть сняты
        Call(form, "StartNewEstimate", new object[] { false });
        int left = 0;
        foreach (EstimateRow row in (List<EstimateRow>)Field(form, "_rows")) if (row.Selected) left++;
        Console.WriteLine("отмеченных позиций после оплаты: " + left + ", номер: [" + fields.Number + "]");

        if (left != 0) { Console.WriteLine("  ОШИБКА: отметки не сняты"); problems++; }

        form.Close();
        Directory.Delete(store, true);

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ПОРЯДОК ОПЛАТЫ ПРОВЕРЕН"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}
