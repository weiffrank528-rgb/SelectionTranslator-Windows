using System;
using System.Drawing;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal enum ExistingInstanceAction
    {
        Cancel,
        OpenSettings,
        Restart
    }

    internal sealed class ExistingInstanceForm : Form
    {
        internal ExistingInstanceAction SelectedAction { get; private set; }

        internal ExistingInstanceForm()
        {
            Text = "划词翻译";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ClientSize = new Size(450, 190);
            BackColor = Color.FromArgb(27, 31, 39);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(402, 34),
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(99, 220, 155),
                Text = "划词翻译已经在运行"
            };
            var description = new Label
            {
                AutoSize = false,
                Location = new Point(25, 64),
                Size = new Size(400, 42),
                ForeColor = Color.FromArgb(190, 198, 210),
                Text = "可以打开现有实例的设置，或者安全退出后重新启动。"
            };

            var openButton = CreateButton("打开设置", new Point(24, 126), Color.FromArgb(48, 106, 160));
            openButton.Click += delegate { Finish(ExistingInstanceAction.OpenSettings); };
            var restartButton = CreateButton("重新启动", new Point(164, 126), Color.FromArgb(48, 135, 102));
            restartButton.Click += delegate { Finish(ExistingInstanceAction.Restart); };
            var cancelButton = CreateButton("取消", new Point(304, 126), Color.FromArgb(59, 65, 77));
            cancelButton.Click += delegate { Finish(ExistingInstanceAction.Cancel); };

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(openButton);
            Controls.Add(restartButton);
            Controls.Add(cancelButton);
            AcceptButton = openButton;
            CancelButton = cancelButton;
            SelectedAction = ExistingInstanceAction.Cancel;
        }

        private static Button CreateButton(string text, Point location, Color color)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(122, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void Finish(ExistingInstanceAction action)
        {
            SelectedAction = action;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
