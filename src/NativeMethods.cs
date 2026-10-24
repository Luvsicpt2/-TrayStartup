using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TrayStartup
{
    internal static class NativeMethods
    {
        public const int WmAppShow = 0x8000 + 1;
        public const int CsDropShadow = 0x00020000;
        public const int WsExToolWindow = 0x00000080;

        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwcpRound = 2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string className, string windowName);

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        public static IntPtr FindWindowByTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return IntPtr.Zero;
            }

            IntPtr found = IntPtr.Zero;
            EnumWindows((window, parameter) =>
            {
                var text = new StringBuilder(256);
                GetWindowText(window, text, text.Capacity);
                if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
                {
                    found = window;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        public static void ForceForeground(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return;
            }

            IntPtr foreground = GetForegroundWindow();
            uint currentThread = GetCurrentThreadId();
            uint foregroundThread = 0;
            if (foreground != IntPtr.Zero)
            {
                uint processId;
                foregroundThread = GetWindowThreadProcessId(foreground, out processId);
            }

            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                {
                    attached = AttachThreadInput(currentThread, foregroundThread, true);
                }

                SetForegroundWindow(window);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThread, foregroundThread, false);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        private static readonly IntPtr HwndTopMost = new IntPtr(-1);

        public static void BringToTop(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return;
            }

            const uint noSize = 0x0001;
            const uint noMove = 0x0002;
            const uint noActivate = 0x0010;
            const uint showWindow = 0x0040;
            SetWindowPos(
                window,
                HwndTopMost,
                0,
                0,
                0,
                0,
                noSize | noMove | noActivate | showWindow);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

        public static bool TryEnableRoundedCorners(IntPtr window)
        {
            try
            {
                int preference = DwmwcpRound;
                return DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref preference, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        public static void TrySetBorderColor(IntPtr window, System.Drawing.Color color)
        {
            try
            {
                int value = color.R | (color.G << 8) | (color.B << 16);
                DwmSetWindowAttribute(window, DwmwaBorderColor, ref value, sizeof(int));
            }
            catch
            {
                // Older Windows versions do not support a custom DWM border.
            }
        }

        public static int GetSystemDpi()
        {
            IntPtr deviceContext = IntPtr.Zero;
            try
            {
                deviceContext = GetDC(IntPtr.Zero);
                if (deviceContext == IntPtr.Zero)
                {
                    return 96;
                }

                const int logPixelsX = 88;
                int dpi = GetDeviceCaps(deviceContext, logPixelsX);
                return dpi > 0 ? dpi : 96;
            }
            catch
            {
                return 96;
            }
            finally
            {
                if (deviceContext != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, deviceContext);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NotifyIconIdentifier
        {
            public int Size;
            public IntPtr WindowHandle;
            public uint Id;
            public Guid Guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(
            ref NotifyIconIdentifier identifier,
            out NativeRect rectangle);

        public static bool TryGetNotifyIconRect(NotifyIcon notifyIcon, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (notifyIcon == null)
            {
                return false;
            }

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo windowField = typeof(NotifyIcon).GetField("window", flags);
                FieldInfo idField = typeof(NotifyIcon).GetField("id", flags);
                if (windowField == null || idField == null)
                {
                    return false;
                }

                var nativeWindow = windowField.GetValue(notifyIcon) as NativeWindow;
                if (nativeWindow == null || nativeWindow.Handle == IntPtr.Zero)
                {
                    return false;
                }

                object idValue = idField.GetValue(notifyIcon);
                if (idValue == null)
                {
                    return false;
                }

                var identifier = new NotifyIconIdentifier
                {
                    Size = Marshal.SizeOf(typeof(NotifyIconIdentifier)),
                    WindowHandle = nativeWindow.Handle,
                    Id = (uint)Convert.ToInt32(idValue),
                    Guid = Guid.Empty
                };

                NativeRect nativeRect;
                int result = Shell_NotifyIconGetRect(ref identifier, out nativeRect);
                if (result != 0 ||
                    nativeRect.Right <= nativeRect.Left ||
                    nativeRect.Bottom <= nativeRect.Top)
                {
                    return false;
                }

                var candidate = Rectangle.FromLTRB(
                    nativeRect.Left,
                    nativeRect.Top,
                    nativeRect.Right,
                    nativeRect.Bottom);
                if (candidate.Width > 128 || candidate.Height > 128 ||
                    !SystemInformation.VirtualScreen.IntersectsWith(candidate))
                {
                    return false;
                }

                rectangle = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
