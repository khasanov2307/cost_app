using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KotovCalc;

internal static class FilterProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object t, string n) { return t.GetType().GetField(n, Hidden).GetValue(t); }

    private static void Call(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null && m.GetParameters().Length == 0) { m.Invoke(target, null); return; }
        }
        throw new MissingMethodException(name);
    }

    private static void Counts(DataGridView grid, out int groups, out int items)
    {
        groups = 0; items = 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.Visible) continue;
            if (row.Tag is string) groups++;
            else if (row.Tag is EstimateRow) items++;
        }
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string store = Path.Combine(Path.GetTempPath(), "kotov-filter");
        if (Directory.Exists(store)) Directory.Delete(store, true);
        Directory.CreateDirectory(store);
        PriceBook.StorePath = Path.Combine(store, "prices.xml");
        PriceBook.Save(PriceBook.ReadSeed());

        int problems = 0;

        MainForm form = new MainForm();
        form.Show();
        Application.DoEvents();

        DataGridView grid = (DataGridView)Field(form, "_grid");
        Button button = (Button)Field(form, "_btnOnlySelected");

        Console.WriteLine("кнопка: [" + button.Text + "], видна: " + button.Visible +
                          ", положение: " + button.Left + "," + button.Top);

        int groups, items;
        Counts(grid, out groups, out items);
        Console.WriteLine("без фильтра: разделов " + groups + ", позиций " + items);
        int allItems = items;

        // отмечаем три позиции
        int marked = 0;
        foreach (DataGridViewRow gridRow in grid.Rows)
        {
            if (marked >= 3) break;
            EstimateRow row = gridRow.Tag as EstimateRow;
            if (row == null) continue;

            // так же, как нажатие галочки: поле и ячейка сетки вместе
            row.Selected = true;
            gridRow.Cells[0].Value = true;
            marked++;
        }

        Console.WriteLine("отмечено позиций: " + marked);

        // включаем фильтр
        Call(form, "ToggleOnlySelected");
        Application.DoEvents();
        Counts(grid, out groups, out items);

        Console.WriteLine("фильтр включён: разделов " + groups + ", позиций " + items +
                          ", текст кнопки [" + button.Text + "]");

        if (items != 3)
        {
            Console.WriteLine("  ОШИБКА: показано не 3 позиции, а " + items);
            problems++;
        }

        Console.WriteLine("  видимые строки:");
        foreach (DataGridViewRow gridRow in grid.Rows)
        {
            if (!gridRow.Visible) continue;
            EstimateRow row = gridRow.Tag as EstimateRow;
            Console.WriteLine("    " + (row == null ? "[раздел] " : (row.Selected ? "[+] " : "[ ] ")) +
                              Convert.ToString(gridRow.Cells[1].Value) +
                              (row == null ? "" : "   поле Selected=" + row.Selected));
        }

        bool onlyMarked = true;
        foreach (DataGridViewRow gridRow in grid.Rows)
        {
            if (!gridRow.Visible) continue;
            EstimateRow row = gridRow.Tag as EstimateRow;
            if (row != null && !row.Selected) { onlyMarked = false; break; }
        }

        Console.WriteLine("все показанные строки отмечены: " + onlyMarked);
        if (!onlyMarked) { Console.WriteLine("  ОШИБКА: видны неотмеченные строки"); problems++; }

        // повторное нажатие отменяет фильтр
        Call(form, "ToggleOnlySelected");
        Application.DoEvents();
        Counts(grid, out groups, out items);

        Console.WriteLine("фильтр снят: разделов " + groups + ", позиций " + items +
                          ", текст кнопки [" + button.Text + "]");

        if (items != allItems)
        {
            Console.WriteLine("  ОШИБКА: вернулось не всё, позиций " + items + " вместо " + allItems);
            problems++;
        }

        form.Close();
        Directory.Delete(store, true);

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ФИЛЬТР ПО ОТМЕТКАМ РАБОТАЕТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}