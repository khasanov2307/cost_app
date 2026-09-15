// Проверка окна оплаты без показа: что записывается при смешанной оплате.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KotovCalc;

internal static class PaymentProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, Hidden).GetValue(target);
    }

    private static void Set(object form, string field, string value)
    {
        ((TextBox)Field(form, field)).Text = value;
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string store = Path.Combine(Path.GetTempPath(), "kotov-payprobe");
        if (Directory.Exists(store)) Directory.Delete(store, true);
        Directory.CreateDirectory(store);
        PriceBook.StorePath = Path.Combine(store, "prices.xml");
        PriceBook.Save(PriceBook.ReadSeed());

        CashBook cash = new CashBook();
        cash.Ensure("Основная касса");
        cash.Ensure("Касса мастеров");
        cash.Ensure("Расчётный счёт");

        int problems = 0;

        // --- смешанная оплата в две кассы
        PaymentForm form = new PaymentForm(cash, "1/2026", "ООО Ромашка", 4500m, null);
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        ((ComboBox)Field(form, "_kind")).SelectedIndex = (int)PaymentKind.Mixed;
        Set(form, "_cashBox", "1500");
        Set(form, "_cashlessBox", "3000");
        ((ComboBox)Field(form, "_cashDeskList")).SelectedItem = "Основная касса";
        ((ComboBox)Field(form, "_cashlessDeskList")).SelectedItem = "Расчётный счёт";
        typeof(PaymentForm).GetMethod("UpdateSummary", Hidden).Invoke(form, null);

        string summary = ((Label)Field(form, "_summary")).Text.Replace(Environment.NewLine, " | ");
        Console.WriteLine("подпись: " + summary);

        if (!summary.Contains("Основная касса") || !summary.Contains("Расчётный счёт"))
        {
            Console.WriteLine("  ОШИБКА: в подписи нет выбранных касс");
            problems++;
        }

        Console.WriteLine("кнопка записи доступна: " + ((Button)Field(form, "_ok")).Enabled);

        typeof(PaymentForm).GetMethod("Accept", Hidden).Invoke(form, null);
        Payment result = form.Result;

        if (result == null)
        {
            Console.WriteLine("  ОШИБКА: оплата не сформирована");
            problems++;
        }
        else
        {
            Console.WriteLine("способ: " + PaymentKinds.Title(result.Kind));
            Console.WriteLine("касса наличной части: [" + result.CashDeskName + "]");
            Console.WriteLine("касса безналичной части: [" + result.CashlessDeskName + "]");
            Console.WriteLine("подпись касс: " + result.DeskTitle);
            Console.WriteLine("наличные: " + result.Cash + ", безналичные: " + result.Cashless);

            if (result.CashDeskName != "Основная касса") { Console.WriteLine("  ОШИБКА: касса наличных не та"); problems++; }
            if (result.CashlessDeskName != "Расчётный счёт") { Console.WriteLine("  ОШИБКА: касса безналичных не та"); problems++; }
        }

        form.Dispose();

        // --- попытка записать больше суммы заявки: кнопка должна быть недоступна
        PaymentForm over = new PaymentForm(cash, "2/2026", "ООО Ромашка", 1000m, null);
        { Form shown = over; shown.ShowInTaskbar = false; shown.Opacity = 0; shown.Show(); Application.DoEvents(); }
        Set(over, "_cashBox", "1500");
        typeof(PaymentForm).GetMethod("UpdateSummary", Hidden).Invoke(over, null);
        bool blocked = !((Button)Field(over, "_ok")).Enabled;
        Console.WriteLine("переплата запрещена: " + blocked);
        if (!blocked) { Console.WriteLine("  ОШИБКА: переплата не запрещена"); problems++; }
        over.Dispose();

        // --- наличные и безналичные одной суммой: касса для безналичных скрыта
        PaymentForm one = new PaymentForm(cash, "3/2026", "ООО Ромашка", 2000m, null);
        { Form shown = one; shown.ShowInTaskbar = false; shown.Opacity = 0; shown.Show(); Application.DoEvents(); }
        ((ComboBox)Field(one, "_kind")).SelectedIndex = (int)PaymentKind.Cash;
        Set(one, "_cashBox", "2000");
        typeof(PaymentForm).GetMethod("UpdateSummary", Hidden).Invoke(one, null);

        bool cashlessHidden = !((ComboBox)Field(one, "_cashlessDeskList")).Visible;
        bool cashVisible = ((ComboBox)Field(one, "_cashDeskList")).Visible;
        Console.WriteLine("при наличных: поле кассы видно " + cashVisible +
                          ", поле кассы для безналичных видно " + !cashlessHidden);

        if (!cashVisible || !cashlessHidden) { Console.WriteLine("  ОШИБКА: поля касс показаны неверно"); problems++; }

        typeof(PaymentForm).GetMethod("Accept", Hidden).Invoke(one, null);
        if (one.Result == null) { Console.WriteLine("  ОШИБКА: оплата наличными не сформирована"); problems++; }
        else
        {
            Console.WriteLine("наличные: касса [" + one.Result.CashDeskName + "], безналичная часть пуста: " +
                              (one.Result.Cashless == 0m));
            if (one.Result.Cashless != 0m || one.Result.CashDeskName.Length == 0) { problems++; }
        }
        one.Dispose();

        Directory.Delete(store, true);

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ОКНО ОПЛАТЫ ПРОВЕРЕНО БЕЗ ЗАМЕЧАНИЙ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}
