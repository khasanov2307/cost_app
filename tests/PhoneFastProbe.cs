// Проверка поля телефона в настоящем окне.
//
// Пока идёт ввод, поле остаётся обычным: цифры идут подряд, курсор двигается
// на один знак. Маска 8 (999) 999-99-99 ставится при уходе из поля и при чтении
// значения программой.
using System;
using System.Drawing;
using System.Windows.Forms;
using KotovCalc;

internal static class PhoneFastProbe
{
    private static int problems;

    private static void Check(string what, bool value, string details)
    {
        if (!value) problems++;
        Console.WriteLine((value ? "  PASS  " : "  FAIL  ") + what + (value ? "" : "   " + details));
    }

    private static void Type(PhoneBox box, char symbol)
    {
        int caret = Math.Min(box.SelectionStart, box.Text.Length);
        string next = box.Text.Substring(0, caret) + symbol + box.Text.Substring(caret);

        box.Text = next;
        box.SelectionStart = Math.Min(caret + 1, box.Text.Length);
    }

    private static void Backspace(PhoneBox box)
    {
        int caret = Math.Min(box.SelectionStart, box.Text.Length);
        if (caret == 0) return;

        box.Text = box.Text.Substring(0, caret - 1) + box.Text.Substring(caret);
        box.SelectionStart = Math.Min(caret - 1, box.Text.Length);
    }

    /// <summary>Ввод номера: курсор должен идти строго по одному знаку вперёд.</summary>
    private static void Run(Form form, PhoneBox box, string digits)
    {
        box.Text = "";
        box.SelectionStart = 0;
        Application.DoEvents();

        Console.WriteLine("набор " + digits + ":");
        bool caretOk = true;

        for (int i = 0; i < digits.Length; i++)
        {
            Type(box, digits[i]);
            Application.DoEvents();

            // курсор должен стоять ровно после введённых цифр
            if (box.SelectionStart != i + 1 || box.Text != digits.Substring(0, i + 1)) caretOk = false;

            Console.WriteLine("  " + (i + 1) + ": [" + box.Text + "] курсор " + box.SelectionStart +
                              " из " + box.Text.Length);
        }

        Check("курсор двигался ровно по одному знаку: " + digits, caretOk, "сбился");
        Check("пока идёт ввод, в поле набрано как есть: " + digits,
              box.Text == digits, "[" + box.Text + "]");

        // уход из поля: ставится маска
        box.ApplyMask();
        Application.DoEvents();

        string expected = PhoneMask.Format(digits);
        Console.WriteLine("  после маски: [" + box.Text + "]");
        Check("маска поставлена: " + digits, box.Text == expected,
              "[" + box.Text + "] вместо [" + expected + "]");
        Check("курсор в конце: " + digits, box.SelectionStart == box.Text.Length,
              "курсор " + box.SelectionStart + " из " + box.Text.Length);
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Form form = new Form();
        form.ClientSize = new Size(320, 120);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);      // окно есть, но за экраном

        PhoneBox box = new PhoneBox();
        box.Location = new Point(12, 12);
        box.Width = 240;
        form.Controls.Add(box);
        form.Show();
        Application.DoEvents();

        Run(form, box, "89123456789");    // с восьмёрки
        Run(form, box, "9123456789");     // без восьмёрки
        Run(form, box, "79123456789");    // с семёрки
        Run(form, box, "9037775533");     // без восьмёрки, другой номер

        // стирание по одной цифре
        box.Text = "89123456789";
        box.SelectionStart = box.Text.Length;
        Application.DoEvents();

        Console.WriteLine("стирание по одной цифре:");
        bool backspaceOk = true;

        for (int i = 11; i >= 1; i--)
        {
            Backspace(box);
            Application.DoEvents();

            if (box.SelectionStart != i - 1) backspaceOk = false;
            Console.WriteLine("  осталось " + (i - 1) + ": [" + box.Text + "] курсор " + box.SelectionStart);
        }

        Check("при стирании курсор двигался влево ровно", backspaceOk, "сбился");
        Check("после стирания поле пустое", box.Text.Length == 0, "[" + box.Text + "]");

        // чтение программой даёт номер по маске
        box.Text = "89123456789";
        Console.WriteLine("чтение значения: [" + box.Value + "]");
        Check("программа читает телефон по маске", box.Value == "8 (912) 345-67-89", box.Value);

        form.Close();

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ВВОД И СТИРАНИЕ ТЕЛЕФОНА РАБОТАЮТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}