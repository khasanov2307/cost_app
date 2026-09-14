// ---------------------------------------------------------------------------
//  Калькулятор стоимости услуг  (Windows Forms, .NET Framework 4.x)
//
//  Назначение: расчёт стоимости услуг по прайс-листу (справочнику).
//  Пользователь отмечает галочками нужные позиции, при необходимости правит
//  количество — итоговая сумма считается автоматически.
//
//  Сборка: build.ps1 (использует csc.exe из .NET Framework, установка не нужна)
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace KotovCalc
{
    /// <summary>Единицы измерения, доступные для выбора в карточке позиции.</summary>
    internal static class Uom
    {
        /// <summary>Перечень единиц измерения, предлагаемый в карточке позиции.</summary>
        public static readonly string[] List = new string[]
        {
            "шт.",      // штука
            "услуга",
            "литр",
            "комплект"
        };

        /// <summary>Название единицы для подсказок: «шт.» — «штука».</summary>
        public static string Title(string unit)
        {
            if (string.IsNullOrEmpty(unit)) return List[0];
            if (unit == "шт." || unit == "шт" || unit.Equals("штука", StringComparison.CurrentCultureIgnoreCase))
                return "штука";
            return unit;
        }

        /// <summary>Приводит единицу к перечню; незнакомые значения сохраняются как есть.</summary>
        public static string Normalize(string unit)
        {
            if (string.IsNullOrEmpty(unit)) return List[0];

            string trimmed = unit.Trim();
            if (trimmed.Length == 0) return List[0];

            foreach (string known in List)
            {
                if (string.Equals(known, trimmed, StringComparison.CurrentCultureIgnoreCase))
                    return known;
            }

            // привычные сокращения
            if (trimmed.Equals("шт", StringComparison.CurrentCultureIgnoreCase) ||
                trimmed.Equals("штука", StringComparison.CurrentCultureIgnoreCase) ||
                trimmed.Equals("штук", StringComparison.CurrentCultureIgnoreCase))
                return "шт.";
            if (trimmed.Equals("л", StringComparison.CurrentCultureIgnoreCase) ||
                trimmed.Equals("литров", StringComparison.CurrentCultureIgnoreCase))
                return "литр";
            if (trimmed.Equals("компл", StringComparison.CurrentCultureIgnoreCase) ||
                trimmed.Equals("компл.", StringComparison.CurrentCultureIgnoreCase))
                return "комплект";

            return trimmed;                 // своя единица измерения — оставляем
        }
    }

    /// <summary>Одна позиция справочника «прайс-лист» (карточка позиции).</summary>
    internal sealed class ServiceItem
    {
        public string Group = "";
        public string Article = "";
        public string Name = "";
        public string Unit = "шт.";
        public decimal Price;

        public ServiceItem() { }

        public ServiceItem(string group, string name, string unit, decimal price)
        {
            Group = group;
            Name = name;
            Unit = unit;
            Price = price;
        }

        public ServiceItem(string group, string article, string name, string unit, decimal price)
        {
            Group = group;
            Article = article;
            Name = name;
            Unit = unit;
            Price = price;
        }

        public ServiceItem Clone()
        {
            return new ServiceItem(Group, Article, Name, Unit, Price);
        }
    }

    /// <summary>Позиция сметы: данные прайса, отметка, количество и цена для этой сметы.</summary>
    internal sealed class EstimateRow
    {
        public ServiceItem Item;
        public bool Selected;
        public decimal Quantity = 1m;

        /// <summary>Цена, изменённая прямо в смете (null — берётся цена прайса).</summary>
        public decimal? PriceOverride;

        public EstimateRow(ServiceItem item) { Item = item; }

        /// <summary>Цена, по которой считается позиция.</summary>
        public decimal Price
        {
            get { return PriceOverride.HasValue ? PriceOverride.Value : Item.Price; }
        }

        /// <summary>Цена отличается от прайса.</summary>
        public bool PriceChanged
        {
            get { return PriceOverride.HasValue && PriceOverride.Value != Item.Price; }
        }

        public decimal Sum { get { return Selected ? Price * Quantity : 0m; } }
    }

    /// <summary>Реквизиты документа, который формируется из сметы.</summary>
    internal sealed class DocumentFields
    {
        public string Number = "";      // номер сметы
        public string Customer = "";    // ФИО заказчика
        public decimal Discount;        // скидка на всю смету, %

        public void Normalize()
        {
            if (Number == null) Number = "";
            if (Customer == null) Customer = "";

            Number = Number.Trim();
            Customer = Customer.Trim();

            if (Discount < 0m) Discount = 0m;
            if (Discount > 90m) Discount = 90m;
        }

        public DocumentFields Copy()
        {
            return new DocumentFields { Number = Number, Customer = Customer, Discount = Discount };
        }
    }

    /// <summary>Шаблон набора услуг: имя и позиции с количеством.</summary>
    internal sealed class ServiceTemplate
    {
        public string Name = "";
        public DateTime Saved = DateTime.Now;
        public List<TemplateItem> Items = new List<TemplateItem>();

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Позиция шаблона: артикул, раздел, наименование и количество.</summary>
    internal sealed class TemplateItem
    {
        public string Group = "";
        public string Article = "";
        public string Name = "";
        public decimal Quantity = 1m;
    }

    /// <summary>Культура приложения: десятичная запятая, как принято в РФ.</summary>
    internal static class Fmt
    {
        public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        /// <summary>Сумма в виде «1 234,56 ₽».</summary>
        public static string Money(decimal value)
        {
            return value.ToString("N2", Ru) + " \u20BD";
        }

        public static string MoneyPlain(decimal value)
        {
            return value.ToString("N2", Ru);
        }

        public static string Qty(decimal value)
        {
            return value.ToString("0.###", Ru);
        }

        /// <summary>Разбор числа с поддержкой запятой и точки, пробелов и знака ₽.</summary>
        public static bool TryParseDecimal(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrEmpty(text)) return false;

            string cleaned = text.Replace("\u20BD", "").Replace("\u00A0", "")
                                 .Replace(" ", "").Replace("'", "").Trim();
            if (cleaned.Length == 0) return false;

            if (decimal.TryParse(cleaned, NumberStyles.Number, Ru, out value)) return true;
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return true;

            cleaned = cleaned.Replace(',', '.');
            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }
    }
}