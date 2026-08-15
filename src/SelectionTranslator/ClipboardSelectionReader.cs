using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SelectionTranslator
{
    internal static class ClipboardSelectionReader
    {
        private const int ClipboardOpenAttempts = 7;
        private const int ClipboardOpenRetryMilliseconds = 15;
        private const ulong MaximumTextBytes = 16UL * 1024UL * 1024UL;

        internal static Task<string> TryReadAsync(
            IntPtr expectedForegroundWindow,
            int expectedProcessId,
            bool useWpsCompatibility,
            uint clipboardSequenceAtMouseUp,
            CancellationToken token)
        {
            var completion = new TaskCompletionSource<string>();
            var thread = new Thread(delegate()
            {
                IClipboardSnapshot snapshot = null;
                var clipboardWasChanged = false;
                var copiedText = "";
                try
                {
                    if (!token.IsCancellationRequested
                        && IsExpectedForegroundProcess(expectedForegroundWindow, expectedProcessId))
                    {
                        // A user may press Ctrl+C immediately after mouse-up. In that case the
                        // clipboard sequence has already changed: use the user's copy as the input
                        // and never restore an older snapshot over it.
                        if (NativeMethods.GetClipboardSequenceNumber() != clipboardSequenceAtMouseUp)
                        {
                            copiedText = TryReadUnicodeText() ?? "";
                            if (!string.IsNullOrWhiteSpace(copiedText))
                            {
                                completion.TrySetResult(copiedText);
                                return;
                            }
                        }

                        // Ordinary applications keep the conservative text-only snapshot. WPS often
                        // leaves HTML/OLE formats on the clipboard and does not expose TextPattern, so
                        // its opt-in compatibility path keeps the original standard OLE data object.
                        snapshot = ClipboardTextSnapshot.TryCaptureIfSafe();
                        if (snapshot == null && useWpsCompatibility)
                            snapshot = OleClipboardSnapshot.TryCapture();

                        if (snapshot != null)
                        {
                            if (useWpsCompatibility) Thread.Sleep(70);
                            var baseline = NativeMethods.GetClipboardSequenceNumber();
                            if (NativeMethods.SendCtrlC())
                            {
                                var waitMilliseconds = useWpsCompatibility ? 750 : 260;
                                var deadline = Environment.TickCount + waitMilliseconds;
                                while (!token.IsCancellationRequested && unchecked(deadline - Environment.TickCount) > 0)
                                {
                                    if (NativeMethods.GetClipboardSequenceNumber() != baseline)
                                    {
                                        clipboardWasChanged = true;
                                        copiedText = TryReadUnicodeText() ?? "";
                                        if (!string.IsNullOrEmpty(copiedText)) break;
                                    }
                                    Thread.Sleep(useWpsCompatibility ? 18 : 12);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    copiedText = "";
                }
                finally
                {
                    try
                    {
                        if (clipboardWasChanged && snapshot != null) snapshot.TryRestore();
                    }
                    catch { }
                    finally
                    {
                        if (snapshot != null) snapshot.Dispose();
                    }
                    completion.TrySetResult(copiedText ?? "");
                }
            });
            thread.IsBackground = true;
            thread.Name = useWpsCompatibility
                ? "SelectionTranslator WPS clipboard reader"
                : "SelectionTranslator clipboard reader";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private static bool IsExpectedForegroundProcess(IntPtr expectedWindow, int expectedProcessId)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == expectedWindow) return true;
            if (foreground == IntPtr.Zero || expectedProcessId <= 0) return false;
            uint processId;
            NativeMethods.GetWindowThreadProcessId(foreground, out processId);
            return processId == (uint)expectedProcessId;
        }

        private static string TryReadUnicodeText()
        {
            if (!TryOpenClipboard()) return "";
            try { return ReadUnicodeTextWhileOpen(); }
            catch { return ""; }
            finally { NativeMethods.CloseClipboard(); }
        }

        private static string ReadUnicodeTextWhileOpen()
        {
            var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return "";
            var size = NativeMethods.GlobalSize(handle).ToUInt64();
            if (size < 2 || size > MaximumTextBytes) return "";
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero) return "";
            try
            {
                var characterCount = checked((int)(size / 2));
                return (Marshal.PtrToStringUni(pointer, characterCount) ?? "").TrimEnd('\0');
            }
            finally { NativeMethods.GlobalUnlock(handle); }
        }

        private static bool TryOpenClipboard()
        {
            for (var attempt = 0; attempt < ClipboardOpenAttempts; attempt++)
            {
                if (NativeMethods.OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(ClipboardOpenRetryMilliseconds);
            }
            return false;
        }

        private interface IClipboardSnapshot : IDisposable
        {
            bool TryRestore();
        }

        private sealed class ClipboardTextSnapshot : IClipboardSnapshot
        {
            private readonly bool _wasEmpty;
            private readonly string _text;

            private ClipboardTextSnapshot(bool wasEmpty, string text)
            {
                _wasEmpty = wasEmpty;
                _text = text ?? "";
            }

            internal static ClipboardTextSnapshot TryCaptureIfSafe()
            {
                if (!TryOpenClipboard()) return null;
                try
                {
                    var hasAnyFormat = false;
                    var hasUnicodeText = false;
                    uint currentFormat = 0;
                    while (true)
                    {
                        currentFormat = NativeMethods.EnumClipboardFormats(currentFormat);
                        if (currentFormat == 0) break;
                        hasAnyFormat = true;
                        if (!IsSafeTextFormat(currentFormat)) return null;
                        if (currentFormat == NativeMethods.CF_UNICODETEXT) hasUnicodeText = true;
                    }

                    if (!hasAnyFormat) return new ClipboardTextSnapshot(true, "");
                    if (!hasUnicodeText) return null;
                    return new ClipboardTextSnapshot(false, ReadUnicodeTextWhileOpen());
                }
                catch { return null; }
                finally { NativeMethods.CloseClipboard(); }
            }

            private static bool IsSafeTextFormat(uint format)
            {
                return format == NativeMethods.CF_TEXT
                    || format == NativeMethods.CF_OEMTEXT
                    || format == NativeMethods.CF_UNICODETEXT
                    || format == NativeMethods.CF_LOCALE;
            }

            public bool TryRestore()
            {
                if (!TryOpenClipboard()) return false;
                try
                {
                    if (!NativeMethods.EmptyClipboard()) return false;
                    if (_wasEmpty) return true;

                    var bytes = Encoding.Unicode.GetBytes(_text + "\0");
                    var memory = NativeMethods.GlobalAlloc(
                        NativeMethods.GMEM_MOVEABLE | NativeMethods.GMEM_ZEROINIT,
                        new UIntPtr((uint)bytes.Length));
                    if (memory == IntPtr.Zero) return false;

                    var pointer = NativeMethods.GlobalLock(memory);
                    if (pointer == IntPtr.Zero)
                    {
                        NativeMethods.GlobalFree(memory);
                        return false;
                    }
                    try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
                    finally { NativeMethods.GlobalUnlock(memory); }

                    if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, memory) != IntPtr.Zero)
                        return true;
                    NativeMethods.GlobalFree(memory);
                    return false;
                }
                catch { return false; }
                finally { NativeMethods.CloseClipboard(); }
            }

            public void Dispose() { }
        }

        private sealed class OleClipboardSnapshot : IClipboardSnapshot
        {
            private IDataObject _dataObject;

            private OleClipboardSnapshot(IDataObject dataObject)
            {
                _dataObject = dataObject;
            }

            internal static OleClipboardSnapshot TryCapture()
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    IDataObject dataObject;
                    var result = NativeMethods.OleGetClipboard(out dataObject);
                    if (result >= 0 && dataObject != null)
                        return new OleClipboardSnapshot(dataObject);
                    Thread.Sleep(20);
                }
                return null;
            }

            public bool TryRestore()
            {
                return _dataObject != null && NativeMethods.OleSetClipboard(_dataObject) >= 0;
            }

            public void Dispose()
            {
                if (_dataObject == null) return;
                try
                {
                    if (Marshal.IsComObject(_dataObject)) Marshal.ReleaseComObject(_dataObject);
                }
                catch { }
                _dataObject = null;
            }
        }
    }
}
