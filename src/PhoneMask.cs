// ---------------------------------------------------------------------------
//  Телефон: единый вид 8 (999) 999-99-99.
//
//  Маска применяется, когда поле покидают или когда номер читает программа,
//  а не при каждом нажатии клавиши. Если форматировать прямо во время ввода,
//  приходится переставлять место курсора, а Windows Forms при этом сама меняет
//  текст — курсор прыгает, и цифры попадают не на своё место. Пока идёт ввод,
//  поле остаётся обычным полем с цифрами.
// ---------------------------------------------------------------------------

using System;
using System.Text;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Приведение телефона к виду 8 (999) 999-99-99.</summary>
    internal static class PhoneMask
    {
        /// <summary>Только цифры номера.</summary>
        public static string Digits(string text)
        {
            StringBuilder digits = new StringBuilder();

            foreach (char symbol in text ?? "")
                if (char.IsDigit(symbol)) digits.Append(symbol);

            return digits.ToString();
        }

        /// <summary>
        /// Телефон по маске 8 (999) 999-99-99.
        /// Незаполненные знаки не подставляются, чтобы строка не пустела зря.
        /// </summary>
        public static string Format(string text)
        {
            string digits = Digits(text);

            // 7 и 9 в начале — код страны или код оператора без восьмёрки
            if (digits.Length > 0 && digits[0] == '7') digits = digits.Substring(1);
            else if (digits.Length > 0 && digits[0] == '9') digits = "8" + digits;

            if (digits.Length == 0) return "";

            // первая цифра всегда восьмёрка
            if (digits[0] != '8') digits = "8" + digits;
            digits = digits.Substring(0, Math.Min(digits.Length, 11));

            StringBuilder text2 = new StringBuilder();
            text2.Append('8');

            if (digits.Length > 1)
                text2.Append(" (").Append(digits.Substring(1, Math.Min(3, digits.Length - 1)));

            if (digits.Length > 4)
                text2.Append(") ").Append(digits.Substring(4, Math.Min(3, digits.Length - 4)));

            if (digits.Length > 7)
                text2.Append('-').Append(digits.Substring(7, Math.Min(2, digits.Length - 7)));

            if (digits.Length > 9)
                text2.Append('-').Append(digits.Substring(9, Math.Min(2, digits.Length - 9)));

            return text2.ToString();
        }

        /// <summary>Заполнен ли номер полностью (11 цифр).</summary>
        public static bool IsComplete(string text)
        {
            string digits = Digits(text);
            return digits.Length == 11 && digits[0] == '8';
        }
    }

    /// <summary>
    /// Поле ввода телефона: во время набора остаётся обычным полем, а маска
    /// ставится при уходе из поля. Так курсор никогда не перескакивает.
    /// </summary>
    internal sealed class PhoneBox : TextBox
    {
        public PhoneBox()
        {
            MaxLength = 32;
        }

        /// <summary>Телефон в едином виде. На чтение — с маской, на запись — как есть.</summary>
        public string Value
        {
            get { return PhoneMask.Format(Text); }
            set
            {
                Text = value ?? "";
                ApplyMask();
            }
        }

        /// <summary>Привести текст поля к виду 8 (999) 999-99-99.</summary>
        public void ApplyMask()
        {
            string formatted = PhoneMask.Format(Text);
            if (formatted == Text) return;

            Text = formatted;
            SelectionStart = Text.Length;
        }

        protected override void OnLeave(EventArgs e)
        {
            ApplyMask();
            base.OnLeave(e);
        }
    }
}
