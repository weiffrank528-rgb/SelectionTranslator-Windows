using System;
using System.Drawing;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _working;
        private readonly CheckBox _enabled = new CheckBox();
        private readonly ComboBox _engine = new ComboBox();
        private readonly TextBox _sourceLanguage = new TextBox();
        private readonly TextBox _targetLanguage = new TextBox();
        private readonly NumericUpDown _minCharacters = NumberBox(1, 100, 2);
        private readonly NumericUpDown _maxCharacters = NumberBox(50, 10000, 1500);
        private readonly NumericUpDown _dragMilliseconds = NumberBox(0, 1500, 60);
        private readonly NumericUpDown _dragPixels = NumberBox(1, 50, 5);
        private readonly NumericUpDown _selectionDelay = NumberBox(0, 2000, 90);
        private readonly NumericUpDown _uiaTimeout = NumberBox(100, 3000, 500);
        private readonly NumericUpDown _autoHideSeconds = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 300,
            DecimalPlaces = 1,
            Increment = 0.5M,
            Value = 6.5M
        };
        private readonly CheckBox _hideOnOutsideClick = new CheckBox();
        private readonly CheckBox _clipboardFallback = new CheckBox();
        private readonly CheckBox _wpsCompatibility = new CheckBox();
        private readonly TextBox _whitelist = new TextBox();
        private readonly TextBox _blacklist = new TextBox();
        private readonly TextBox _myMemoryEmail = new TextBox();
        private readonly TextBox _googleKey = new TextBox();
        private readonly TextBox _googleEndpoint = new TextBox();
        private readonly TextBox _openAIKey = new TextBox();
        private readonly TextBox _openAIEndpoint = new TextBox();
        private readonly TextBox _openAIModel = new TextBox();
        private readonly TextBox _deepLKey = new TextBox();
        private readonly TextBox _deepLEndpoint = new TextBox();

        internal AppSettings SavedSettings { get; private set; }

        internal SettingsForm(AppSettings settings)
        {
            _working = settings.Clone();
            Text = "划词翻译设置";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(680, 720);
            MinimumSize = new Size(620, 600);
            Font = new Font("Microsoft YaHei UI", 9F);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildGeneralTab());
            tabs.TabPages.Add(BuildEnginesTab());

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };
            var save = new Button { Text = "保存", Width = 90, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "取消", Width = 90, DialogResult = DialogResult.Cancel };
            save.Click += SaveClicked;
            buttonPanel.Controls.Add(save);
            buttonPanel.Controls.Add(cancel);

            Controls.Add(tabs);
            Controls.Add(buttonPanel);
            AcceptButton = save;
            CancelButton = cancel;
            LoadValues();
        }

        private TabPage BuildGeneralTab()
        {
            var tab = new TabPage("常规与取词") { AutoScroll = true };
            var table = NewTable();
            AddRow(table, "启用自动划词翻译", _enabled);
            AddRow(table, "翻译引擎", _engine);
            _engine.DropDownStyle = ComboBoxStyle.DropDownList;
            _engine.Items.AddRange(new object[] { "MyMemory", "Google", "OpenAI", "DeepL" });
            AddRow(table, "源语言", _sourceLanguage, "MyMemory 建议填 en；Google/DeepL 填 auto 可自动识别；OpenAI 会按内容理解。");
            AddRow(table, "目标语言", _targetLanguage, "默认 zh-CN（简体中文）");
            AddRow(table, "最少字符数", _minCharacters);
            AddRow(table, "最多字符数", _maxCharacters);
            AddRow(table, "最短拖动时间（毫秒）", _dragMilliseconds);
            AddRow(table, "最小拖动距离（像素）", _dragPixels);
            AddRow(table, "松手后等待（毫秒）", _selectionDelay, "让目标应用先完成选区提交。");
            AddRow(table, "UI Automation 超时（毫秒）", _uiaTimeout);
            AddRow(table, "浮窗停留时间（秒）", _autoHideSeconds, "例如 6.5 表示停留 6.5 秒；填 0 表示不按时间自动隐藏。");
            _hideOnOutsideClick.Text = "点击浮窗以外的任意位置时立即隐藏";
            _hideOnOutsideClick.AutoSize = true;
            AddRow(table, "点击外部时隐藏", _hideOnOutsideClick, "默认开启。只观察点击，不拦截，也不会影响目标应用或下一次划词。");
            _clipboardFallback.Text = "UI Automation 失败时模拟 Ctrl+C，并安全恢复原剪贴板";
            _clipboardFallback.AutoSize = true;
            AddRow(table, "剪贴板兜底", _clipboardFallback, "遇到无法安全快照的 OLE/GDI 格式时会跳过本次兜底，避免剪贴板损坏或程序崩溃。");
            _wpsCompatibility.Text = "为 WPS Writer / WPS PDF 启用增强取词兼容模式";
            _wpsCompatibility.AutoSize = true;
            AddRow(table, "WPS 兼容", _wpsCompatibility, "识别 WPS 同进程浮动工具窗，延长复制等待，并通过标准 OLE 对象暂存和恢复复杂剪贴板内容。");

            _whitelist.Multiline = true;
            _whitelist.Height = 55;
            _blacklist.Multiline = true;
            _blacklist.Height = 65;
            AddRow(table, "进程白名单", _whitelist, "留空表示允许全部。用逗号、分号或换行分隔，例如 chrome, WINWORD。");
            AddRow(table, "进程黑名单", _blacklist, "黑名单优先；无需写 .exe。");
            tab.Controls.Add(table);
            return tab;
        }

        private TabPage BuildEnginesTab()
        {
            var tab = new TabPage("翻译引擎") { AutoScroll = true };
            var table = NewTable();
            var notice = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = Color.FromArgb(150, 70, 20),
                Text = "隐私提示：公共/API 翻译会把所选文本发送给相应服务。不要在密码、密钥、私人消息等敏感内容上使用自动翻译。"
            };
            AddRow(table, "重要", notice);
            AddRow(table, "MyMemory 联系邮箱", _myMemoryEmail, "可留空。匿名每天 5,000 字符；提供有效邮箱每天 50,000 字符，并非无限量免费。");
            _googleKey.UseSystemPasswordChar = true;
            AddRow(table, "Google API Key", _googleKey, "使用 Google Cloud Translation Basic v2；需启用 Cloud Translation API 和结算账户。");
            AddRow(table, "Google API 地址", _googleEndpoint);
            _openAIKey.UseSystemPasswordChar = true;
            AddRow(table, "OpenAI API Key", _openAIKey);
            AddRow(table, "OpenAI Responses 地址", _openAIEndpoint);
            AddRow(table, "OpenAI 模型", _openAIModel);
            _deepLKey.UseSystemPasswordChar = true;
            AddRow(table, "DeepL API Key", _deepLKey);
            AddRow(table, "DeepL API 地址", _deepLEndpoint, "Free 账户通常使用 api-free.deepl.com。");
            tab.Controls.Add(table);
            return tab;
        }

        private static TableLayoutPanel NewTable()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(16),
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static void AddRow(TableLayoutPanel table, string labelText, Control control, string hint)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = labelText, AutoSize = true, Margin = new Padding(3, 8, 10, 8) };
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(3, 5, 3, string.IsNullOrEmpty(hint) ? 8 : 2);
            table.Controls.Add(label, 0, row);

            if (string.IsNullOrEmpty(hint))
            {
                table.Controls.Add(control, 1, row);
            }
            else
            {
                var panel = new FlowLayoutPanel
                {
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    Margin = Padding.Empty
                };
                control.Width = 400;
                var hintLabel = new Label
                {
                    Text = hint,
                    AutoSize = true,
                    MaximumSize = new Size(400, 0),
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(3, 0, 3, 8)
                };
                panel.Controls.Add(control);
                panel.Controls.Add(hintLabel);
                table.Controls.Add(panel, 1, row);
            }
        }

        private static void AddRow(TableLayoutPanel table, string labelText, Control control)
        {
            AddRow(table, labelText, control, null);
        }

        private static NumericUpDown NumberBox(int minimum, int maximum, int value)
        {
            return new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value, ThousandsSeparator = true };
        }

        private void LoadValues()
        {
            _enabled.Checked = _working.Enabled;
            _engine.SelectedItem = _working.Engine;
            if (_engine.SelectedIndex < 0) _engine.SelectedIndex = 0;
            _sourceLanguage.Text = _working.SourceLanguage;
            _targetLanguage.Text = _working.TargetLanguage;
            SetNumber(_minCharacters, _working.MinCharacters);
            SetNumber(_maxCharacters, _working.MaxCharacters);
            SetNumber(_dragMilliseconds, _working.MinDragMilliseconds);
            SetNumber(_dragPixels, _working.DragThresholdPixels);
            SetNumber(_selectionDelay, _working.SelectionDelayMilliseconds);
            SetNumber(_uiaTimeout, _working.UiaTimeoutMilliseconds);
            _autoHideSeconds.Value = Math.Min(_autoHideSeconds.Maximum,
                Math.Max(_autoHideSeconds.Minimum, _working.AutoHideMilliseconds / 1000M));
            _hideOnOutsideClick.Checked = _working.HideOnOutsideClick;
            _clipboardFallback.Checked = _working.EnableClipboardFallback;
            _wpsCompatibility.Checked = _working.EnableWpsCompatibility;
            _whitelist.Text = _working.Whitelist;
            _blacklist.Text = _working.Blacklist;
            _myMemoryEmail.Text = _working.MyMemoryEmail;
            _googleKey.Text = _working.GoogleApiKey;
            _googleEndpoint.Text = _working.GoogleEndpoint;
            _openAIKey.Text = _working.OpenAIApiKey;
            _openAIEndpoint.Text = _working.OpenAIEndpoint;
            _openAIModel.Text = _working.OpenAIModel;
            _deepLKey.Text = _working.DeepLApiKey;
            _deepLEndpoint.Text = _working.DeepLEndpoint;
        }

        private static void SetNumber(NumericUpDown box, int value)
        {
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        private void SaveClicked(object sender, EventArgs eventArgs)
        {
            if (string.IsNullOrWhiteSpace(_sourceLanguage.Text) || string.IsNullOrWhiteSpace(_targetLanguage.Text))
            {
                MessageBox.Show(this, "请填写源语言和目标语言。", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _working.Enabled = _enabled.Checked;
            _working.Engine = Convert.ToString(_engine.SelectedItem);
            _working.SourceLanguage = _sourceLanguage.Text.Trim();
            _working.TargetLanguage = _targetLanguage.Text.Trim();
            _working.MinCharacters = (int)_minCharacters.Value;
            _working.MaxCharacters = (int)_maxCharacters.Value;
            _working.MinDragMilliseconds = (int)_dragMilliseconds.Value;
            _working.DragThresholdPixels = (int)_dragPixels.Value;
            _working.SelectionDelayMilliseconds = (int)_selectionDelay.Value;
            _working.UiaTimeoutMilliseconds = (int)_uiaTimeout.Value;
            _working.AutoHideMilliseconds = Decimal.ToInt32(Decimal.Round(_autoHideSeconds.Value * 1000M, 0));
            _working.HideOnOutsideClick = _hideOnOutsideClick.Checked;
            _working.EnableClipboardFallback = _clipboardFallback.Checked;
            _working.EnableWpsCompatibility = _wpsCompatibility.Checked;
            _working.Whitelist = _whitelist.Text.Trim();
            _working.Blacklist = _blacklist.Text.Trim();
            _working.MyMemoryEmail = _myMemoryEmail.Text.Trim();
            _working.GoogleApiKey = _googleKey.Text.Trim();
            _working.GoogleEndpoint = _googleEndpoint.Text.Trim();
            _working.OpenAIApiKey = _openAIKey.Text.Trim();
            _working.OpenAIEndpoint = _openAIEndpoint.Text.Trim();
            _working.OpenAIModel = _openAIModel.Text.Trim();
            _working.DeepLApiKey = _deepLKey.Text.Trim();
            _working.DeepLEndpoint = _deepLEndpoint.Text.Trim();
            SavedSettings = _working;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
