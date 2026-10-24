using System;
using System.Threading;
using System.Windows.Forms;

namespace TrayStartup
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SelfTest.Run(args[1]);
                return;
            }

            if (args.Length >= 2 && string.Equals(args[0], "--render-preview", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    PreviewRenderer.Render(args[1]);
                    Environment.ExitCode = 0;
                }
                catch (Exception exception)
                {
                    Log.Error("Failed to render preview", exception);
                    Environment.ExitCode = 1;
                }

                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, eventArgs) =>
            {
                Exception exception = eventArgs.Exception;
                Log.Error("Unhandled UI exception", exception);
                MessageBox.Show(
                    "程序遇到错误：" + exception.Message + "\n\n日志：" + Log.FilePath,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                Log.Error("Unhandled application exception", eventArgs.ExceptionObject as Exception);
            };

            bool createdNew;
            _mutex = new Mutex(true, AppIdentity.MutexName, out createdNew);
            if (!createdNew)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    IntPtr existing = NativeMethods.FindWindowByTitle(
                        AppIdentity.MessageWindowTitle);
                    if (existing != IntPtr.Zero)
                    {
                        NativeMethods.PostMessage(existing, NativeMethods.WmAppShow, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }

                    Thread.Sleep(100);
                }

                return;
            }

            try
            {
                using (var context = new TrayApplicationContext())
                {
                    Application.Run(context);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Fatal startup error", exception);
                MessageBox.Show(
                    "启动失败：" + exception.Message + "\n\n日志：" + Log.FilePath,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // The mutex may not be owned if startup failed early.
                }

                _mutex.Dispose();
            }
        }
    }
}
