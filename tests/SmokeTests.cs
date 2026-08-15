using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    internal static class SmokeTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var settings = new AppSettings();
                var clone = settings.Clone();
                Assert(clone.Engine == "MyMemory", "settings clone");
                Assert(clone.AutoDetectSourceLanguage, "automatic source detection default");
                var migrated = new JavaScriptSerializer().Deserialize<AppSettings>("{\"Engine\":\"MyMemory\",\"SourceLanguage\":\"zh-CN\"}");
                Assert(migrated.AutoDetectSourceLanguage, "automatic source detection old-settings migration");
                Assert(clone.AutoHideMilliseconds == 6500 && clone.HideOnOutsideClick, "popup dismissal defaults");
                Assert(clone.EnableWpsCompatibility, "WPS compatibility default");

                TestNativeGlobalMemoryRoundTrip();
                Console.WriteLine("PASS native clipboard buffer primitives");

                var expectedInputSize = IntPtr.Size == 8 ? 40 : 28;
                Assert(Marshal.SizeOf(typeof(NativeMethods.INPUT)) == expectedInputSize, "Win32 INPUT layout");
                Console.WriteLine("PASS Win32 INPUT layout");

                using (var hook = new GlobalMouseHook(delegate { return settings; })) { }
                Console.WriteLine("PASS global mouse hook install/uninstall");

                using (var popup = new PopupForm()) { }
                using (var settingsForm = new SettingsForm(settings)) { }
                using (var existingInstanceForm = new ExistingInstanceForm()) { }
                using (var speaker = new OriginalTextSpeaker()) { }
                Console.WriteLine("PASS popup, settings, instance dialog, and local speech construct");

                TestInstanceCoordination();
                Console.WriteLine("PASS existing-instance open/restart coordination");

                var engine = TranslationEngineFactory.Create(settings);
                Assert(engine.DisplayName.IndexOf("MyMemory", StringComparison.OrdinalIgnoreCase) >= 0, "default engine factory");
                Console.WriteLine("PASS default engine factory");

                var googleSettings = settings.Clone();
                googleSettings.Engine = "Google";
                var googleEngine = TranslationEngineFactory.Create(googleSettings);
                Assert(googleEngine.DisplayName.IndexOf("Google", StringComparison.OrdinalIgnoreCase) >= 0, "Google engine factory");
                Console.WriteLine("PASS Google engine factory");

                Assert(LanguageDetection.DetectChineseOrEnglish("需要你手动选择") == "zh-CN", "detect Chinese source");
                Assert(LanguageDetection.DetectChineseOrEnglish("You need to select it manually") == "en", "detect English source");
                Assert(LanguageDetection.TranslationTitle("en") == "英文翻译", "English translation title");
                Assert(LanguageDetection.TranslationTitle("zh-CN") == "中文翻译", "Chinese translation title");
                var englishTarget = settings.Clone();
                englishTarget.TargetLanguage = "en";
                Assert(LanguageDetection.ResolveTargetLanguage("zh-CN", englishTarget) == "en", "Chinese source keeps English target");
                Assert(LanguageDetection.ResolveTargetLanguage("en", englishTarget) == "zh-CN", "English source flips to Chinese target");
                var chineseTarget = settings.Clone();
                chineseTarget.TargetLanguage = "zh-CN";
                Assert(LanguageDetection.ResolveTargetLanguage("en", chineseTarget) == "zh-CN", "English source keeps Chinese target");
                Assert(LanguageDetection.ResolveTargetLanguage("zh-CN", chineseTarget) == "en", "Chinese source flips to English target");
                var explicitLanguage = settings.Clone();
                explicitLanguage.AutoDetectSourceLanguage = false;
                explicitLanguage.SourceLanguage = "zh-CN";
                Assert(LanguageDetection.ResolveSourceLanguage("English text", explicitLanguage) == "zh-CN", "manual source override");
                Assert(LanguageDetection.ResolveTargetLanguage("en", explicitLanguage) == "zh-CN", "manual target remains fixed");
                Console.WriteLine("PASS Chinese/English source detection and labels");

                foreach (var argument in args)
                {
                    const string renderPrefix = "--render-popup=";
                    if (argument.StartsWith(renderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        RenderPopup(argument.Substring(renderPrefix.Length));
                        Console.WriteLine("PASS popup preview render");
                    }
                    const string renderSettingsPrefix = "--render-settings=";
                    if (argument.StartsWith(renderSettingsPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        RenderSettings(argument.Substring(renderSettingsPrefix.Length), settings);
                        Console.WriteLine("PASS settings preview render");
                    }
                }

                if (Array.IndexOf(args, "--skip-network") < 0)
                {
                    var translated = engine.TranslateAsync("Hello world", settings, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    Assert(!string.IsNullOrWhiteSpace(translated), "live MyMemory translation");
                    Console.WriteLine("PASS live MyMemory translation: " + translated);

                    var chineseToEnglish = settings.Clone();
                    chineseToEnglish.TargetLanguage = "en";
                    var english = engine.TranslateAsync("需要你手动", chineseToEnglish, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    Assert(!string.IsNullOrWhiteSpace(english) && english != "需要你手动", "live MyMemory Chinese to English");
                    Console.WriteLine("PASS live MyMemory Chinese to English: " + english);
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + exception);
                return 1;
            }
        }

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Smoke test failed: " + name);
        }

        private static void TestNativeGlobalMemoryRoundTrip()
        {
            var expected = new byte[] { 7, 14, 21, 28, 35, 42 };
            var memory = NativeMethods.GlobalAlloc(
                NativeMethods.GMEM_MOVEABLE | NativeMethods.GMEM_ZEROINIT,
                new UIntPtr((uint)expected.Length));
            Assert(memory != IntPtr.Zero, "GlobalAlloc");
            try
            {
                var writePointer = NativeMethods.GlobalLock(memory);
                Assert(writePointer != IntPtr.Zero, "GlobalLock write");
                try { Marshal.Copy(expected, 0, writePointer, expected.Length); }
                finally { NativeMethods.GlobalUnlock(memory); }

                Assert(NativeMethods.GlobalSize(memory).ToUInt64() >= (ulong)expected.Length, "GlobalSize");
                var actual = new byte[expected.Length];
                var readPointer = NativeMethods.GlobalLock(memory);
                Assert(readPointer != IntPtr.Zero, "GlobalLock read");
                try { Marshal.Copy(readPointer, actual, 0, actual.Length); }
                finally { NativeMethods.GlobalUnlock(memory); }
                for (var index = 0; index < expected.Length; index++)
                    Assert(actual[index] == expected[index], "global memory byte copy");
            }
            finally { NativeMethods.GlobalFree(memory); }
        }

        private static void TestInstanceCoordination()
        {
            var testSuffix = "-Smoke-" + Guid.NewGuid().ToString("N");
            using (var primary = new InstanceCoordinator(testSuffix))
            {
                Assert(primary.IsPrimary, "primary instance mutex");
                var acknowledged = false;
                var workerFinished = new ManualResetEvent(false);
                var worker = new Thread(delegate()
                {
                    using (var secondary = new InstanceCoordinator(testSuffix))
                    {
                        Assert(!secondary.IsPrimary, "secondary instance detection");
                        acknowledged = secondary.RequestOpenSettings(2000);
                        secondary.RequestExit();
                    }
                    workerFinished.Set();
                });
                worker.IsBackground = true;
                worker.Start();

                var deadline = Environment.TickCount + 1800;
                while (!primary.ConsumeOpenSettingsRequest()
                    && unchecked(deadline - Environment.TickCount) > 0)
                    Thread.Sleep(15);
                primary.AcknowledgeOpenSettingsRequest();
                Assert(workerFinished.WaitOne(2200), "secondary coordination completion");
                Assert(acknowledged, "open settings acknowledgement");

                deadline = Environment.TickCount + 800;
                while (!primary.ConsumeExitRequest()
                    && unchecked(deadline - Environment.TickCount) > 0)
                    Thread.Sleep(15);
                Assert(unchecked(deadline - Environment.TickCount) > 0, "restart request");
                workerFinished.Dispose();
            }
        }

        private static void RenderPopup(string path)
        {
            using (var popup = new PopupForm())
            {
                popup.ShowResult(
                    "需要你手动选择一次，然后应用会自动识别中文并翻译成英文。",
                    "You need to select it manually once. The app will then detect Chinese and translate it into English.",
                    "MyMemory（免 Key）", "UI Automation", "zh-CN", "en", true, new Point(700, 420), 0);
                Application.DoEvents();
                Assert(popup.ContainsScreenPoint(new Point(popup.Left + 10, popup.Top + 10)), "popup contains inside point");
                Assert(!popup.ContainsScreenPoint(new Point(popup.Right + 10, popup.Bottom + 10)), "popup excludes outside point");
                using (var bitmap = new Bitmap(popup.Width, popup.Height))
                {
                    popup.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(path, ImageFormat.Png);
                }
                popup.Hide();
            }
        }

        private static void RenderSettings(string path, AppSettings settings)
        {
            using (var form = new SettingsForm(settings))
            {
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(path, ImageFormat.Png);
                }
                form.Close();
            }
        }
    }
}
