// ---------------------------------------------------------------------------
//  Окно текста: чек, справка о клавишах и другие текстовые документы.
//  Текст можно скопировать и сохранить файлом.
// ---------------------------------------------------------------------------

using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KotovCalc
{
    internal sealed class TextForm : Form
    {
        private readonly TextBox _text;
        private readonly string _fileName;
        private readonly string _folder;

        public TextForm(string title, string text, string fileName, string folder)
        {
            _fileName = fileName;
            _folder = folder;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 420);
            ClientSize = new Size(560, 560);
            Font = new Font("Consolas", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = false;

            _text = new TextBox();
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.ScrollBars = ScrollBars.Both;
            _text.WordWrap = false;
            _text.Dock = DockStyle.Fill;
            _text.Font = Font;
            _text.BackColor = Color.White;
            _text.Text = text;
            _text.Select(0, 0);
            Controls.Add(_text);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 44;
            Controls.Add(bottom);

            Button copy = MakeButton("Копировать", 130);
            copy.Location = new Point(12, 8);
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(_text.Text);
                    MessageBox.Show(this, "Текст скопирован в буфер обмена.",
                        title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось скопировать:\n" + ex.Message,
                        title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            bottom.Controls.Add(copy);

            if (_fileName != null && _folder != null)
            {
                Button save = MakeButton("Сохранить файлом", 170);
                save.Location = new Point(150, 8);
                save.Click += delegate { SaveFile(title); };
                bottom.Controls.Add(save);
            }

            Button close = MakeButton("Закрыть", 110);
            close.Location = new Point(ClientSize.Width - 122, 8);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.DialogResult = DialogResult.OK;
            bottom.Controls.Add(close);

            CancelButton = close;
        }

        private static Button MakeButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 28;
            button.FlatStyle = FlatStyle.System;
            button.Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            return button;
        }

        private void SaveFile(string title)
        {
            try
            {
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);

                string path = Path.Combine(_folder, _fileName);
                File.WriteAllText(path, _text.Text, new UTF8Encoding(true));

                MessageBox.Show(this, "Файл сохранён:\n" + path,
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить файл:\n" + ex.Message,
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
