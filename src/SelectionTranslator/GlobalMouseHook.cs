using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class MultiClickTracker
    {
        private int _lastClickUpTick;
        private Point _lastClickPoint;
        private int _consecutiveClickCount;

        internal int RecordClick(Point point, int tick, int maximumDelay, Size doubleClickSize)
        {
            var closeEnough = Math.Abs(point.X - _lastClickPoint.X) <= Math.Max(2, doubleClickSize.Width / 2)
                && Math.Abs(point.Y - _lastClickPoint.Y) <= Math.Max(2, doubleClickSize.Height / 2);
            var soonEnough = _consecutiveClickCount > 0
                && unchecked(tick - _lastClickUpTick) <= maximumDelay;
            _consecutiveClickCount = closeEnough && soonEnough
                ? Math.Min(3, _consecutiveClickCount + 1)
                : 1;
            _lastClickPoint = point;
            _lastClickUpTick = tick;
            return _consecutiveClickCount;
        }

        internal void Reset()
        {
            _consecutiveClickCount = 0;
        }
    }

    internal sealed class MouseSelectionGesture
    {
        internal Point StartPoint;
        internal Point EndPoint;
        internal int DurationMilliseconds;
        internal uint ClipboardSequenceNumber;
        internal int ClickCount;
    }

    internal sealed class GlobalMouseHook : IDisposable
    {
        private readonly Func<AppSettings> _settings;
        private readonly NativeMethods.LowLevelMouseProc _callback;
        private IntPtr _hook;
        private bool _leftDown;
        private Point _start;
        private Point _last;
        private int _downTick;
        private int _maxDistanceSquared;
        private readonly MultiClickTracker _clickTracker = new MultiClickTracker();

        internal event Action<MouseSelectionGesture> SelectionGestureCompleted;
        internal event Action<Point> LeftButtonPressed;

        internal GlobalMouseHook(Func<AppSettings> settings)
        {
            _settings = settings;
            _callback = HookCallback;
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback,
                    NativeMethods.GetModuleHandle(module.ModuleName), 0);
            }
            if (_hook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法安装全局鼠标监听。" );
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                var point = new Point(data.Point.X, data.Point.Y);
                var message = wParam.ToInt32();
                if (message == NativeMethods.WM_LBUTTONDOWN)
                {
                    var pressedHandler = LeftButtonPressed;
                    if (pressedHandler != null) pressedHandler(point);
                    _leftDown = true;
                    _start = point;
                    _last = point;
                    _downTick = Environment.TickCount;
                    _maxDistanceSquared = 0;
                }
                else if (message == NativeMethods.WM_MOUSEMOVE && _leftDown)
                {
                    _last = point;
                    var dx = point.X - _start.X;
                    var dy = point.Y - _start.Y;
                    _maxDistanceSquared = Math.Max(_maxDistanceSquared, dx * dx + dy * dy);
                }
                else if (message == NativeMethods.WM_LBUTTONUP && _leftDown)
                {
                    _leftDown = false;
                    _last = point;
                    var settings = _settings();
                    var duration = unchecked(Environment.TickCount - _downTick);
                    var threshold = Math.Max(1, settings.DragThresholdPixels);
                    if (_maxDistanceSquared >= threshold * threshold && duration >= settings.MinDragMilliseconds)
                    {
                        _clickTracker.Reset();
                        RaiseSelectionGesture(duration, 0);
                    }
                    else if (_maxDistanceSquared < threshold * threshold)
                    {
                        var now = Environment.TickCount;
                        var doubleClickSize = SystemInformation.DoubleClickSize;
                        var clickCount = _clickTracker.RecordClick(point, now,
                            (int)NativeMethods.GetDoubleClickTime(), doubleClickSize);

                        // A double-click normally selects a word; a third click often expands it to
                        // a line or paragraph. The application cancels the double-click read if the
                        // third click arrives before it completes.
                        if (clickCount >= 2)
                        {
                            RaiseSelectionGesture(duration, clickCount);
                            if (clickCount >= 3) _clickTracker.Reset();
                        }
                    }
                }
            }

            // Always pass through: the application never swallows or modifies the user's click.
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void RaiseSelectionGesture(int duration, int clickCount)
        {
            var handler = SelectionGestureCompleted;
            if (handler == null) return;
            handler(new MouseSelectionGesture
            {
                StartPoint = _start,
                EndPoint = _last,
                DurationMilliseconds = duration,
                ClipboardSequenceNumber = NativeMethods.GetClipboardSequenceNumber(),
                ClickCount = clickCount
            });
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
