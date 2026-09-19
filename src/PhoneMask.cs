// ---------------------------------------------------------------------------
//  Телефон: единый вид 8 (999) 999-99-99.
//
//  Маска применяется при вводе и при показе сохранённых телефонов, поэтому
//  любой формат приводится к одному виду: +7 912 345 67 89, 89123456789
//  и 8 (912) 345-67-89 дают одинаковый результат.
// ---------------------------------------------------------------------------

using System;
using System.Drawing;
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
        /// Незаполненные знаки не подставляются, чтобы курсор не прыгал.
        /// </summary>
        public static string Format(string text)
        {
            string digits = Digits(text);

            // 7 и 9 в начале — это код страны или код оператора без восьмёрки
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

        /// <summary>Место курсора после указанного числа цифр и знаков маски.</summary>
        public static int PositionAfterDigits(string text, int digits)
        {
            if (digits <= 0) return 0;

            int seen = 0;
            int position = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) continue;

                seen++;
                position = i + 1;
                if (seen == digits) break;
            }

            if (seen < digits) return text.Length;

            // пропускаем знаки маски сразу за цифрой: «) », «-», пробел
            while (position < text.Length && !char.IsDigit(text[position])) position++;

            return position;
        }

        /// <summary>Заполнен ли номер полностью (11 цифр).</summary>

        public static bool IsComplete(string text)
        {
            string digits = Digits(text);
            return digits.Length == 11 && digits[0] == '8';
        }
    }

    /// <summary>Поле ввода телефона: само ставит скобки и дефисы.</summary>
    internal sealed class PhoneBox : TextBox
    {
        private bool _formatting;

        public PhoneBox()
        {
            MaxLength = 18;                     // «8 (999) 999-99-99» — 18 знаков
        }
        /// <summary>
        /// Форматирование по ходу ввода. Место курсора остаётся после той же
        /// цифры: считаем цифры слева от курсора до форматирования и ставим курсор
        /// после стольких же цифр в готовой строке. Так одинаково верно работает
        /// и быстрый ввод, и быстрое стирание.
        /// </summary>
        protected override void OnTextChanged(EventArgs e)
        {
            // защита от повторного входа: форматирование меняет текст и снова вызывает событие
            if (_formatting) { base.OnTextChanged(e); return; }

            base.OnTextChanged(e);

            // сколько цифр стоит слева от курсора — считаем до любых изменений
            int caret = Math.Min(SelectionStart, Text.Length);
            int digitsBefore = PhoneMask.Digits(Text.Substring(0, caret)).Length;

            string formatted = PhoneMask.Format(Text);

            // запрет входа ставим до правок: установка курсора в Windows Forms
            // меняет текст и снова вызывает это событие, из-за чего курсор
            // перескакивал, а цифры попадали не на своё место
            _formatting = true;
            try
            {
                if (formatted != Text) Text = formatted;

                SelectionStart = PhoneMask.PositionAfterDigits(Text, digitsBefore);
                SelectionLength = 0;
            }
            finally
            {
                _formatting = false;
            }
        }
    }
}
