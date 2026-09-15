// Тестовый драйвер: проверяет загрузку справочника, расчёт итога и сохранение отметок.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KotovCalc;

internal static class Harness
{
    private static int _failures;

    private static void Check(string name, object expected, object actual)
    {
        bool ok = Equals(expected, actual);
        if (!ok) _failures++;
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
                          (ok ? "" : "   expected=<" + expected + "> actual=<" + actual + ">"));
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // хранилище цен подменяем на временное, чтобы не трогать данные пользователя
        if (args != null && args.Length > 0) PriceBook.StorePath = args[0];

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;

        // ---------- разбор прайс-листа ----------
        Console.WriteLine("[1] Прайс-лист из внутреннего хранилища");
        string error;
        List<ServiceItem> items = PriceBook.Load(out error);
        Check("позиций загружено", 36, items.Count);
        Check("хранилище создано", true, File.Exists(PriceBook.StorePath));

        // второй запуск читает из хранилища уже без замечаний
        List<ServiceItem> again = PriceBook.Load(out error);
        Check("повторное чтение без ошибок", true, string.IsNullOrEmpty(error));
        Check("позиций столько же", 36, again.Count);
        Check("сообщений об ошибке нет в статусе", true, string.IsNullOrEmpty(error));

        // ожидаемый итог считаем независимо, по заводскому набору цен
        decimal expectedDiag = 0m;
        decimal expectedMaint = 0m;
        int diagCount = 0;
        foreach (ServiceItem seed in PriceBook.ReadSeed())
        {
            if (seed.Group.StartsWith("Диагностика"))
            {
                expectedDiag += seed.Price;
                diagCount++;
            }
            else if (seed.Group.IndexOf("обслуживание", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                expectedMaint += seed.Price;
            }
        }
        Console.WriteLine("        ожидаемый итог по группе Диагностика = " + expectedDiag +
                          " (" + diagCount + " поз.)");
        Check("позиций в группе Диагностика", 5, diagCount);

        // ---------- форма ----------
        Console.WriteLine("[2] Форма и таблица");
        MainForm form = new MainForm();

        DataGridView grid = GetField<DataGridView>(form, "_grid");
        Label totalLabel = GetField<Label>(form, "_totalLabel");
        Label countLabel = GetField<Label>(form, "_countLabel");

        Check("форма создана", true, form != null);
        Check("строк в таблице (36 позиций + 5 заголовков)", 41, grid.Rows.Count);
        Check("начальный итог", "ИТОГО: 0,00 \u20BD", totalLabel.Text);

        Console.WriteLine("[2б] Наименования колонок таблицы");
        Check("шапка таблицы видна", true, grid.ColumnHeadersVisible);
        Check("колонок в таблице", 8, grid.Columns.Count);
        Check("колонка 1 (артикул)", "Артикул", grid.Columns[1].HeaderText);
        Check("колонка 2", "Наименование", grid.Columns[2].HeaderText);
        Check("колонка 3", "Единица измерения", grid.Columns[3].HeaderText);
        Check("колонка 4", "Цена", grid.Columns[4].HeaderText);
        Check("колонка 5", "Количество", grid.Columns[5].HeaderText);
        Check("колонка 6", "Всего", grid.Columns[6].HeaderText);
        Check("колонка 7 (цена в заявке)", "Цена в заявке", grid.Columns[7].HeaderText);

        // номера колонок для проверок ниже: порядок задаётся в программе
        int columnQuantity = -1;
        int columnSum = -1;

        for (int i = 0; i < grid.Columns.Count; i++)
        {
            if (grid.Columns[i].HeaderText == "Количество") columnQuantity = i;
            if (grid.Columns[i].HeaderText == "Всего") columnSum = i;
        }
        Check("колонка галочек без надписи", "", grid.Columns[0].HeaderText);

        // отметить группу "Диагностика" через публичное поведение checkbox-ячейки
        Console.WriteLine("[3] Отметка группы «Диагностика» галочками");
        int marked = 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!(row.Tag is EstimateRow)) continue;
            EstimateRow data = (EstimateRow)row.Tag;
            if (!data.Item.Group.StartsWith("Диагностика")) continue;
            row.Cells[0].Value = true;      // именно так это делает щелчок по галочке
            marked++;
        }
        Check("отмечено галочками", 5, marked);
        Check("итог в подписи", "ИТОГО: " + expectedDiag.ToString("N2", Fmt.Ru) + " \u20BD",
              totalLabel.Text);
        Check("внутренний итог", expectedDiag, SumSelected(form));

        // количество 2 у первой позиции => итог + одна цена
        Console.WriteLine("[4] Изменение количества");
        EstimateRow first = null;
        DataGridViewRow firstGridRow = null;
        foreach (DataGridViewRow row in grid.Rows)
        {
            EstimateRow data = row.Tag as EstimateRow;
            if (data != null && data.Selected) { first = data; firstGridRow = row; break; }
        }
        Check("первая отмеченная позиция найдена", true, first != null);
        firstGridRow.Cells[columnQuantity].Value = 2m;
        Check("итог удвоенной позиции", expectedDiag + first.Item.Price, SumSelected(form));
        Check("сумма в строке", first.Item.Price * 2m,
              Convert.ToDecimal(firstGridRow.Cells[columnSum].Value, CultureInfo.InvariantCulture));

        Console.WriteLine("[5] Обработка неверного количества");
        firstGridRow.Cells[columnQuantity].Value = "абв";
        Check("итог не изменился", expectedDiag + first.Item.Price, SumSelected(form));
        Check("количество восстановлено", 2m, first.Quantity);

        Console.WriteLine("[6] Поиск по справочнику");
        TextBox find = GetField<TextBox>(form, "_find");
        find.Text = "масл";
        int visible = 0;
        foreach (DataGridViewRow row in grid.Rows) if (row.Visible) visible++;
        Console.WriteLine("        видимых строк при поиске «масл»: " + visible);
        Check("поиск что-то нашёл", true, visible > 0 && visible < grid.Rows.Count);

        find.Text = "заведомо-нет-такого";
        int visibleNone = 0;
        foreach (DataGridViewRow row in grid.Rows) if (row.Visible) visibleNone++;
        Check("пустой результат поиска", 0, visibleNone);

        Console.WriteLine("[7] Функция разбора чисел");
        decimal v;
        Check("«1 234,56»", 1234.56m, Fmt.TryParseDecimal("1 234,56", out v) ? v : -1m);
        Check("«1200»", 1200m, Fmt.TryParseDecimal("1200", out v) ? v : -1m);
        Check("«2.5»", 2.5m, Fmt.TryParseDecimal("2.5", out v) ? v : -1m);
        Check("«1 500,00 ₽»", 1500m, Fmt.TryParseDecimal("1 500,00 \u20BD", out v) ? v : -1m);

        Console.WriteLine("[8] Формирование текста сметы");
        string doc = (string)typeof(MainForm)
            .GetMethod("BuildDocumentText", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
        Check("заявка содержит шапку", true, doc.Contains("ЗАЯВКА НА РАСЧЕТ"));
        Check("заявка содержит итог", true, doc.Contains("ИТОГО К ОПЛАТЕ:"));
        Check("заявка содержит отмеченную услугу", true, doc.Contains("Компьютерная диагностика"));

        Console.WriteLine("[9] При запуске начинается новая заявка");
        MethodInfo save = typeof(MainForm).GetMethod("SaveSession",
            BindingFlags.Instance | BindingFlags.NonPublic);
        save.Invoke(form, null);
        string cfg = Path.Combine(PriceBook.StoreFolder, "session.tsv");
        Check("файл состояния создан", true, File.Exists(cfg));

        // новая заявка: отметки и количества прошлого раза не восстанавливаются
        MainForm second = new MainForm();
        Label secondTotal = GetField<Label>(second, "_totalLabel");
        Check("при запуске итог нулевой", "ИТОГО: 0,00 \u20A0\u20BD".Replace("\u20A0", ""),
              secondTotal.Text.Replace("  ", " "));
        Check("при запуске нет отмеченных позиций", 0, CountSelected(second));
        DocumentFields secondFields = GetField<DocumentFields>(second, "_fields");
        Check("номер заявки очищен", "", secondFields.Number);
        Check("заказчик очищен", "", secondFields.Customer);

        if (File.Exists(cfg)) File.Delete(cfg);
        second.Dispose();
        form.Dispose();

        Console.WriteLine();
        if (_failures == 0) Console.WriteLine("ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ");
        else Console.WriteLine("ПРОВАЛЕНО ПРОВЕРОК: " + _failures);

        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    private static decimal SumSelected(MainForm form)
    {
        decimal total = 0m;
        FieldInfo field = typeof(MainForm).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (EstimateRow row in (IEnumerable<EstimateRow>)field.GetValue(form))
            total += row.Sum;
        return total;
    }

    private static int CountSelected(MainForm form)
    {
        List<EstimateRow> rows = GetField<List<EstimateRow>>(form, "_rows");
        int count = 0;
        foreach (EstimateRow row in rows) if (row.Selected) count++;
        return count;
    }

    private static EstimateRow FindSelected(MainForm form, string group, string name)
    {
        FieldInfo field = typeof(MainForm).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (EstimateRow row in (IEnumerable<EstimateRow>)field.GetValue(form))
            if (row.Item.Group == group && row.Item.Name == name) return row;
        return null;
    }

    private static T GetField<T>(object instance, string name) where T : class
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return (T)field.GetValue(instance);
    }
}