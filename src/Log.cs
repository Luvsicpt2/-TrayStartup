using System;
using System.IO;
using System.Text;

namespace TrayStartup
{
    internal static class Log
    {
        private static readonly object Sync = new object();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrayStartup",
            "app.log");

        public static string FilePath
        {
            get { return LogPath; }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (Sync)
                {
                    string directory = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                    {
                        File.Delete(LogPath);
                    }

                    var builder = new StringBuilder();
                    builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    builder.Append(" [");
                    builder.Append(level);
                    builder.Append("] ");
                    builder.AppendLine(message ?? string.Empty);
                    if (exception != null)
                    {
                        builder.AppendLine(exception.ToString());
                    }

                    File.AppendAllText(LogPath, builder.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never break the tray app.
            }
        }
    }
}
