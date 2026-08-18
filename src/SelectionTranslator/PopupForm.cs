using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class PopupForm : Form
    {
        private const int PopupWidth = 590;
        private const int OuterPadding = 18;
        private const int CardWidth = PopupWidth - OuterPadding * 2;
        private const int CardInnerWidth = CardWidth - 28;

        private static readonly Color WindowColor = Color.FromArgb(24, 27, 34);
        private static readonly Color CardColor = Color.FromArgb(35, 40, 50);
        private static readonly Color BorderColor = Color.FromArgb(61, 69, 84);
        private static readonly Color PrimaryText = Color.FromArgb(245, 247, 250);
        private static readonly Color SecondaryText = Color.FromArgb(175, 184, 198);
        private static readonly Color AccentColor = Color.FromArgb(96, 180, 255);
        private static readonly Color SuccessColor = Color.FromArgb(99, 220, 155);

        private readonly Panel _accentBar;
        private readonly Label _header;
        private readonly Button _closeButton;
        private readonly Panel _sourceCard;
        private readonly Label _sourceTitle;
        private readonly Label _source;
        private readonly Button _speakButton;
        private readonly Button _copyButton;
        private readonly Label _translationTitle;
        private readonly Button _copyTranslationButton;
        private readonly Label _result;
        private readonly Label _footer;
        private readonly Timer _hideTimer;

        private Point _anchor;
        private string _fullSourceText = "";
        private string _fullTranslationText = "";
        private string _baseFooterText = "";
        private int _autoHideMilliseconds;
        private bool _isSpeaking;

        internal event Action UserDismissed;
        internal event Action<string> ReadOriginalRequested;

        internal PopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = WindowColor;
            ForeColor = PrimaryText;
            Width = PopupWidth;
            Opacity = 0.98;
            DoubleBuffered = true;

            _accentBar = new Panel
            {
                BackColor = AccentColor,
                Location = new Point(0, 0),
                Size = new Size(PopupWidth, 4)
            };
            _header = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = SuccessColor,
                Location = new Point(OuterPadding, 14),
                Size = new Size(500, 29),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "划词翻译"
            };
            _closeButton = CreateFlatButton("×", new Size(34, 32));
            _closeButton.Font = new Font("Segoe UI", 14F, FontStyle.Regular);
            _closeButton.Location = new Point(PopupWidth - OuterPadding - _closeButton.Width, 10);
            _closeButton.BackColor = WindowColor;
            _closeButton.ForeColor = SecondaryText;
            _closeButton.Click += delegate { RequestUserDismiss(); };

            _sourceCard = new Panel
            {
                BackColor = CardColor,
                Location = new Point(OuterPadding, 54),
                Size = new Size(CardWidth, 100)
            };
            _sourceTitle = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = SecondaryText,
                Location = new Point(14, 12),
                Size = new Size(250, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "原文"
            };
            _speakButton = CreateFlatButton("▶  朗读原文", new Size(126, 34));
            _speakButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _speakButton.Location = new Point(CardWidth - 14 - _speakButton.Width, 10);
            _speakButton.BackColor = Color.FromArgb(46, 104, 158);
            _speakButton.ForeColor = Color.White;
            _speakButton.Click += delegate
            {
                var handler = ReadOriginalRequested;
                if (handler != null) handler(_fullSourceText);
            };
            _copyButton = CreateFlatButton("复制原文", new Size(108, 34));
            _copyButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _copyButton.Location = new Point(_speakButton.Left - 8 - _copyButton.Width, 10);
            _copyButton.BackColor = Color.FromArgb(59, 65, 77);
            _copyButton.ForeColor = Color.White;
            _copyButton.Click += delegate { CopyOriginalToClipboard(); };
            _source = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
                ForeColor = PrimaryText,
                Location = new Point(14, 51),
                Size = new Size(CardInnerWidth, 34),
                TextAlign = ContentAlignment.TopLeft
            };
            _sourceCard.Controls.Add(_sourceTitle);
            _sourceCard.Controls.Add(_copyButton);
            _sourceCard.Controls.Add(_speakButton);
            _sourceCard.Controls.Add(_source);

            _translationTitle = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = AccentColor,
                Location = new Point(OuterPadding + 2, 172),
                Size = new Size(CardWidth - 4, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "中文翻译"
            };
            _copyTranslationButton = CreateFlatButton("复制译文", new Size(108, 32));
            _copyTranslationButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _copyTranslationButton.BackColor = Color.FromArgb(59, 65, 77);
            _copyTranslationButton.ForeColor = Color.White;
            _copyTranslationButton.Click += delegate { CopyTranslationToClipboard(); };
            _result = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Regular),
                ForeColor = PrimaryText,
                Location = new Point(OuterPadding + 2, 202),
                Size = new Size(CardWidth - 4, 56),
                TextAlign = ContentAlignment.TopLeft
            };
            _footer = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = SecondaryText,
                Location = new Point(OuterPadding + 2, 270),
                Size = new Size(CardWidth - 4, 23),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.Add(_accentBar);
            Controls.Add(_header);
            Controls.Add(_closeButton);
            Controls.Add(_sourceCard);
            Controls.Add(_translationTitle);
            Controls.Add(_copyTranslationButton);
            Controls.Add(_result);
            Controls.Add(_footer);

            _hideTimer = new Timer();
            _hideTimer.Tick += delegate { HideImmediately(); };
            AttachHoverPause(this);
            _speakButton.MouseEnter += delegate
            {
                if (_speakButton.Enabled)
                    _speakButton.BackColor = _isSpeaking ? Color.FromArgb(188, 85, 85) : Color.FromArgb(55, 124, 188);
            };
            _speakButton.MouseLeave += delegate
            {
                if (_speakButton.Enabled)
                    _speakButton.BackColor = _isSpeaking ? Color.FromArgb(166, 75, 75) : Color.FromArgb(46, 104, 158);
            };
            StyleButtonHover(_closeButton, WindowColor, Color.FromArgb(52, 57, 68));
            StyleButtonHover(_copyButton, Color.FromArgb(59, 65, 77), Color.FromArgb(75, 83, 98));
            StyleButtonHover(_copyTranslationButton, Color.FromArgb(59, 65, 77), Color.FromArgb(75, 83, 98));
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return parameters;
            }
        }

        internal bool ContainsScreenPoint(Point point)
        {
            return Visible && Bounds.Contains(point);
        }

        internal void HideImmediately()
        {
            _hideTimer.Stop();
            Hide();
        }

        internal void ShowLoading(string source, string engine, string sourceLanguage, string targetLanguage,
            bool automaticallyDetected, Point anchor)
        {
            _anchor = anchor;
            _fullSourceText = source ?? "";
            _fullTranslationText = "";
            _autoHideMilliseconds = 0;
            _isSpeaking = false;
            _header.Text = engine;
            _header.ForeColor = AccentColor;
            SetLanguageLabels(sourceLanguage, targetLanguage, automaticallyDetected);
            _source.Text = Compact(_fullSourceText, 700);
            _result.Text = "正在翻译，请稍候…";
            _copyTranslationButton.Enabled = false;
            _baseFooterText = "正在获取译文";
            _footer.Text = _baseFooterText;
            ResetSpeechButton();
            LayoutContent();
            ShowNearAnchor();
            _hideTimer.Stop();
        }

        internal void ShowResult(string source, string translation, string engine, string readMethod,
            string sourceLanguage, string targetLanguage, bool automaticallyDetected, Point anchor, int autoHideMilliseconds)
        {
            _anchor = anchor;
            _fullSourceText = source ?? "";
            _fullTranslationText = translation ?? "";
            _autoHideMilliseconds = autoHideMilliseconds;
            _isSpeaking = false;
            _header.Text = engine;
            _header.ForeColor = SuccessColor;
            SetLanguageLabels(sourceLanguage, targetLanguage, automaticallyDetected);
            _source.Text = Compact(_fullSourceText, 700);
            _result.Text = _fullTranslationText;
            _copyTranslationButton.Enabled = !string.IsNullOrWhiteSpace(_fullTranslationText);
            _baseFooterText = readMethod + " · 悬停暂停隐藏 · 点击外部关闭";
            _footer.Text = _baseFooterText;
            ResetSpeechButton();
            LayoutContent();
            ShowNearAnchor();
            RestartHideTimer();
        }

        internal void ShowError(string message, Point anchor, int autoHideMilliseconds)
        {
            _anchor = anchor;
            _fullSourceText = "";
            _fullTranslationText = "";
            _autoHideMilliseconds = autoHideMilliseconds;
            _isSpeaking = false;
            _header.Text = "翻译失败";
            _header.ForeColor = Color.FromArgb(255, 137, 137);
            _sourceTitle.Text = "提示";
            _translationTitle.Text = "错误详情";
            _source.Text = "请检查网络或在托盘菜单中打开设置。";
            _result.Text = message;
            _copyTranslationButton.Enabled = false;
            _baseFooterText = "点击 × 或浮窗外部关闭";
            _footer.Text = _baseFooterText;
            ResetSpeechButton();
            LayoutContent();
            ShowNearAnchor();
            RestartHideTimer();
        }

        private void SetLanguageLabels(string sourceLanguage, string targetLanguage, bool automaticallyDetected)
        {
            var sourceName = LanguageDetection.DisplayName(sourceLanguage);
            _sourceTitle.Text = automaticallyDetected ? "原文 · 自动识别：" + sourceName : "原文 · " + sourceName;
            _translationTitle.Text = LanguageDetection.TranslationTitle(targetLanguage);
        }

        internal void SetSpeechState(bool isSpeaking, string message)
        {
            _isSpeaking = isSpeaking;
            _speakButton.Text = isSpeaking ? "■  停止朗读" : "▶  朗读原文";
            _speakButton.BackColor = isSpeaking ? Color.FromArgb(166, 75, 75) : Color.FromArgb(46, 104, 158);
            _footer.Text = string.IsNullOrWhiteSpace(message) ? _baseFooterText : message;
            if (isSpeaking) _hideTimer.Stop();
            else RestartHideTimer();
        }

        private void ResetSpeechButton()
        {
            _speakButton.Text = "▶  朗读原文";
            _speakButton.BackColor = Color.FromArgb(46, 104, 158);
            _speakButton.Enabled = !string.IsNullOrWhiteSpace(_fullSourceText);
            _copyButton.Enabled = !string.IsNullOrWhiteSpace(_fullSourceText);
        }

        private void CopyOriginalToClipboard()
        {
            if (string.IsNullOrWhiteSpace(_fullSourceText)) return;
            try
            {
                Clipboard.SetText(_fullSourceText);
                _footer.Text = "原文已复制到剪贴板";
                RestartHideTimer();
            }
            catch (ExternalException)
            {
                _footer.Text = "剪贴板正被其他程序占用，请稍后重试";
            }
        }

        private void CopyTranslationToClipboard()
        {
            if (string.IsNullOrWhiteSpace(_fullTranslationText)) return;
            try
            {
                Clipboard.SetText(_fullTranslationText);
                _footer.Text = "译文已复制到剪贴板";
                RestartHideTimer();
            }
            catch (ExternalException)
            {
                _footer.Text = "剪贴板正被其他程序占用，请稍后重试";
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) && !string.IsNullOrWhiteSpace(_fullSourceText))
            {
                CopyOriginalToClipboard();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void WndProc(ref Message message)
        {
            const int WmMouseActivate = 0x0021;
            const int MaNoActivate = 3;
            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(MaNoActivate);
                return;
            }
            base.WndProc(ref message);
        }

        private void RequestUserDismiss()
        {
            HideImmediately();
            var handler = UserDismissed;
            if (handler != null) handler();
        }

        private void LayoutContent()
        {
            var sourceMeasured = TextRenderer.MeasureText(_source.Text ?? "", _source.Font,
                new Size(CardInnerWidth, 150), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var sourceHeight = Math.Max(32, Math.Min(132, sourceMeasured.Height + 9));
            _source.Size = new Size(CardInnerWidth, sourceHeight);
            _sourceCard.Size = new Size(CardWidth, _source.Bottom + 14);

            _translationTitle.Location = new Point(OuterPadding + 2, _sourceCard.Bottom + 16);
            _translationTitle.Size = new Size(CardWidth - _copyTranslationButton.Width - 16, 24);
            _copyTranslationButton.Location = new Point(
                PopupWidth - OuterPadding - 2 - _copyTranslationButton.Width,
                _translationTitle.Top - 4);
            _result.Location = new Point(OuterPadding + 2, _translationTitle.Bottom + 7);
            var resultMeasured = TextRenderer.MeasureText(_result.Text ?? "", _result.Font,
                new Size(CardWidth - 4, 410), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var resultHeight = Math.Max(52, Math.Min(390, resultMeasured.Height + 12));
            _result.Size = new Size(CardWidth - 4, resultHeight);

            _footer.Location = new Point(OuterPadding + 2, _result.Bottom + 12);
            ClientSize = new Size(PopupWidth, Math.Max(250, _footer.Bottom + 15));
            ApplyRoundedRegion();
        }

        private void ShowNearAnchor()
        {
            var screen = Screen.FromPoint(_anchor).WorkingArea;
            var x = _anchor.X + 14;
            var y = _anchor.Y + 16;
            if (x + Width > screen.Right) x = _anchor.X - Width - 14;
            if (y + Height > screen.Bottom) y = _anchor.Y - Height - 14;
            x = Math.Max(screen.Left, Math.Min(x, screen.Right - Width));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - Height));
            Location = new Point(x, y);
            if (!Visible) Show();
            else Invalidate();
        }

        private void RestartHideTimer()
        {
            _hideTimer.Stop();
            if (_isSpeaking || _autoHideMilliseconds <= 0 || Bounds.Contains(Cursor.Position)) return;
            _hideTimer.Interval = Math.Max(500, _autoHideMilliseconds);
            _hideTimer.Start();
        }

        private void AttachHoverPause(Control control)
        {
            control.MouseEnter += delegate { _hideTimer.Stop(); };
            control.MouseLeave += delegate
            {
                BeginInvoke((Action)delegate
                {
                    if (!Bounds.Contains(Cursor.Position)) RestartHideTimer();
                });
            };
            foreach (Control child in control.Controls) AttachHoverPause(child);
        }

        private static Button CreateFlatButton(string text, Size size)
        {
            var button = new Button
            {
                Text = text,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(69, 78, 94);
            return button;
        }

        private static void StyleButtonHover(Button button, Color normal, Color hover)
        {
            button.MouseEnter += delegate { if (button.Enabled) button.BackColor = hover; };
            button.MouseLeave += delegate { if (button.Enabled) button.BackColor = normal; };
        }

        private void ApplyRoundedRegion()
        {
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), 12))
            {
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter - 1, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter - 1, rectangle.Bottom - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (var pen = new Pen(BorderColor, 1F))
                eventArgs.Graphics.DrawPath(pen, path);
        }

        private static string Compact(string text, int maxLength)
        {
            var compact = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length <= maxLength ? compact : compact.Substring(0, maxLength) + "…";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hideTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
