// Проверка кнопки «Показывать остатки» и выбора заказчика из справочника.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KotovCalc;

internal static class StockProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object t, string n) { return t.GetType().GetField(n, Hidden).GetValue(t); }

    private static int problems;

    private static void Call(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null && m.GetParameters().Length == 0) { m.Invoke(target, null); return; }
        }
        throw new MissingMethodException(name);
    }

    private static void Check(string what, bool value, string details)
    {
        if (!value) problems++;
        Console.WriteLine((value ? "  PASS  " : "  FAIL  ") + what + (value ? "" : "   " + details));
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string store = Path.Combine(Path.GetTempPath(), "kotov-stock");
        if (Directory.Exists(store)) Directory.Delete(store, true);
        Directory.CreateDirectory(store);
        PriceBook.StorePath = Path.Combine(store, "prices.xml");
        PriceBook.Save(PriceBook.ReadSeed());

        // заказчик в справочнике и остатки на складе
        CustomerBook customers = new CustomerBook();
        Customer ivan = customers.Ensure("Иван Петров", "+7 (912) 345-67-89");
        ivan.Car = "Toyota Camry";
        ivan.Plate = "А123ВС 77";
        ConnectionSettings.Customers.Save(customers);

        Warehouse warehouse = new Warehouse();
        List<ServiceItem> catalog = PriceBook.ReadSeed();
        if (catalog.Count > 0)
        {
            warehouse.Income(catalog[0].Article, catalog[0].Name, 12m, 500m, "накладная");
            if (catalog.Count > 1)
                warehouse.Income(catalog[1].Article, catalog[1].Name, 3m, 200m, "накладная");
        }
        ConnectionSettings.Warehouse.Save(warehouse);

        MainForm form = new MainForm();
        form.Show();
        DataGridView grid = (DataGridView)Field(form, "_grid");
        Button stock = (Button)Field(form, "_btnStock");

        // --- заказчик: подстановка данных выбранной карточки
        Console.WriteLine("заказчиков в справочнике: " + customers.Customers.Count);
        typeof(MainForm).GetMethod("ApplyCustomer", Hidden).Invoke(form, new object[] { ivan });
        Application.DoEvents();

        TextBox customerField = (TextBox)Field(form, "_customerPick");
        TextBox car = (TextBox)Field(form, "_carBox");
        TextBox plate = (TextBox)Field(form, "_plateBox");
        PhoneBox phone = (PhoneBox)Field(form, "_phoneBox");

        Console.WriteLine("после выбора: заказчик [" + customerField.Text + "], телефон [" + phone.Text +
                          "], авто [" + car.Text + "], номер [" + plate.Text + "]");

        Check("в поле заказчика ФИО", customerField.Text == "Иван Петров", customerField.Text);
        Check("телефон подставился", phone.Text == "8 (912) 345-67-89", phone.Text);
        Check("автомобиль подставился", car.Text == "Toyota Camry", car.Text);
        Check("госномер подставился", plate.Text == "А123ВС 77", plate.Text);

        // --- колонка остатков
        int stockColumn = -1;
        for (int i = 0; i < grid.Columns.Count; i++)
            if (grid.Columns[i].HeaderText == "Остаток") stockColumn = i;

        Check("колонка «Остаток» существует", stockColumn >= 0, "не найдена");
        Check("колонка «Остаток» скрыта по умолчанию",
              stockColumn >= 0 && !grid.Columns[stockColumn].Visible, "видна сразу");

        // колонка идёт сразу после «Единицы измерения»
        int unitColumn = -1;
        for (int i = 0; i < grid.Columns.Count; i++)
            if (grid.Columns[i].HeaderText == "Единица измерения") unitColumn = i;

        Console.WriteLine("колонка единицы: " + unitColumn + ", колонка остатка: " + stockColumn);
        Check("остаток идёт сразу после единицы измерения",
              unitColumn >= 0 && stockColumn == unitColumn + 1,
              "единица " + unitColumn + ", остаток " + stockColumn);

        // нажимаем кнопку
        Console.WriteLine("кнопка: [" + stock.Text + "]");
        stock.PerformClick();
        Application.DoEvents();

        Check("после нажатия колонка видна",
              stockColumn >= 0 && grid.Columns[stockColumn].Visible, "скрыта");
        Check("надпись кнопки сменилась", stock.Text == "Остатки показаны", stock.Text);

        // значения остатков в строках позиций
        int filled = 0;
        string example = "";
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!(row.Tag is EstimateRow)) continue;
            string value = Convert.ToString(row.Cells[stockColumn].Value);
            if (value.Length > 0 && value != "0") { filled++; if (example.Length == 0) example = value; }
        }

        Console.WriteLine("строк с остатком: " + filled + ", пример: [" + example + "]");
        Check("остатки показаны в строках", filled >= 1, "заполненных строк " + filled);

        // повторное нажатие скрывает колонку
        stock.PerformClick();
        Application.DoEvents();

        Check("повторное нажатие скрывает колонку",
              stockColumn >= 0 && !grid.Columns[stockColumn].Visible, "осталась видна");
        Check("надпись кнопки вернулась", stock.Text == "Показывать остатки", stock.Text);

        // снимок с показанными остатками
        if (args.Length > 0)
        {
            stock.PerformClick();
            Application.DoEvents();
            form.Refresh();
            Application.DoEvents();

            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(args[0], ImageFormat.Png);
            }

            Console.WriteLine("снимок с остатками: " + Path.GetFullPath(args[0]));
        }

        form.Close();
        Directory.Delete(store, true);

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ОСТАТКИ И ВЫБОР ЗАКАЗЧИКА РАБОТАЮТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}
