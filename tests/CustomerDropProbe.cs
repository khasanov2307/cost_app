// Проверка выбора заказчика: кнопка выбора, подстановка данных и очистка.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KotovCalc;

internal static class CustomerDropProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object t, string n) { return t.GetType().GetField(n, Hidden).GetValue(t); }

    private static int problems;

    private static void Check(string what, bool value, string details)
    {
        if (!value) problems++;
        Console.WriteLine((value ? "  PASS  " : "  FAIL  ") + what + (value ? "" : "   " + details));
    }

    private static void Call(object target, string name, params object[] args)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (method.Name != name) continue;
                if (method.GetParameters().Length != args.Length) continue;
                method.Invoke(target, args);
                return;
            }
        }
        throw new MissingMethodException(name);
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string store = Path.Combine(Path.GetTempPath(), "kotov-customer");
        if (Directory.Exists(store)) Directory.Delete(store, true);
        Directory.CreateDirectory(store);
        PriceBook.StorePath = Path.Combine(store, "prices.xml");
        PriceBook.Save(PriceBook.ReadSeed());

        CustomerBook customers = new CustomerBook();

        Customer ivan = customers.Ensure("Иван Петров", "+7 (912) 345-67-89");
        ivan.Car = "Toyota Camry";
        ivan.Plate = "А123ВС 77";

        Customer maria = customers.Ensure("Мария Сидорова", "8 916 000-11-22");
        maria.Car = "Kia Rio";

        customers.Ensure("Сергей Кузнецов", "+7 (903) 777-55-33");
        ConnectionSettings.Customers.Save(customers);

        MainForm form = new MainForm();
        form.Show();
        Application.DoEvents();

        TextBox box = (TextBox)Field(form, "_customerPick");
        PhoneBox phone = (PhoneBox)Field(form, "_phoneBox");
        TextBox car = (TextBox)Field(form, "_carBox");
        TextBox plate = (TextBox)Field(form, "_plateBox");
        DocumentFields fields = (DocumentFields)Field(form, "_fields");

        Check("поле заказчика — поле ввода, а не список",
              box != null && !(box is ComboBox), "тип " + (box == null ? "нет" : box.GetType().Name));

        // подстановка выбранного заказчика
        Call(form, "ApplyCustomer", ivan);
        Application.DoEvents();

        Console.WriteLine("после выбора: поле [" + box.Text + "], телефон [" + phone.Text +
                          "], авто [" + car.Text + "], номер [" + plate.Text + "]");

        Check("в поле заказчика ФИО", box.Text == "Иван Петров", "в поле [" + box.Text + "]");
        Check("телефон подставлен", phone.Text == "8 (912) 345-67-89", phone.Text);
        Check("автомобиль подставлен", car.Text == "Toyota Camry", car.Text);
        Check("госномер подставлен", plate.Text == "А123ВС 77", plate.Text);

        // курсор стоит в конце ФИО, а не в начале
        Console.WriteLine("курсор после выбора: " + box.SelectionStart + " из " + box.Text.Length);
        Check("курсор в конце ФИО", box.SelectionStart == box.Text.Length,
              "курсор " + box.SelectionStart);

        // ввод с клавиатуры: курсор не прыгает и текст не теряется
        box.Text = "";
        box.SelectionStart = 0;
        Application.DoEvents();

        string typed = "сидор";
        for (int i = 1; i <= typed.Length; i++)
        {
            box.Text = typed.Substring(0, i);
            box.SelectionStart = i;
            Application.DoEvents();

            Console.WriteLine("  введено [" + box.Text + "] курсор " + box.SelectionStart +
                              " из " + box.Text.Length);
        }

        Check("курсор остался в конце строки", box.SelectionStart == box.Text.Length,
              "курсор " + box.SelectionStart + " при длине " + box.Text.Length);
        Check("введённый текст не потерялся", box.Text == "сидор", "в поле [" + box.Text + "]");
        Check("введённый строкой заказчик попал в реквизиты",
              fields.Customer == "сидор", "в реквизитах [" + fields.Customer + "]");

        // поиск по введённому тексту: по ФИО и по цифрам телефона
        List<Customer> byName = customers.Search("сидор");
        List<Customer> byPhone = customers.Search("916");
        List<Customer> byFull = customers.Search("89037775533");

        Check("поиск по части ФИО", byName.Count == 1 && byName[0].Name == "Мария Сидорова",
              "найдено " + byName.Count);
        Check("поиск по части телефона", byPhone.Count == 1 && byPhone[0].Name == "Мария Сидорова",
              "найдено " + byPhone.Count);
        Check("поиск по полному номеру с восьмёркой", byFull.Count == 1 &&
              byFull[0].Name == "Сергей Кузнецов", "найдено " + byFull.Count);
        Check("неизвестный заказчик не найден", customers.Search("нет такого").Count == 0, "нашёлся");

        // очистка
        Call(form, "ApplyCustomer", maria);
        Application.DoEvents();
        Call(form, "ClearCustomer");
        Application.DoEvents();

        Console.WriteLine("после очистки: поле [" + box.Text + "], телефон [" + phone.Text +
                          "], авто [" + car.Text + "], номер [" + plate.Text + "]");

        Check("после очистки поле пустое", box.Text.Length == 0, "в поле [" + box.Text + "]");
        Check("после очистки телефон пуст", phone.Text.Length == 0, "телефон [" + phone.Text + "]");
        Check("после очистки автомобиль пуст", car.Text.Length == 0, "авто [" + car.Text + "]");
        Check("после очистки госномер пуст", plate.Text.Length == 0, "номер [" + plate.Text + "]");
        Check("после очистки реквизиты пусты", fields.Customer.Length == 0,
              "заказчик [" + fields.Customer + "]");

        // после очистки можно выбрать другого
        Call(form, "ApplyCustomer", ivan);
        Application.DoEvents();

        Check("после очистки выбор снова работает", box.Text == "Иван Петров",
              "в поле [" + box.Text + "]");

        form.Close();
        Directory.Delete(store, true);

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ВЫБОР ЗАКАЗЧИКА РАБОТАЕТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}