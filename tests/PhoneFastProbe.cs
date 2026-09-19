// Проверка поля телефона: быстрый ввод и быстрое стирание.
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

    /// <summary>Нажатие клавиши с цифрой: символ вставляется на месте курсора.</summary>
    private static void Type(PhoneBox box, char symbol)
    {
        int caret = Math.Min(box.SelectionStart, box.Text.Length);

        // как при нажатии клавиши: символ уже вставлен, курсор за ним
        string next = box.Text.Substring(0, caret) + symbol + box.Text.Substring(caret);
        box.Text = next;
        box.SelectionStart = Math.Min(caret + 1, box.Text.Length);
    }

    /// <summary>Стирание: убирается знак перед курсором, курсор сдвигается влево.</summary>
    private static void Backspace(PhoneBox box)
    {
        int caret = Math.Min(box.SelectionStart, box.Text.Length);
        if (caret == 0) return;

        box.Text = box.Text.Substring(0, caret - 1) + box.Text.Substring(caret);
        box.SelectionStart = caret - 1;

        // курсор не должен стоять между цифрой и знаком маски
        if (box.SelectionStart > 0 && box.SelectionStart < box.Text.Length &&
            !char.IsDigit(box.Text[box.SelectionStart]))
        {
            string formatted = PhoneMask.Format(box.Text);
            int digitsBefore = PhoneMask.Digits(box.Text.Substring(0, box.SelectionStart)).Length;
            box.SelectionStart = PhoneMask.PositionAfterDigits(formatted, digitsBefore);
        }
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        PhoneBox box = new PhoneBox();
        box.Width = 200;

        string digits = "89123456789";

        Console.WriteLine("быстрый ввод номера " + digits + ":");
        for (int i = 0; i < digits.Length; i++)
        {
            Type(box, digits[i]);
            Console.WriteLine("  после " + (i + 1) + "-й цифры: [" + box.Text + "], курсор " +
                              box.SelectionStart + " из " + box.Text.Length);
        }

        Check("номер собран верно", box.Text == "8 (912) 345-67-89", "[" + box.Text + "]");
        Check("курсор в конце номера", box.SelectionStart == box.Text.Length,
              "курсор " + box.SelectionStart + " из " + box.Text.Length);

        Console.WriteLine();
        Console.WriteLine("быстрое стирание по одной цифре:");
        for (int i = 1; i <= 11; i++)
        {
            Backspace(box);
            Console.WriteLine("  после стирания " + i + ": [" + box.Text + "], курсор " +
                              box.SelectionStart + " из " + box.Text.Length);
        }

        Check("после стирания поле пустое", box.Text.Length == 0, "[" + box.Text + "]");
        Check("курсор в начале", box.SelectionStart == 0, "курсор " + box.SelectionStart);

        Console.WriteLine();
        Console.WriteLine("быстрый ввод после стирания (набор заново):");
        string again = "9037775533";
        for (int i = 0; i < again.Length; i++)
        {
            Type(box, again[i]);
        }

        Console.WriteLine("  получилось: [" + box.Text + "]");
        Check("номер набран заново верно", box.Text == "8 (903) 777-55-33", "[" + box.Text + "]");

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("ВВОД И СТИРАНИЕ ТЕЛЕФОНА РАБОТАЮТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}