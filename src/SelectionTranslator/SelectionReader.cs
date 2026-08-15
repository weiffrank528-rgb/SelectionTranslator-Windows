using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace SelectionTranslator
{
    internal sealed class TargetWindowInfo
    {
        internal IntPtr Handle;
        internal int ProcessId;
        internal string ProcessName;
        internal string WindowTitle;
    }

    internal sealed class SelectionReadResult
    {
        internal string Text;
        internal Rectangle? SelectionRectangle;
        internal string Method;
        internal bool IsSensitive;

        internal static SelectionReadResult Empty(string method)
        {
            return new SelectionReadResult { Text = "", Method = method };
        }
    }

    internal sealed class SelectionReader
    {
        internal async Task<SelectionReadResult> ReadAsync(
            TargetWindowInfo window, Point mousePoint, AppSettings settings, CancellationToken token,
            uint clipboardSequenceAtMouseUp)
        {
            var uiaTask = RunUiaReadOnWorker(window, mousePoint);
            var timeoutTask = Task.Delay(Math.Max(100, settings.UiaTimeoutMilliseconds), token);
            var completed = await Task.WhenAny(uiaTask, timeoutTask);
            token.ThrowIfCancellationRequested();

            if (completed == uiaTask)
            {
                var uiaResult = await uiaTask;
                if (uiaResult.IsSensitive || !string.IsNullOrWhiteSpace(uiaResult.Text)) return uiaResult;
            }

            if (!settings.EnableClipboardFallback) return SelectionReadResult.Empty("UI Automation");
            var isWps = string.Equals(window.ProcessName, "wps", StringComparison.OrdinalIgnoreCase)
                || string.Equals(window.ProcessName, "wpspdf", StringComparison.OrdinalIgnoreCase);
            var useWpsCompatibility = isWps && settings.EnableWpsCompatibility;
            var copiedText = await ClipboardSelectionReader.TryReadAsync(
                window.Handle, window.ProcessId, useWpsCompatibility,
                clipboardSequenceAtMouseUp, token);
            return new SelectionReadResult
            {
                Text = copiedText ?? "",
                Method = useWpsCompatibility ? "WPS 兼容 Ctrl+C" : "Ctrl+C 兜底"
            };
        }

        private static Task<SelectionReadResult> RunUiaReadOnWorker(TargetWindowInfo window, Point mousePoint)
        {
            var completion = new TaskCompletionSource<SelectionReadResult>();
            var thread = new Thread(delegate()
            {
                try { completion.TrySetResult(TryReadWithUia(window, mousePoint)); }
                catch { completion.TrySetResult(SelectionReadResult.Empty("UI Automation")); }
            });
            thread.IsBackground = true;
            thread.Name = "SelectionTranslator UIA reader";
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return completion.Task;
        }

        private static SelectionReadResult TryReadWithUia(TargetWindowInfo window, Point mousePoint)
        {
            var candidates = new List<AutomationElement>();
            TryAdd(candidates, delegate { return AutomationElement.FocusedElement; });
            TryAdd(candidates, delegate
            {
                return AutomationElement.FromPoint(new System.Windows.Point(mousePoint.X, mousePoint.Y));
            });
            TryAdd(candidates, delegate { return AutomationElement.FromHandle(window.Handle); });

            foreach (var candidate in candidates)
            {
                var current = candidate;
                for (var depth = 0; current != null && depth < 16; depth++)
                {
                    var result = TryReadElement(current, mousePoint);
                    if (result.IsSensitive || !string.IsNullOrWhiteSpace(result.Text)) return result;
                    try { current = TreeWalker.ControlViewWalker.GetParent(current); }
                    catch { current = null; }
                }
            }

            // Some browser/PDF providers expose TextPattern only on a document descendant.
            try
            {
                var root = AutomationElement.FromHandle(window.Handle);
                var condition = new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true);
                var provider = root.FindFirst(TreeScope.Descendants, condition);
                if (provider != null)
                {
                    var result = TryReadElement(provider, mousePoint);
                    if (result.IsSensitive || !string.IsNullOrWhiteSpace(result.Text)) return result;
                }
            }
            catch { }

            return SelectionReadResult.Empty("UI Automation");
        }

        private static void TryAdd(ICollection<AutomationElement> list, Func<AutomationElement> getElement)
        {
            try
            {
                var element = getElement();
                if (element != null) list.Add(element);
            }
            catch { }
        }

        private static SelectionReadResult TryReadElement(AutomationElement element, Point mousePoint)
        {
            try
            {
                if (element.Current.IsPassword)
                    return new SelectionReadResult { Text = "", Method = "已跳过密码输入框", IsSensitive = true };
            }
            catch { }

            object rawPattern;
            try
            {
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out rawPattern))
                    return SelectionReadResult.Empty("UI Automation");
            }
            catch { return SelectionReadResult.Empty("UI Automation"); }

            try
            {
                var pattern = (TextPattern)rawPattern;
                var ranges = pattern.GetSelection();
                if (ranges == null || ranges.Length == 0) return SelectionReadResult.Empty("UI Automation");

                var textParts = new List<string>();
                Rectangle? nearestRectangle = null;
                double nearestDistance = double.MaxValue;
                foreach (var range in ranges)
                {
                    var text = (range.GetText(-1) ?? "").Trim('\0', '\r', '\n', ' ', '\t');
                    if (!string.IsNullOrWhiteSpace(text)) textParts.Add(text);

                    var rectangles = range.GetBoundingRectangles();
                    foreach (var automationRect in rectangles)
                    {
                        var rect = Rectangle.Round(new RectangleF((float)automationRect.X, (float)automationRect.Y,
                            (float)automationRect.Width, (float)automationRect.Height));
                        if (rect.Width <= 0 || rect.Height <= 0) continue;
                        var dx = rect.Right - mousePoint.X;
                        var dy = rect.Bottom - mousePoint.Y;
                        var distance = dx * dx + dy * dy;
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestRectangle = rect;
                        }
                    }
                }

                return new SelectionReadResult
                {
                    Text = string.Join(Environment.NewLine, textParts.ToArray()),
                    SelectionRectangle = nearestRectangle,
                    Method = "UI Automation"
                };
            }
            catch { return SelectionReadResult.Empty("UI Automation"); }
        }
    }
}
