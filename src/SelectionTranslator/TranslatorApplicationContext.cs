using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class TranslatorApplicationContext : ApplicationContext
    {
        private AppSettings _settings;
        private readonly InstanceCoordinator _instanceCoordinator;
        private readonly SelectionReader _selectionReader = new SelectionReader();
        private readonly PopupForm _popup = new PopupForm();
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _enabledMenuItem;
        private readonly GlobalMouseHook _mouseHook;
        private readonly System.Windows.Forms.Timer _foregroundMonitor;
        private readonly System.Windows.Forms.Timer _instanceRequestTimer;
        private OriginalTextSpeaker _speaker;
        private SettingsForm _settingsForm;
        private CancellationTokenSource _gestureCancellation;
        private IntPtr _popupSourceWindow;
        private string _lastText = "";
        private string _currentSourceLanguage = "en";
        private DateTime _lastTextAt = DateTime.MinValue;
        private bool _exiting;

        internal TranslatorApplicationContext(InstanceCoordinator instanceCoordinator)
        {
            _instanceCoordinator = instanceCoordinator;
            _settings = SettingsStore.Load();

            var menu = new ContextMenuStrip();
            _enabledMenuItem = new ToolStripMenuItem("启用自动翻译") { CheckOnClick = true, Checked = _settings.Enabled };
            _enabledMenuItem.CheckedChanged += delegate
            {
                _settings.Enabled = _enabledMenuItem.Checked;
                SettingsStore.Save(_settings);
                if (!_settings.Enabled) DismissPopupAndCancel();
            };
            var settingsItem = new ToolStripMenuItem("设置…");
            settingsItem.Click += delegate { ShowSettings(); };
            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(_enabledMenuItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "划词翻译",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += delegate { ShowSettings(); };

            _mouseHook = new GlobalMouseHook(delegate { return _settings; });
            _mouseHook.LeftButtonPressed += OnGlobalLeftButtonPressed;
            _mouseHook.SelectionGestureCompleted += OnSelectionGestureCompleted;
            _popup.UserDismissed += CancelCurrentPopupWork;
            _popup.ReadOriginalRequested += OnReadOriginalRequested;

            try
            {
                _speaker = new OriginalTextSpeaker();
                _speaker.StatusChanged += OnSpeechStatusChanged;
            }
            catch
            {
                _speaker = null;
            }

            _foregroundMonitor = new System.Windows.Forms.Timer { Interval = 180 };
            _foregroundMonitor.Tick += delegate
            {
                if (_settings.HideOnOutsideClick && _popup.Visible && _popupSourceWindow != IntPtr.Zero)
                {
                    var foreground = NativeMethods.GetForegroundWindow();
                    var popupHandle = _popup.IsHandleCreated ? _popup.Handle : IntPtr.Zero;
                    if (foreground != _popupSourceWindow && foreground != popupHandle)
                        DismissPopupAndCancel();
                }
            };
            _foregroundMonitor.Start();

            _instanceRequestTimer = new System.Windows.Forms.Timer { Interval = 180 };
            _instanceRequestTimer.Tick += PollInstanceRequests;
            _instanceRequestTimer.Start();

            _tray.BalloonTipTitle = "划词翻译已运行";
            _tray.BalloonTipText = "在其他应用中拖选文字，或双击/三击选词，随后会自动翻译。右键托盘图标可设置。";
            _tray.ShowBalloonTip(3500);
        }

        private void PollInstanceRequests(object sender, EventArgs eventArgs)
        {
            if (_exiting) return;
            if (_instanceCoordinator.ConsumeExitRequest())
            {
                ExitApplication();
                return;
            }
            if (_instanceCoordinator.ConsumeOpenSettingsRequest())
            {
                _instanceCoordinator.AcknowledgeOpenSettingsRequest();
                ShowSettings();
            }
        }

        private void OnGlobalLeftButtonPressed(Point point)
        {
            if (!_settings.HideOnOutsideClick || !_popup.Visible) return;
            if (!_popup.ContainsScreenPoint(point)) DismissPopupAndCancel();
        }

        private void DismissPopupAndCancel()
        {
            _popup.HideImmediately();
            CancelCurrentPopupWork();
        }

        private void CancelCurrentPopupWork()
        {
            if (_gestureCancellation != null && !_gestureCancellation.IsCancellationRequested)
                _gestureCancellation.Cancel();
            if (_speaker != null) _speaker.Stop();
        }

        private void OnReadOriginalRequested(string text)
        {
            if (_speaker == null)
            {
                _popup.SetSpeechState(false, "Windows 本地语音不可用，请在系统中安装语音包");
                return;
            }
            _speaker.Toggle(text, _currentSourceLanguage);
        }

        private void OnSpeechStatusChanged(object sender, SpeechStatusEventArgs eventArgs)
        {
            if (_exiting || _popup.IsDisposed) return;
            Action update = delegate
            {
                if (!_popup.IsDisposed) _popup.SetSpeechState(eventArgs.IsSpeaking, eventArgs.Message);
            };
            try
            {
                if (_popup.IsHandleCreated && _popup.InvokeRequired) _popup.BeginInvoke(update);
                else update();
            }
            catch (InvalidOperationException) { }
        }

        private async void OnSelectionGestureCompleted(MouseSelectionGesture gesture)
        {
            if (_exiting || !_settings.Enabled) return;
            if (_popup.ContainsScreenPoint(gesture.EndPoint)) return;

            if (_speaker != null) _speaker.Stop();

            if (_gestureCancellation != null)
            {
                _gestureCancellation.Cancel();
                _gestureCancellation.Dispose();
            }
            _gestureCancellation = new CancellationTokenSource();
            var token = _gestureCancellation.Token;

            try
            {
                var selectionDelay = _settings.SelectionDelayMilliseconds;
                if (gesture.ClickCount == 2)
                    selectionDelay = Math.Max(selectionDelay, Math.Min(260, (int)NativeMethods.GetDoubleClickTime()));
                if (selectionDelay > 0)
                    await Task.Delay(selectionDelay, token);

                var window = GetForegroundWindowInfo();
                if (window == null || window.Handle == IntPtr.Zero || window.ProcessId == Process.GetCurrentProcess().Id) return;
                if (!IsProcessAllowed(window.ProcessName, _settings)) return;
                _popupSourceWindow = window.Handle;

                var result = await _selectionReader.ReadAsync(window, gesture.EndPoint, _settings, token,
                    gesture.ClipboardSequenceNumber);
                if (result.IsSensitive) return;
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    if (!string.IsNullOrWhiteSpace(result.WarningMessage))
                    {
                        _popup.ShowError(result.WarningMessage, gesture.EndPoint, 4500);
                        return;
                    }
                    if (IsWpsProcess(window.ProcessName))
                    {
                        var message = !_settings.EnableClipboardFallback
                            ? "已检测到 WPS 划选，但剪贴板兜底已关闭。请在设置中开启“剪贴板兜底”和“WPS 兼容”。"
                            : "已检测到 WPS 划选，但本次未取得文字。请确认文档允许复制，并在设置中开启“WPS 兼容”。";
                        _popup.ShowError(message, gesture.EndPoint, 5000);
                    }
                    return;
                }

                var text = NormalizeText(result.Text);
                if (MeaningfulLength(text) < _settings.MinCharacters) return;
                if (text.Length > _settings.MaxCharacters) text = text.Substring(0, _settings.MaxCharacters);
                if (text == _lastText && DateTime.UtcNow - _lastTextAt < TimeSpan.FromSeconds(1.2)) return;
                _lastText = text;
                _lastTextAt = DateTime.UtcNow;

                var anchor = gesture.EndPoint;
                if (result.SelectionRectangle.HasValue)
                {
                    var rectangle = result.SelectionRectangle.Value;
                    anchor = new Point(rectangle.Right, rectangle.Bottom);
                }

                var engine = TranslationEngineFactory.Create(_settings);
                var automaticallyDetected = LanguageDetection.IsAutomatic(_settings);
                var sourceLanguage = LanguageDetection.ResolveSourceLanguage(text, _settings);
                var targetLanguage = LanguageDetection.ResolveTargetLanguage(sourceLanguage, _settings);
                var translationSettings = _settings.Clone();
                translationSettings.TargetLanguage = targetLanguage;
                _currentSourceLanguage = sourceLanguage;
                _popup.ShowLoading(text, engine.DisplayName, sourceLanguage, targetLanguage,
                    automaticallyDetected, anchor);
                var translation = await engine.TranslateAsync(text, translationSettings, token);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(translation)) return;
                _popup.ShowResult(text, translation, engine.DisplayName, result.Method, sourceLanguage,
                    targetLanguage, automaticallyDetected, anchor, _settings.AutoHideMilliseconds);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                    _popup.ShowError(FriendlyError(exception), gesture.EndPoint, Math.Max(4000, _settings.AutoHideMilliseconds));
            }
        }

        private static TargetWindowInfo GetForegroundWindowInfo()
        {
            try
            {
                var handle = NativeMethods.GetForegroundWindow();
                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                using (var process = Process.GetProcessById((int)processId))
                {
                    return new TargetWindowInfo
                    {
                        Handle = handle,
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        WindowTitle = process.MainWindowTitle
                    };
                }
            }
            catch { return null; }
        }

        private static bool IsProcessAllowed(string processName, AppSettings settings)
        {
            var blacklist = SplitProcessList(settings.Blacklist);
            if (blacklist.Any(delegate(string pattern) { return MatchesProcess(processName, pattern); })) return false;
            var whitelist = SplitProcessList(settings.Whitelist);
            return whitelist.Count == 0 || whitelist.Any(delegate(string pattern) { return MatchesProcess(processName, pattern); });
        }

        private static bool IsWpsProcess(string processName)
        {
            return string.Equals(processName, "wps", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "wpspdf", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> SplitProcessList(string value)
        {
            return Regex.Split(value ?? "", "[,;\\r\\n]+")
                .Select(delegate(string item) { return item.Trim(); })
                .Where(delegate(string item) { return item.Length > 0; })
                .ToList();
        }

        private static bool MatchesProcess(string processName, string pattern)
        {
            var cleanProcess = (processName ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4) : processName ?? "";
            var cleanPattern = pattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? pattern.Substring(0, pattern.Length - 4) : pattern;
            if (cleanPattern == "*") return true;
            if (cleanPattern.StartsWith("*") && cleanPattern.EndsWith("*") && cleanPattern.Length > 2)
                return cleanProcess.IndexOf(cleanPattern.Trim('*'), StringComparison.OrdinalIgnoreCase) >= 0;
            if (cleanPattern.StartsWith("*")) return cleanProcess.EndsWith(cleanPattern.Substring(1), StringComparison.OrdinalIgnoreCase);
            if (cleanPattern.EndsWith("*")) return cleanProcess.StartsWith(cleanPattern.Substring(0, cleanPattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            return string.Equals(cleanProcess, cleanPattern, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeText(string text)
        {
            return (text ?? "").Replace("\0", "").Replace("\u200B", "").Trim();
        }

        private static int MeaningfulLength(string text)
        {
            return text.Count(delegate(char character) { return !char.IsWhiteSpace(character); });
        }

        private static string FriendlyError(Exception exception)
        {
            var message = exception.Message ?? "未知错误";
            if (message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                return "翻译服务响应超时。";
            if (message.Length > 420) message = message.Substring(0, 420) + "…";
            return message;
        }

        private void ShowSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                _settingsForm.BringToFront();
                return;
            }

            using (var form = new SettingsForm(_settings))
            {
                _settingsForm = form;
                try
                {
                    if (form.ShowDialog() != DialogResult.OK || form.SavedSettings == null) return;
                    _settings = form.SavedSettings;
                    SettingsStore.Save(_settings);
                    _enabledMenuItem.Checked = _settings.Enabled;
                }
                finally { _settingsForm = null; }
            }
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            if (_gestureCancellation != null) _gestureCancellation.Cancel();
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.DialogResult = DialogResult.Cancel;
                _settingsForm.Close();
            }
            _mouseHook.Dispose();
            _instanceRequestTimer.Stop();
            _instanceRequestTimer.Dispose();
            _foregroundMonitor.Stop();
            _foregroundMonitor.Dispose();
            if (_speaker != null)
            {
                _speaker.StatusChanged -= OnSpeechStatusChanged;
                _speaker.Dispose();
                _speaker = null;
            }
            _tray.Visible = false;
            _tray.Dispose();
            _popup.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_exiting) ExitApplication();
            base.Dispose(disposing);
        }
    }
}
