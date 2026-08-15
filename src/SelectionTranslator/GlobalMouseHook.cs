using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SelectionTranslator
{
    internal sealed class MouseSelectionGesture
    {
        internal Point StartPoint;
        internal Point EndPoint;
        internal int DurationMilliseconds;
        internal uint ClipboardSequenceNumber;
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
                        var handler = SelectionGestureCompleted;
                        if (handler != null)
                            handler(new MouseSelectionGesture
                            {
                                StartPoint = _start,
                                EndPoint = _last,
                                DurationMilliseconds = duration,
                                ClipboardSequenceNumber = NativeMethods.GetClipboardSequenceNumber()
                            });
                    }
                }
            }

            // Always pass through: the application never swallows or modifies the user's click.
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
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
