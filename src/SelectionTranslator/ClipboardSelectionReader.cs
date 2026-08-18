using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SelectionTranslator
{
    internal sealed class ClipboardReadResult
    {
        internal string Text;
        internal string WarningMessage;
    }

    internal static class ClipboardSelectionReader
    {
        private const int ClipboardOpenAttempts = 7;
        private const int ClipboardOpenRetryMilliseconds = 15;
        private const ulong MaximumTextBytes = 16UL * 1024UL * 1024UL;
        private static readonly SemaphoreSlim ClipboardGate = new SemaphoreSlim(1, 1);

        internal static Task<ClipboardReadResult> TryReadAsync(
            IntPtr expectedForegroundWindow,
            int expectedProcessId,
            bool useWpsCompatibility,
            uint clipboardSequenceAtMouseUp,
            CancellationToken token)
        {
            var completion = new TaskCompletionSource<ClipboardReadResult>();
            var thread = new Thread(delegate()
            {
                IClipboardSnapshot snapshot = null;
                var gateEntered = false;
                var clipboardWasChanged = false;
                var clipboardChangedBeforeCopy = false;
                var copiedText = "";
                try
                {
                    ClipboardGate.Wait(token);
                    gateEntered = true;
                    if (!token.IsCancellationRequested
                        && IsExpectedForegroundProcess(expectedForegroundWindow, expectedProcessId))
                    {
                        // A sequence change alone does not prove that the user copied the current
                        // selection. It can also be caused by a previous fallback restoring its
                        // snapshot, Office/browser delayed rendering, or a clipboard manager. Never
                        // translate that pre-existing text; always request a fresh copy below.
                        clipboardChangedBeforeCopy =
                            NativeMethods.GetClipboardSequenceNumber() != clipboardSequenceAtMouseUp;

                        // Ordinary applications keep the conservative text-only snapshot. WPS often
                        // leaves HTML/OLE formats on the clipboard and does not expose TextPattern, so
                        // its opt-in compatibility path keeps the original standard OLE data object.
                        snapshot = ClipboardTextSnapshot.TryCaptureIfSafe();
                        if (snapshot == null && useWpsCompatibility)
                            snapshot = OleClipboardSnapshot.TryCapture();
                        clipboardChangedBeforeCopy = clipboardChangedBeforeCopy
                            || NativeMethods.GetClipboardSequenceNumber() != clipboardSequenceAtMouseUp;

                        if (snapshot != null)
                        {
                            if (useWpsCompatibility) Thread.Sleep(70);
                            var baseline = NativeMethods.GetClipboardSequenceNumber();

                            // Preserve a genuine manual Ctrl+C that arrived while UI Automation was
                            // being tried. Refresh the snapshot before issuing our own Ctrl+C.
                            if (baseline != clipboardSequenceAtMouseUp)
                            {
                                snapshot.Dispose();
                                snapshot = ClipboardTextSnapshot.TryCaptureIfSafe();
                                if (snapshot == null && useWpsCompatibility)
                                    snapshot = OleClipboardSnapshot.TryCapture();
                                baseline = NativeMethods.GetClipboardSequenceNumber();
                            }

                            if (snapshot == null) return;
                            if (NativeMethods.SendCtrlC())
                            {
                                var waitMilliseconds = useWpsCompatibility ? 750 : 260;
                                var deadline = Environment.TickCount + waitMilliseconds;
                                var observedSequence = baseline;
                                var quietDeadline = 0;
                                while (!token.IsCancellationRequested && unchecked(deadline - Environment.TickCount) > 0)
                                {
                                    var currentSequence = NativeMethods.GetClipboardSequenceNumber();
                                    if (currentSequence != observedSequence)
                                    {
                                        observedSequence = currentSequence;
                                        clipboardWasChanged = true;
                                        var currentText = TryReadUnicodeText() ?? "";
                                        if (!string.IsNullOrEmpty(currentText))
                                        {
                                            copiedText = currentText;
                                            quietDeadline = Environment.TickCount + 45;
                                        }
                                    }
                                    if (quietDeadline != 0
                                        && unchecked(quietDeadline - Environment.TickCount) <= 0) break;
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
                        if (gateEntered) ClipboardGate.Release();
                    }
                    completion.TrySetResult(new ClipboardReadResult
                    {
                        Text = copiedText ?? "",
                        WarningMessage = string.IsNullOrWhiteSpace(copiedText) && clipboardChangedBeforeCopy
                            ? "检测到剪贴板在取词前发生了变化。为避免翻译旧文本，本次已跳过；请重新选择一次。"
                            : ""
                    });
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
