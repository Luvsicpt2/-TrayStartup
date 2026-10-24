using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TrayStartup
{
    internal sealed class AppStore
    {
        private readonly string _filePath;

        public AppStore(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TrayStartup",
                "apps.json");
            Config = new AppConfig();
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public string BackupPath
        {
            get { return _filePath + ".bak"; }
        }

        public AppConfig Config { get; private set; }

        public string LoadError { get; private set; }

        public void Load()
        {
            LoadError = null;
            Config = new AppConfig();

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var loaded = serializer.ReadObject(stream) as AppConfig;
                    if (loaded == null)
                    {
                        throw new InvalidDataException("配置文件内容为空。");
                    }

                    Config = Normalize(loaded);
                }
            }
            catch (Exception exception)
            {
                string corruptPath = _filePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try
                {
                    File.Copy(_filePath, corruptPath, true);
                }
                catch
                {
                    // Best effort; the original file is left in place.
                }

                LoadError = "配置文件读取失败，已备份为 " + Path.GetFileName(corruptPath) + "，并重新开始。";
                Log.Error("Failed to load config", exception);
                Config = new AppConfig();
            }
        }

        private static AppConfig Normalize(AppConfig config)
        {
            if (config.Apps == null)
            {
                config.Apps = new List<AppEntry>();
            }

            var cleaned = new List<AppEntry>();
            foreach (AppEntry entry in config.Apps)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ExePath))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                }

                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    entry.Name = Path.GetFileNameWithoutExtension(entry.ExePath);
                }

                entry.ExePath = entry.ExePath.Trim();
                entry.Name = entry.Name.Trim();
                entry.Arguments = (entry.Arguments ?? string.Empty).Trim();
                entry.RegistryName = (entry.RegistryName ?? string.Empty).Trim();
                entry.OtherStartup = (entry.OtherStartup ?? string.Empty).Trim();
                cleaned.Add(entry);
            }

            config.Apps = cleaned;
            if (config.Version <= 0)
            {
                config.Version = 1;
            }

            return config;
        }

        public void Save()
        {
            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var serializer = new DataContractJsonSerializer(typeof(AppConfig));
            string json;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, Config);
                json = Encoding.UTF8.GetString(stream.ToArray());
            }

            json = JsonPretty.Format(json) + Environment.NewLine;
            string temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush();
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(temporaryPath, _filePath, BackupPath, true);
                }
                else
                {
                    File.Move(temporaryPath, _filePath);
                }
            }
            catch (Exception exception)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                    // Ignore cleanup failures.
                }

                Log.Error("Failed to save config", exception);
                throw;
            }
        }

        public void Add(AppEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            Config.Apps.Add(entry);
        }

        public void Remove(AppEntry entry)
        {
            if (entry != null)
            {
                Config.Apps.Remove(entry);
            }
        }

        public AppEntry FindByPath(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            foreach (AppEntry entry in Config.Apps)
            {
                if (string.Equals(NormalizePath(entry.ExePath), NormalizePath(exePath), StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public HashSet<string> GetRegistryNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppEntry entry in Config.Apps)
            {
                if (!string.IsNullOrWhiteSpace(entry.RegistryName))
                {
                    names.Add(entry.RegistryName.Trim());
                }
            }

            return names;
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim()).TrimEnd('\\');
            }
            catch
            {
                return path.Trim().TrimEnd('\\');
            }
        }
    }

    internal static class JsonPretty
    {
        public static string Format(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(json.Length + 64);
            int indent = 0;
            bool inString = false;
            bool escaped = false;

            for (int index = 0; index < json.Length; index++)
            {
                char character = json[index];
                if (inString)
                {
                    builder.Append(character);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        inString = true;
                        builder.Append(character);
                        break;
                    case '{':
                    case '[':
                        builder.Append(character);
                        if (!NextSignificantIsClose(json, index))
                        {
                            builder.AppendLine();
                            indent++;
                            AppendIndent(builder, indent);
                        }

                        break;
                    case '}':
                    case ']':
                        if (PreviousSignificantIsOpen(json, index))
                        {
                            builder.Append(character);
                        }
                        else
                        {
                            builder.AppendLine();
                            indent = Math.Max(0, indent - 1);
                            AppendIndent(builder, indent);
                            builder.Append(character);
                        }

                        break;
                    case ',':
                        builder.Append(character);
                        builder.AppendLine();
                        AppendIndent(builder, indent);
                        break;
                    case ':':
                        builder.Append(": ");
                        break;
                    case ' ':
                    case '\r':
                    case '\n':
                    case '\t':
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        private static bool NextSignificantIsClose(string json, int index)
        {
            for (int i = index + 1; i < json.Length; i++)
            {
                char character = json[i];
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                return character == '}' || character == ']';
            }

            return false;
        }

        private static bool PreviousSignificantIsOpen(string json, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                char character = json[i];
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                return character == '{' || character == '[';
            }

            return false;
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }
    }
}
