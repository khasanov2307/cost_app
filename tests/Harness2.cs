// Проверки встроенного редактора прайс-листа и обновления цен с сохранением отметок.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KotovCalc;

internal static class Harness2
{
    private static int _failures;

    private static void Check(string name, object expected, object actual)
    {
        bool ok = Equals(expected, actual);
        if (!ok) _failures++;
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
                          (ok ? "" : "   expected=<" + expected + "> actual=<" + actual + ">"));
    }

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T Get<T>(object instance, string field) where T : class
    {
        return (T)instance.GetType().GetField(field, Hidden).GetValue(instance);
    }

    private static object Call(object instance, string method, params object[] args)
    {
        if (args == null) args = new object[0];
        MethodInfo info = instance.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (info == null) throw new MissingMethodException(instance.GetType().Name, method);

        try
        {
            return info.Invoke(instance, args);
        }
        catch (TargetInvocationException ex)
        {
            throw new Exception(method + " -> " + ex.InnerException.Message, ex.InnerException);
        }
    }

    // колонки таблицы редактора: 0 — знак, 1 — раздел, 2 — артикул,
    // 3 — наименование, 4 — ед. изм., 5 — цена
    private const int ColToggle = 0;
    private const int ColGroup = 1;
    private const int ColArticle = 2;
    private const int ColName = 3;
    private const int ColUnit = 4;
    private const int ColPrice = 5;

    private static List<PriceRow> Rows(PriceEditorForm editor)
    {
        return Get<List<PriceRow>>(editor, "_rows");
    }

    private static DataGridView Grid(Form form)
    {
        return Get<DataGridView>(form, "_grid");
    }

    private static Label Total(MainForm form)
    {
        return Get<Label>(form, "_totalLabel");
    }

    private static decimal SumSelected(MainForm form)
    {
        decimal total = 0m;
        foreach (EstimateRow row in Get<List<EstimateRow>>(form, "_rows")) total += row.Sum;
        return total;
    }

    private static EstimateRow Find(MainForm form, string name)
    {
        foreach (EstimateRow row in Get<List<EstimateRow>>(form, "_rows"))
            if (row.Item.Name == name) return row;
        return null;
    }

    private static DataGridViewRow FindRow(DataGridView grid, PriceRow row)
    {
        foreach (DataGridViewRow gridRow in grid.Rows)
            if (ReferenceEquals(gridRow.Tag, row)) return gridRow;
        return null;
    }

    private static PriceRow Find(PriceEditorForm editor, string name)
    {
        foreach (PriceRow row in Rows(editor))
            if (row.Name == name) return row;
        return null;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // хранилище цен подменяем на временное, чтобы не трогать данные пользователя
        if (args != null && args.Length > 0) PriceBook.StorePath = args[0];

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string cfg = Path.Combine(PriceBook.StoreFolder, "session.tsv");
        if (File.Exists(cfg)) File.Delete(cfg);

        Console.WriteLine("[1] Редактор загружает прайс-лист из хранилища");
        PriceBook.Save(PriceBook.ReadSeed());          // чистое состояние для проверки
        MainForm main = new MainForm();
        PriceEditorForm editor = new PriceEditorForm();
        DataGridView grid = Grid(editor);
        Check("позиций в редакторе", 36, Rows(editor).Count);
        // 36 позиций + 5 строк-заголовков разделов
        Check("строк в таблице редактора", 41, grid.Rows.Count);

        Console.WriteLine("[2] Правка позиции так, как это делает пользователь (ввод в ячейку)");
        PriceRow first = Rows(editor)[0];
        DataGridViewRow firstRow = FindRow(grid, first);
        grid.CurrentCell = firstRow.Cells[1];
        grid.BeginEdit(true);
        Check("правка открыта", true, grid.IsCurrentCellInEditMode);
        firstRow.Cells[ColName].Value = "Компьютерная диагностика (новая редакция)";
        grid.EndEdit();
        Check("правка применилась к нужной позиции", "Компьютерная диагностика (новая редакция)",
              first.Name);
        Check("соседняя позиция не изменилась", "Диагностика ходовой части", Rows(editor)[1].Name);
        Check("всего позиций не изменилось", 36, Rows(editor).Count);

        Console.WriteLine("[3] Добавление позиции");
        grid.CurrentCell = firstRow.Cells[1];
        PriceRow before = Rows(editor)[0];
        Call(editor, "AddRows");                      // добавится в конец своего раздела
        Check("позиций стало больше", 37, Rows(editor).Count);

        // новая позиция — последняя в разделе выделенной строки
        PriceRow created = null;
        for (int i = Rows(editor).Count - 1; i >= 0; i--)
        {
            if (Rows(editor)[i].Name != "Новая услуга") continue;
            created = Rows(editor)[i];
            break;
        }
        Check("новая позиция создана", true, created != null);
        Check("новая позиция в разделе выделенной строки", before.Group, created.Group);
        int lastInSection = Rows(editor).IndexOf(created);
        for (int i = 0; i < Rows(editor).Count; i++)
            if (Rows(editor)[i].Group == before.Group) lastInSection = Math.Max(lastInSection, i);
        Check("новая позиция в конце раздела", Rows(editor).IndexOf(created), lastInSection);

        // вписываем данные: группа, наименование, единица, цена
        DataGridViewRow createdRow = FindRow(grid, created);
        grid.CurrentCell = createdRow.Cells[1];
        grid.BeginEdit(true);
        createdRow.Cells[ColName].Value = "Проверка фар";
        grid.EndEdit();
        createdRow.Cells[ColGroup].Value = "Диагностика";
        createdRow.Cells[ColUnit].Value = "услуга";
        createdRow.Cells[ColPrice].Value = 300m;
        Check("наименование записано", "Проверка фар", created.Name);
        Check("группа записана", "Диагностика", created.Group);
        Check("единица записана", "услуга", created.Unit);
        Check("цена записана", 300m, created.Price);

        Console.WriteLine("[4] Дублирование");
        grid.ClearSelection();
        createdRow.Selected = true;
        Call(editor, "DuplicateRows");
        Check("позиций после дублирования", 38, Rows(editor).Count);

        Console.WriteLine("[5] Проверка данных перед записью");
        PriceRow blank = Rows(editor)[0];
        string original = blank.Name;
        blank.Name = "";
        Check("найдена одна проблема", 1, (int)Call(editor, "ValidateRows"));
        Check("ошибочная ячейка помечена", true,
              FindRow(grid, blank).Cells[ColName].ErrorText.Length > 0);
        blank.Name = original;
        Check("после исправления проблем нет", 0, (int)Call(editor, "ValidateRows"));

        Console.WriteLine("[6] Удаление выделенной позиции");
        PriceRow extra = null;
        foreach (PriceRow row in Rows(editor))
            if (row.Name == "Проверка фар" && !ReferenceEquals(row, created)) extra = row;
        Check("дубликат найден", true, extra != null);
        grid.ClearSelection();
        FindRow(grid, extra).Selected = true;
        Call(editor, "DeleteRowsConfirmed");
        Check("позиций после удаления", 37, Rows(editor).Count);
        Check("осталась одна «Проверка фар»", 1, CountNamed(editor, "Проверка фар"));

        Console.WriteLine("[7] Сортировка и группировка");
        Call(editor, "SortByName");
        bool sorted = true;
        for (int i = 1; i < Rows(editor).Count; i++)
            if (string.Compare(Rows(editor)[i - 1].Name, Rows(editor)[i].Name,
                    StringComparison.CurrentCultureIgnoreCase) > 0) sorted = false;
        Check("отсортировано по наименованию", true, sorted);

        Call(editor, "GroupByGroup");
        HashSet<string> seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        string previous = null;
        bool grouped = true;
        foreach (PriceRow row in Rows(editor))
        {
            if (string.Equals(previous, row.Group, StringComparison.CurrentCultureIgnoreCase)) continue;
            if (seen.Contains(row.Group)) grouped = false;
            seen.Add(row.Group);
            previous = row.Group;
        }
        Check("разделы идут одним блоком", true, grouped);
        Check("позиция на месте", true, Find(editor, "Проверка фар") != null);

        Console.WriteLine("[7б] Правка наименования раздела (ранее падала)");
        DataGridViewRow sectionRow = null;
        foreach (DataGridViewRow r in grid.Rows) if (r.Tag is PriceRow) { sectionRow = r; break; }
        ToggleClick(editor, grid, sectionRow.Index, ColGroup);   // щелчок по ячейке «Раздел»
        Check("щелчок по ячейке раздела не сворачивает раздел", true, sectionRow.Visible);
        Check("ячейка раздела доступна для правки", false, sectionRow.Cells[ColGroup].ReadOnly);

        PriceRow sectionData = (PriceRow)sectionRow.Tag;
        grid.CurrentCell = sectionRow.Cells[1];
        grid.BeginEdit(true);
        sectionRow.Cells[ColGroup].Value = "Диагностика и осмотр";
        grid.EndEdit();
        Check("название раздела изменено", "Диагностика и осмотр", sectionData.Group);
        Check("наименование услуги не пострадало",
              !string.IsNullOrEmpty(sectionData.Name) && sectionData.Name != "Диагностика и осмотр",
              true);

        Console.WriteLine("[7в] Сворачивание раздела знаком «–/+»");
        int visibleBefore = CountVisible(grid);
        int headerRow = sectionRow.Index - 1;                 // строка-заголовок раздела
        Check("над позицией есть заголовок раздела", true,
              headerRow >= 0 && grid.Rows[headerRow].Tag == null);

        ToggleClick(editor, grid, headerRow, ColToggle);
        Check("раздел свёрнут", true, CountVisible(grid) < visibleBefore);
        Check("заголовок раздела остался виден", true, grid.Rows[headerRow].Visible);
        Check("знак сменился на «+»", "+",
              Convert.ToString(grid.Rows[headerRow].Cells[ColToggle].Value));
        Check("курсор не остался на скрытой строке", true,
              grid.CurrentCell == null || grid.CurrentRow.Visible);

        ToggleClick(editor, grid, headerRow, ColToggle);
        Check("раздел развёрнут", visibleBefore, CountVisible(grid));
        Check("знак вернулся на «–»", "\u2013",
              Convert.ToString(grid.Rows[headerRow].Cells[ColToggle].Value));

        Console.WriteLine("[7г] Переименование всего раздела");
        int inSection = 0;
        foreach (PriceRow row in Rows(editor))
            if (row.Group == "Диагностика и осмотр") row.Group = "Диагностика";
        editor.RefreshGrid();
        foreach (PriceRow row in Rows(editor)) if (row.Group == "Диагностика") inSection++;
        Check("позиций в разделе", 6, inSection);   // 5 исходных + добавленная «Проверка фар»
        Check("переименование выполнено", true,
              editor.RenameGroupTo("Диагностика", "Диагностика и осмотр"));
        int renamedNow = 0, leftovers = 0;
        foreach (PriceRow row in Rows(editor))
        {
            if (row.Group == "Диагностика и осмотр") renamedNow++;
            if (row.Group == "Диагностика") leftovers++;
        }
        Check("переименованы все позиции раздела", 6, renamedNow);
        Check("старого названия не осталось", 0, leftovers);
        Check("пустое название раздела отклонено", false,
              editor.RenameGroupTo("Диагностика и осмотр", "   "));
        Console.WriteLine("[7д] В строке-заголовке раздела нет наименования услуги");
        int headerTotal = 0;
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.Tag != null) continue;
            headerTotal++;
            Check("раздел «" + Convert.ToString(r.Cells[ColGroup].Value) + "» без услуги",
                  "", Convert.ToString(r.Cells[ColName].Value));
            Check("раздел «" + Convert.ToString(r.Cells[ColGroup].Value) + "» без цены",
                  true, r.Cells[ColPrice].Value == null);
        }
        Check("заголовков разделов", 5, headerTotal);

        Console.WriteLine("[7е] Создание нового раздела");
        int sectionsBefore = headerTotal;
        editor.PendingSectionAnswer = "Кузовные работы";
        Call(editor, "AddSection");
        Check("разделов стало больше", sectionsBefore + 1, CountHeaders(grid));

        DataGridViewRow newHeader = null;
        for (int i = 0; i < grid.Rows.Count; i++)
            if (grid.Rows[i].Tag == null) newHeader = grid.Rows[i];
        Check("название нового раздела", "Кузовные работы",
              Convert.ToString(newHeader.Cells[ColGroup].Value));
        Check("в новом разделе нет чужой услуги", "",
              Convert.ToString(newHeader.Cells[ColName].Value));

        int inNewSection = 0;
        foreach (PriceRow row in Rows(editor)) if (row.Group == "Кузовные работы") inNewSection++;
        Check("позиция нового раздела создана", 1, inNewSection);

        Console.WriteLine("[7ж] Позиция добавляется в конец своего раздела");
        int newSectionRow = -1;
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            PriceRow candidate = grid.Rows[i].Tag as PriceRow;
            if (candidate != null && candidate.Group == "Кузовные работы") { newSectionRow = i; break; }
        }
        Check("позиция нового раздела найдена", true, newSectionRow > 0);
        grid.ClearSelection();
        grid.Rows[newSectionRow].Selected = true;
        grid.CurrentCell = grid.Rows[newSectionRow].Cells[ColName];
        Call(editor, "AddRows");
        inNewSection = 0;
        foreach (PriceRow row in Rows(editor)) if (row.Group == "Кузовные работы") inNewSection++;
        Check("позиций в новом разделе", 2, inNewSection);
        Check("число разделов не изменилось", sectionsBefore + 1, CountHeaders(grid));
        Console.WriteLine("[7з] Артикул и выбор единицы измерения");
        Check("колонка артикула подписана", "Артикул",
              grid.Columns[ColArticle].HeaderText);
        Check("единица измерения — выпадающий список", true,
              grid.Columns[ColUnit] is DataGridViewComboBoxColumn);

        DataGridViewComboBoxColumn unitColumn = (DataGridViewComboBoxColumn)grid.Columns[ColUnit];
        Check("единица 1", "шт.", unitColumn.Items[0]);
        Check("единица 2", "услуга", unitColumn.Items[1]);
        Check("единица 3", "литр", unitColumn.Items[2]);
        Check("единица 4", "комплект", unitColumn.Items[3]);
        Check("перечень начинается с четырёх основных единиц", true, unitColumn.Items.Count >= 4);

        // вписываем артикул и выбираем единицу из перечня у первой позиции
        DataGridViewRow articleRow = null;
        foreach (DataGridViewRow r in grid.Rows) if (r.Tag is PriceRow) { articleRow = r; break; }
        PriceRow firstData = (PriceRow)articleRow.Tag;

        grid.CurrentCell = articleRow.Cells[ColArticle];
        grid.BeginEdit(true);
        articleRow.Cells[ColArticle].Value = "ART-001";
        grid.EndEdit();
        Check("артикул записан", "ART-001", firstData.Article);

        grid.CurrentCell = articleRow.Cells[ColUnit];
        grid.BeginEdit(true);
        articleRow.Cells[ColUnit].Value = "литр";
        grid.EndEdit();
        Check("единица выбрана из перечня", "литр", firstData.Unit);

        // своя единица измерения не должна ломать таблицу
        PriceRow customUnit = null;
        foreach (PriceRow row in Rows(editor))
            if (row.Unit != "шт." && row.Unit != "услуга" && row.Unit != "литр" && row.Unit != "комплект")
            { customUnit = row; break; }
        Console.WriteLine("        позиция с прежней единицей: " +
                          (customUnit == null ? "нет" : customUnit.Unit));
        Check("список единиц принимает значение из данных", true, unitColumn.Items.Count >= 4);

        Console.WriteLine("[8] Сохранение во внутреннее хранилище");
        string error;
        List<ServiceItem> before8 = PriceBook.Load(out error);
        Check("до сохранения в хранилище нет новой услуги", false,
              Contains(before8, "Проверка фар"));
        Call(editor, "SaveChanges");
        Check("редактор закрылся с результатом OK", DialogResult.OK, editor.DialogResult);
        Check("хранилище обновлено", true, File.Exists(PriceBook.StorePath));
        List<ServiceItem> saved = PriceBook.Load(out error);
        Check("новая услуга сохранена", true, Contains(saved, "Проверка фар"));
        Check("переименованная услуга сохранена", true,
              Contains(saved, "Компьютерная диагностика (новая редакция)"));
        Check("позиций в хранилище", 39, saved.Count);   // 36 + 2 в новом разделе + 1 добавленная

        bool articleSaved = false;
        foreach (ServiceItem item in saved)
            if (item.Article == "ART-001" && item.Unit == "литр") articleSaved = true;
        Check("артикул и выбранная единица сохранены", true, articleSaved);
        Check("ошибок чтения нет", true, string.IsNullOrEmpty(error));

        Console.WriteLine("[8б] Выгрузка и загрузка через файл (обмен с Excel)");
        string exchange = Path.Combine(dir, "Обмен.tsv");
        PriceBook.ExportTsv(exchange, saved);
        Check("файл выгрузки создан", true, File.Exists(exchange));
        List<ServiceItem> imported = PriceBook.ImportTsv(exchange);
        Check("позиций в выгруженном файле", 39, imported.Count);
        Check("наименование сохранилось", true, Contains(imported, "Проверка фар"));
        Check("цена сохранилась", 300m, PriceOf(imported, "Проверка фар"));
        File.Delete(exchange);

        Console.WriteLine("[9] Главное окно подхватывает изменённый прайс-лист");
        main.ReloadPricesPreserving();
        Check("позиций в главном окне", 39, Get<List<EstimateRow>>(main, "_rows").Count);
        Check("новая услуга видна", true, Find(main, "Проверка фар") != null);
        Check("цена новой услуги", 300m, Find(main, "Проверка фар").Item.Price);

        Console.WriteLine("[10] Обновление цен бережёт отметки и количество");
        EstimateRow target = Find(main, "Компьютерная диагностика (новая редакция)");
        Check("позиция найдена", true, target != null);
        target.Selected = true;
        target.Quantity = 2m;
        main.RefreshGrid();
        Check("итог до изменения цены", 3000m, SumSelected(main));

        // меняем цену прямо в хранилище: 1500 -> 1800
        List<ServiceItem> stored = PriceBook.Load(out error);
        bool priceChanged = false;
        foreach (ServiceItem item in stored)
        {
            if (item.Name != "Компьютерная диагностика (новая редакция)") continue;
            Check("цена до изменения", 1500m, item.Price);
            item.Price = 1800m;
            priceChanged = true;
        }
        Check("позиция найдена в хранилище", true, priceChanged);
        PriceBook.Save(stored);
        main.ReloadPricesPreserving();

        EstimateRow updated = Find(main, "Компьютерная диагностика (новая редакция)");
        Check("новая цена применена", 1800m, updated.Item.Price);
        Check("отметка сохранена", true, updated.Selected);
        Check("количество сохранено", 2m, updated.Quantity);
        Check("итог пересчитан", 3600m, SumSelected(main));
        Check("подпись итога", "ИТОГО: 3\u00A0600,00 \u20BD", Total(main).Text);

        Console.WriteLine("[11] Поиск в главном окне после перезагрузки");
        TextBox find = Get<TextBox>(main, "_find");
        find.Text = "Проверка фар";
        int visibleItems = 0;
        int visibleHeaders = 0;
        foreach (DataGridViewRow row in Grid(main).Rows)
        {
            if (!row.Visible) continue;
            if (row.Tag is EstimateRow) visibleItems++; else visibleHeaders++;
        }
        // поиск показывает раздел целиком, чтобы услугу можно было отметить в контексте
        int inGroup = 0;
        foreach (EstimateRow row in Get<List<EstimateRow>>(main, "_rows"))
            if (row.Item.Group == Find(main, "Проверка фар").Item.Group) inGroup++;
        Check("под поиск попал раздел найденной услуги", inGroup, visibleItems);
        Check("виден один раздел", 1, visibleHeaders);
        Check("остальные позиции скрыты", true, visibleItems < 37);
        find.Text = "";
        Check("отметки сохранились", 3600m, SumSelected(main));

        // ------------------------------------------------ очистка прайс-листа
        Console.WriteLine();
        Console.WriteLine("[12] Очистка прайс-листа");

        PriceEditorForm wiper = new PriceEditorForm();
        wiper.Show();
        wiper.PendingClearAnswer = "почти";
        Invoke(wiper, "ClearAllRows");
        Check("неверное слово не очищает", true, Rows(wiper).Count > 0);

        wiper.PendingClearAnswer = "ПОЛНОСТЬЮ";
        Invoke(wiper, "ClearAllRows");
        Check("прайс-лист очищен", 0, Rows(wiper).Count);
        Check("строк в таблице нет", 0, Grid(wiper).Rows.Count);

        // проверяем строку состояния: она должна объяснить, что делать дальше
        ToolStripStatusLabel clearStatus = Get<ToolStripStatusLabel>(wiper, "_statusText");
        Check("подсказка сохранения после очистки", true,
              clearStatus.Text.Contains("Сохранить"));

        // очистка ещё не записана — в хранилище цены на месте
        string clearError;
        Check("до сохранения хранилище не тронуто", true, PriceBook.Load(out clearError).Count > 0);

        wiper.Dispose();

        // сохранение пустого прайс-листа: пишем в хранилище напрямую тем же способом, что и редактор
        PriceBook.Save(new List<ServiceItem>());
        Check("пустой прайс-лист записан", 0, PriceBook.Load(out clearError).Count);

        main.ReloadPricesPreserving();
        Check("в главном окне нет позиций", 0, Get<List<EstimateRow>>(main, "_rows").Count);
        Check("итог после очистки нулевой", Total(main).Text.StartsWith("ИТОГО: 0,00"), true);

        // редактор должен открываться и на пустом прайс-листе
        PriceEditorForm emptyEditor = new PriceEditorForm();
        emptyEditor.Show();
        Check("редактор открылся на пустом прайсе", 0, Rows(emptyEditor).Count);
        emptyEditor.PendingSectionAnswer = "Новый раздел";
        Invoke(emptyEditor, "AddSection");
        Check("раздел добавляется в пустой прайс", 1, Rows(emptyEditor).Count);
        emptyEditor.Dispose();

        // возвращаем заводской прайс-лист
        PriceEditorForm restored = new PriceEditorForm();
        restored.Show();
        restored.PendingRestoreAnswer = true;
        Invoke(restored, "RestoreFactoryPrices");
        Check("заводской прайс вернулся", true, Rows(restored).Count > 30);
        restored.Dispose();

        Check("заводской прайс записан в хранилище", true, PriceBook.Load(out clearError).Count > 30);

        main.ReloadPricesPreserving();
        Check("главное окно снова с позициями", true, Get<List<EstimateRow>>(main, "_rows").Count > 30);

        main.Dispose();
        editor.Dispose();
        if (File.Exists(cfg)) File.Delete(cfg);

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ");
            Environment.Exit(0);
        }
        Console.WriteLine("ПРОВАЛЕНО ПРОВЕРОК: " + _failures);
        Environment.Exit(1);
    }

    /// <summary>Щелчок по знаку «–/+»: вызывается тот же обработчик, что и в программе.</summary>
    private static object Invoke(PriceEditorForm editor, string method, params object[] arguments)
    {
        MethodInfo info = typeof(PriceEditorForm).GetMethod(method, Hidden);
        if (info == null) throw new InvalidOperationException("Метод не найден: " + method);
        return info.Invoke(editor, arguments);
    }

    private static void ToggleClick(PriceEditorForm editor, DataGridView grid, int row, int column)
    {
        MethodInfo info = typeof(PriceEditorForm).GetMethod("Grid_CellClick", Hidden);
        info.Invoke(editor, new object[] { grid, new DataGridViewCellEventArgs(column, row) });
    }

    private static int CountHeaders(DataGridView grid)
    {
        int n = 0;
        foreach (DataGridViewRow row in grid.Rows) if (row.Tag == null) n++;
        return n;
    }

    private static int CountVisible(DataGridView grid)
    {
        int n = 0;
        foreach (DataGridViewRow row in grid.Rows) if (row.Visible) n++;
        return n;
    }

    private static decimal PriceOf(List<ServiceItem> items, string name)
    {
        foreach (ServiceItem item in items) if (item.Name == name) return item.Price;
        return -1m;
    }

    private static int CountNamed(PriceEditorForm editor, string name)
    {
        int n = 0;
        foreach (PriceRow row in Rows(editor)) if (row.Name == name) n++;
        return n;
    }

    private static bool Contains(List<ServiceItem> items, string name)
    {
        foreach (ServiceItem item in items) if (item.Name == name) return true;
        return false;
    }
}
