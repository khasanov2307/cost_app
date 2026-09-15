using System;
using KotovCalc;

internal static class PhoneProbe
{
    private static int problems;

    private static void Check(string input, string expected)
    {
        string actual = PhoneMask.Format(input);
        bool ok = actual == expected;
        if (!ok) problems++;
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + "[" + input + "] -> [" + actual + "]" +
                          (ok ? "" : "   ожидалось [" + expected + "]"));
    }

    private static void Main()
    {
        Console.WriteLine("маска телефона 8 (999) 999-99-99:");
        Check("8", "8");
        Check("89", "8 (9");
        Check("8912", "8 (912");
        Check("8912345", "8 (912) 345");
        Check("891234567", "8 (912) 345-67");
        Check("89123456789", "8 (912) 345-67-89");
        Check("+7 912 345 67 89", "8 (912) 345-67-89");
        Check("+7 (912) 345-67-89", "8 (912) 345-67-89");
        Check("79123456789", "8 (912) 345-67-89");
        Check("9123456789", "8 (912) 345-67-89");
        Check("8-912-345-67-89", "8 (912) 345-67-89");
        Check("8 912 345 67 89", "8 (912) 345-67-89");
        Check("891234567899999", "8 (912) 345-67-89");
        Check("", "");
        Check("ерунда", "");      // без цифр поле остаётся пустым
        Check("8 (912) 345-67-89", "8 (912) 345-67-89");

        Console.WriteLine();
        Console.WriteLine("полнота номера:");
        Console.WriteLine("  89123456789: " + PhoneMask.IsComplete("89123456789"));
        Console.WriteLine("  8912345: " + PhoneMask.IsComplete("8912345"));

        if (PhoneMask.IsComplete("89123456789") != true) problems++;
        if (PhoneMask.IsComplete("8912345") != false) problems++;

        Console.WriteLine();
        if (problems == 0) { Console.WriteLine("МАСКА ТЕЛЕФОНА РАБОТАЕТ"); Environment.Exit(0); }
        Console.WriteLine("ЗАМЕЧАНИЙ: " + problems);
        Environment.Exit(1);
    }
}