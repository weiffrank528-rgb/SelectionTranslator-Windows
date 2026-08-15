using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            InitializeWindowsForms();

            using (var coordinator = new InstanceCoordinator())
            {
                if (!coordinator.IsPrimary)
                {
                    var action = ShowExistingInstanceDialog();
                    if (action == ExistingInstanceAction.Cancel) return;
                    if (action == ExistingInstanceAction.OpenSettings)
                    {
                        if (!coordinator.RequestOpenSettings(1400))
                        {
                            MessageBox.Show("当前运行的可能是旧版本，无法直接打开设置。\n请再次双击程序并选择“重新启动”。",
                                "划词翻译", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        return;
                    }

                    coordinator.RequestExit();
                    if (!coordinator.TryBecomePrimary(2800))
                    {
                        // Compatibility path for versions before instance coordination existed.
                        // This only runs after the user explicitly clicked Restart.
                        StopLegacyInstancesInCurrentSession();
                        if (!coordinator.TryBecomePrimary(2200))
                        {
                            MessageBox.Show("无法结束现有实例。请在任务管理器中结束 SelectionTranslator 后重试。",
                                "划词翻译", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }

                RunPrimaryInstance(coordinator);
            }
        }

        private static void InitializeWindowsForms()
        {
            try
            {
                if (!NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    NativeMethods.SetProcessDPIAware();
            }
            catch { NativeMethods.SetProcessDPIAware(); }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        private static ExistingInstanceAction ShowExistingInstanceDialog()
        {
            using (var form = new ExistingInstanceForm())
            {
                form.ShowDialog();
                return form.SelectedAction;
            }
        }

        private static void RunPrimaryInstance(InstanceCoordinator coordinator)
        {
            try
            {
                Application.ThreadException += OnThreadException;
                Application.Run(new TranslatorApplicationContext(coordinator));
            }
            catch (Exception exception)
            {
                MessageBox.Show("无法启动划词翻译：" + exception.Message, "划词翻译",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Application.ThreadException -= OnThreadException;
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs eventArgs)
        {
            MessageBox.Show("应用发生错误：" + eventArgs.Exception.Message, "划词翻译",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void StopLegacyInstancesInCurrentSession()
        {
            var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName("SelectionTranslator"))
            {
                try
                {
                    if (process.Id != current.Id && process.SessionId == current.SessionId)
                    {
                        process.Kill();
                        process.WaitForExit(1200);
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
    }
}
