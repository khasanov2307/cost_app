// Рендерит окна программы в PNG для визуальной проверки без ручного запуска.
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KotovCalc;

internal static class Render
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        string mode = args.Length > 0 ? args[0] : "main";
        string output = args.Length > 1 ? args[1] : "shot.png";
        string dir = AppDomain.CurrentDomain.BaseDirectory;

        if (mode == "editor")
        {
            // своё хранилище, чтобы не трогать данные пользователя
            string storeDir = Path.Combine(Path.GetTempPath(), "kotov-render");
            if (Directory.Exists(storeDir)) Directory.Delete(storeDir, true);
            Directory.CreateDirectory(storeDir);
            PriceBook.StorePath = Path.Combine(storeDir, "prices.xml");
            PriceBook.Save(PriceBook.ReadSeed());

            PriceEditorForm editor = new PriceEditorForm();
            editor.StartPosition = FormStartPosition.Manual;
            editor.Location = new Point(0, 0);
            editor.Show();
            Application.DoEvents();

            DataGridView grid = (DataGridView)typeof(PriceEditorForm).GetField("_grid", Hidden).GetValue(editor);
            TextBox find = (TextBox)typeof(PriceEditorForm).GetField("_find", Hidden).GetValue(editor);

            if (args.Length > 2 && args[2] == "added")
            {
                // имитируем нажатие «Добавить»: новая позиция должна подсветиться
                foreach (DataGridViewRow r in grid.Rows)
                    if (r.Tag is PriceRow) { grid.CurrentCell = r.Cells[1]; break; }
                Application.DoEvents();
                typeof(PriceEditorForm).GetMethod("AddRows",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                Application.DoEvents();
            }
            if (args.Length > 3) find.Text = args[3];

            Application.DoEvents();
            editor.Refresh();
            Application.DoEvents();




            using (Bitmap bitmap = new Bitmap(editor.Width, editor.Height))
            {
                editor.DrawToBitmap(bitmap, new Rectangle(0, 0, editor.Width, editor.Height));
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("saved: " + Path.GetFullPath(output));
            editor.Close();
            return;
        }

        string mainStore = Path.Combine(Path.GetTempPath(), "kotov-render-main");
        if (Directory.Exists(mainStore)) Directory.Delete(mainStore, true);
        Directory.CreateDirectory(mainStore);
        PriceBook.StorePath = Path.Combine(mainStore, "prices.xml");

        MainForm form = new MainForm();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Show();
        Application.DoEvents();

        DataGridView mainGrid = (DataGridView)typeof(MainForm).GetField("_grid", Hidden).GetValue(form);
        if (args.Length > 2 && args[2] == "marked")
        {
            foreach (DataGridViewRow row in mainGrid.Rows)
            {
                EstimateRow data = row.Tag as EstimateRow;
                if (data == null) continue;
                if (data.Item.Name.IndexOf("диагностика", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    data.Item.Name.IndexOf("колодок", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    data.Item.Name.IndexOf("масла", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row.Cells[0].Value = true;
                }
                if (data.Item.Name.IndexOf("Компьютерная", StringComparison.OrdinalIgnoreCase) >= 0)
                    row.Cells[4].Value = 2m;
            }
        }

        Application.DoEvents();
        form.Refresh();
        Application.DoEvents();

        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("saved: " + Path.GetFullPath(output));
        form.Close();
    }
}