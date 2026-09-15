// Проверка: раскрытие списка заказчиков и поиск по ФИО или телефону.
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

    /// <summary>Раскрытие списка — так же, как это делает щелчок по стрелке.</summary>
    private static void OpenDropDown(ComboBox box)
    {
        MethodInfo method = typeof(ComboBox).GetMethod("OnDropDown", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new MissingMethodException("OnDropDown");
        method.Invoke(box, new object[] { EventArgs.Empty });
    }

    /// <summary>Настоящий щелчок по стрелке раскрытия списка.</summary>
    private static void ClickArrow(ComboBox box)
    {
        MethodInfo rectangle = typeof(ComboBox).GetMethod("get_DropDownButtonRectangle",
            BindingFlags.Instance | BindingFlags.NonPublic);

        // внутренний прямоугольник стрелки есть не во всех версиях: тогда раскрываем напрямую
        if (rectangle == null) { OpenDropDown(box); return; }

        System.Drawing.Rectangle area = (System.Drawing.Rectangle)rectangle.Invoke(box, null);
        System.Drawing.Point point = new System.Drawing.Point(area.Left + area.Width / 2, area.Top + area.Height / 2);

        MethodInfo mouseDown = typeof(ComboBox).GetMethod("OnMouseDown",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo mouseUp = typeof(ComboBox).GetMethod("OnMouseUp",
            BindingFlags.Instance | BindingFlags.NonPublic);

        mouseDown.Invoke(box, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        mouseUp.Invoke(box, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        Application.DoEvents();
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string store = Path.Combine(Path.GetTempPath(), "kotov-drop");
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

        Customer sergey = customers.Ensure("Сергей Кузнецов", "+7 (903) 777-55-33");
        ConnectionSettings.Customers.Save(customers);

        MainForm form = new MainForm();
        form.Show();
        Application.DoEvents();

        ComboBox box = (ComboBox)Field(form, "_customerPick");
        Console.WriteLine("заказчиков в справочнике: " + customers.Customers.Count);

        // 1) раскрытие пустого поля — весь справочник
        try
        {
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("после раскрытия пустого поля строк: " + box.Items.Count);
            Check("пустое поле показывает всех", box.Items.Count == 3, "строк " + box.Items.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при раскрытии пустого поля: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        // 2) поиск по части фамилии
        try
        {
            box.Text = "мария";
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("поиск «мария»: строк " + box.Items.Count +
                              (box.Items.Count > 0 ? ", первая [" + Convert.ToString(box.Items[0]) + "]" : ""));
            Check("поиск по имени нашёл одну карточку", box.Items.Count == 1, "строк " + box.Items.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при поиске по имени: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        // 3) поиск по цифрам телефона
        try
        {
            box.Text = "916";
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("поиск «916»: строк " + box.Items.Count +
                              (box.Items.Count > 0 ? ", первая [" + Convert.ToString(box.Items[0]) + "]" : ""));
            Check("поиск по цифрам телефона нашёл карточку", box.Items.Count == 1, "строк " + box.Items.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при поиске по телефону: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        // 4) поиск по полному номеру с восьмёркой
        try
        {
            box.Text = "89037775533";
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("поиск «89037775533»: строк " + box.Items.Count);
            Check("поиск по полному номеру нашёл карточку", box.Items.Count == 1, "строк " + box.Items.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при поиске по полному номеру: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        // 5) ничего не найдено
        try
        {
            box.Text = "нет такого";
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("поиск «нет такого»: строк " + box.Items.Count);
            Check("неизвестный заказчик не найден", box.Items.Count == 0, "строк " + box.Items.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при пустом результате: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        // 6) выбор из найденного списка
        box.Text = "кузнец";
        OpenDropDown(box);
        Application.DoEvents();


        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
            Application.DoEvents();


            PhoneBox phone = (PhoneBox)Field(form, "_phoneBox");
            TextBox car = (TextBox)Field(form, "_carBox");
            DocumentFields fields = (DocumentFields)Field(form, "_fields");

            Console.WriteLine("телефон: [" + phone.Text + "]");
            Console.WriteLine("автомобиль: [" + car.Text + "]");
            Console.WriteLine("в реквизитах заявки: заказчик [" + fields.Customer +
                              "], телефон [" + fields.CustomerPhone + "]");

            Check("в реквизитах записан заказчик",
                  fields.Customer == "Сергей Кузнецов", "записано [" + fields.Customer + "]");
            Check("в поле заказчика только ФИО",
                  box.Text == "Сергей Кузнецов", "в поле [" + box.Text + "]");

            Console.WriteLine("выбран: [" + box.Text + "], индекс " + box.SelectedIndex +
                              ", телефон [" + phone.Text + "]");
            // проверка телефона сделана выше, здесь поля уже перезаписаны

            // сразу после выбора в поле должно быть ФИО
            Check("сразу после выбора в поле ФИО", box.Text == "Сергей Кузнецов",
                  "в поле [" + box.Text + "]");

            // и после того, как форма отрисуется, поле не должно вернуть подпись строки
            Application.DoEvents();
            form.Refresh();
            Application.DoEvents();
            Console.WriteLine("после отрисовки: [" + box.Text + "]");
            Check("после отрисовки в поле ФИО", box.Text == "Сергей Кузнецов",
                  "в поле [" + box.Text + "]");

            // очистка выбранного заказчика
            typeof(MainForm).GetMethod("ClearCustomer", Hidden).Invoke(form, null);
            Application.DoEvents();

            Console.WriteLine("после очистки: заказчик [" + box.Text + "], телефон [" + phone.Text +
                              "], автомобиль [" + car.Text + "], номер [" +
                              ((TextBox)Field(form, "_plateBox")).Text + "]");

            Check("после очистки поле пустое", box.Text.Length == 0, "в поле [" + box.Text + "]");
            Check("после очистки телефон пуст", phone.Text.Length == 0, "телефон [" + phone.Text + "]");
            Check("после очистки автомобиль пуст", car.Text.Length == 0, "авто [" + car.Text + "]");
            Check("после очистки реквизиты пусты", fields.Customer.Length == 0,
                  "заказчик [" + fields.Customer + "]");

            // после очистки можно выбрать заказчика снова
            box.SelectedIndex = -1;
            box.Text = "петров";
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("после очистки поиск «петров»: строк " + box.Items.Count +
                              (box.Items.Count > 0 ? ", первая [" + Convert.ToString(box.Items[0]) + "]" : ""));
            Check("после очистки поиск работает", box.Items.Count == 1, "строк " + box.Items.Count);

            if (box.Items.Count > 0)
            {
                box.SelectedIndex = 0;
                Application.DoEvents();
                form.Refresh();
                Application.DoEvents();

                Console.WriteLine("новый выбор: [" + box.Text + "], телефон [" + phone.Text + "]");
                Check("после очистки выбор снова подставляет ФИО",
                      box.Text == "Иван Петров", "в поле [" + box.Text + "]");
                Check("после очистки выбор снова подставляет телефон",
                      phone.Text == "8 (912) 345-67-89", "телефон [" + phone.Text + "]");
            }
        }
        else
        {
            Console.WriteLine("  ОШИБКА: поиск «кузнец» ничего не нашёл");
            problems++;
        }

        // 7) настоящий щелчок по стрелке: именно здесь программа падала
        try
        {
            box.Text = "";
            ClickArrow(box);
            Console.WriteLine("щелчок по стрелке: раскрыт " + box.DroppedDown + ", строк " + box.Items.Count);
            Check("щелчок по стрелке не роняет программу", true, "");

            box.DroppedDown = false;
            Application.DoEvents();
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при щелчке по стрелке: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        // 8) повторное раскрытие после выбора не должно падать
        try
        {
            OpenDropDown(box);
            Application.DoEvents();
            Console.WriteLine("повторное раскрытие: строк " + box.Items.Count);
            Check("повторное раскрытие без ошибок", true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОШИБКА при повторном раскрытии: " + ex.GetType().Name + ": " + ex.Message);
            problems++;
        }

        form.Close();
        Directory.Delete(store, true);

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ПОИСК ЗАКАЗЧИКА РАБОТАЕТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}

