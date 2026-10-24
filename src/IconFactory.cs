using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TrayStartup
{
    internal static class IconFactory
    {
        public static Icon LoadTrayIcon()
        {
            Size size = SystemInformation.SmallIconSize;
            return Load(size.Width, size.Height);
        }

        public static Icon LoadWindowIcon()
        {
            return Load(32, 32);
        }

        private static Icon Load(int width, int height)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("TrayStartup.app.ico"))
                {
                    if (stream != null)
                    {
                        return new Icon(stream, new Size(Math.Max(16, width), Math.Max(16, height)));
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Failed to load embedded icon", exception);
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
