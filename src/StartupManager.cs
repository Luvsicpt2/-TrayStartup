using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TrayStartup
{
    internal sealed class StartupManager
    {
        public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly string _runKeyPath;
        private readonly string _selfValueName;
        private readonly string _selfExePath;

        public StartupManager(string runKeyPath = null, string selfValueName = "TrayStartup", string selfExePath = null)
        {
            _runKeyPath = string.IsNullOrWhiteSpace(runKeyPath) ? DefaultRunKeyPath : runKeyPath;
            _selfValueName = string.IsNullOrWhiteSpace(selfValueName) ? "TrayStartup" : selfValueName;
            _selfExePath = selfExePath ?? System.Windows.Forms.Application.ExecutablePath;
        }

        public sealed class RunEntryMatch
        {
            public string ValueName { get; set; }
            public string Command { get; set; }
        }

        public sealed class StartupState
        {
            public bool Enabled { get; set; }
            public bool Missing { get; set; }
            public bool Conflict { get; set; }
            public bool CanToggle { get; set; }
            public string Error { get; set; }
            public string CurrentCommand { get; set; }
        }

        public StartupState GetState(AppEntry entry)
        {
            var state = new StartupState { CanToggle = true };
            if (entry == null)
            {
                state.CanToggle = false;
                state.Error = "软件信息无效。";
                return state;
            }

            if (!File.Exists(entry.ExePath))
            {
                state.Missing = true;
                state.CanToggle = false;
                state.Error = "程序文件不存在。";
                return state;
            }

            if (string.IsNullOrWhiteSpace(entry.RegistryName))
            {
                return state;
            }

            try
            {
                string command = ReadRunValue(entry.RegistryName);
                state.CurrentCommand = command;
                if (string.IsNullOrEmpty(command))
                {
                    return state;
                }

                state.Enabled = true;
                if (!CommandTargetsPath(command, entry.ExePath))
                {
                    state.Conflict = true;
                    state.CanToggle = false;
                    state.Error = "同名启动项已被其他程序修改，为避免误删未做更改。";
                }

                return state;
            }
            catch (Exception exception)
            {
                state.CanToggle = false;
                state.Error = "无法读取注册表启动项：" + exception.Message;
                return state;
            }
        }

        public bool Enable(AppEntry entry, out string error)
        {
            error = null;
            if (entry == null)
            {
                error = "软件信息无效。";
                return false;
            }

            if (!File.Exists(entry.ExePath))
            {
                error = "程序文件不存在，无法开启开机启动。";
                return false;
            }

            try
            {
                using (RegistryKey key = EnsureRunKey())
                {
                    if (key == null)
                    {
                        error = "无法打开当前用户的启动项注册表。";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(entry.RegistryName))
                    {
                        entry.RegistryName = ChooseValueName(entry.Name, entry.ExePath, null, key);
                    }

                    string existing = key.GetValue(entry.RegistryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (!string.IsNullOrEmpty(existing) && !CommandTargetsPath(existing, entry.ExePath))
                    {
                        error = "启动项名称已被其他程序占用，未做修改。";
                        return false;
                    }

                    string command = BuildCommand(entry.ExePath, entry.Arguments);
                    key.SetValue(entry.RegistryName, command, RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = "写入启动项失败：" + exception.Message;
                Log.Error("Failed to enable startup for " + entry.Name, exception);
                return false;
            }
        }

        public bool Disable(AppEntry entry, out string error)
        {
            error = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.RegistryName))
            {
                return true;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_runKeyPath, true))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    object raw = key.GetValue(entry.RegistryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (raw == null)
                    {
                        return true;
                    }

                    string command = raw as string;
                    if (string.IsNullOrEmpty(command) || !CommandTargetsPath(command, entry.ExePath))
                    {
                        error = "启动项内容已变化，为避免误删其他程序，未做修改。";
                        return false;
                    }

                    key.DeleteValue(entry.RegistryName, false);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = "删除启动项失败：" + exception.Message;
                Log.Error("Failed to disable startup for " + entry.Name, exception);
                return false;
            }
        }

        public RunEntryMatch FindExistingUserRunEntry(string exePath, ISet<string> excludedNames)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_runKeyPath, false))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    foreach (string valueName in key.GetValueNames())
                    {
                        if (excludedNames != null && excludedNames.Contains(valueName))
                        {
                            continue;
                        }

                        string command = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (!string.IsNullOrEmpty(command) && CommandTargetsPath(command, exePath))
                        {
                            return new RunEntryMatch { ValueName = valueName, Command = command };
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Failed to inspect existing startup entries", exception);
            }

            return null;
        }

        public string ChooseValueName(string appName, string exePath, ISet<string> reservedNames, RegistryKey existingKey = null)
        {
            string baseName = SanitizeValueName(appName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = SanitizeValueName(Path.GetFileNameWithoutExtension(exePath));
            }

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "启动项";
            }

            RegistryKey ownedKey = null;
            try
            {
                RegistryKey key = existingKey ?? (ownedKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, false));
                var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (key != null)
                {
                    foreach (string name in key.GetValueNames())
                    {
                        registered.Add(name);
                    }
                }

                for (int suffix = 1; suffix <= 99; suffix++)
                {
                    string candidate = suffix == 1 ? baseName : baseName + " (" + suffix + ")";
                    if (candidate.Length > 60)
                    {
                        string tail = suffix == 1 ? string.Empty : " (" + suffix + ")";
                        int keep = Math.Max(1, 60 - tail.Length);
                        candidate = baseName.Substring(0, Math.Min(baseName.Length, keep)).TrimEnd() + tail;
                    }

                    if (reservedNames != null && reservedNames.Contains(candidate))
                    {
                        continue;
                    }

                    if (!registered.Contains(candidate))
                    {
                        return candidate;
                    }
                }

                return baseName + " (" + Guid.NewGuid().ToString("N").Substring(0, 4) + ")";
            }
            finally
            {
                if (ownedKey != null)
                {
                    ownedKey.Dispose();
                }
            }
        }

        public string GetOtherStartupSources(string exePath, string managedRegistryName)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return string.Empty;
            }

            try
            {
                CheckUserRunKey(exePath, managedRegistryName, warnings);
                CheckMachineRunKey(exePath, RegistryView.Registry64, "系统级启动项", warnings);
                CheckMachineRunKey(exePath, RegistryView.Registry32, "系统级启动项（32 位）", warnings);
                CheckStartupFolder(exePath, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "当前用户启动文件夹", warnings);
                CheckStartupFolder(exePath, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "所有用户启动文件夹", warnings);
            }
            catch (Exception exception)
            {
                Log.Error("Failed to inspect other startup mechanisms", exception);
            }

            return string.Join("；", warnings);
        }

        public bool IsSelfStartEnabled()
        {
            try
            {
                string command = ReadRunValue(_selfValueName);
                return !string.IsNullOrEmpty(command) && CommandTargetsPath(command, _selfExePath);
            }
            catch (Exception exception)
            {
                Log.Error("Failed to read self-start entry", exception);
                return false;
            }
        }

        public bool SetSelfStart(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = EnsureRunKey())
                {
                    if (key == null)
                    {
                        error = "无法打开当前用户的启动项注册表。";
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(_selfValueName, BuildCommand(_selfExePath, string.Empty), RegistryValueKind.String);
                        return true;
                    }

                    object raw = key.GetValue(_selfValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (raw == null)
                    {
                        return true;
                    }

                    string command = raw as string;
                    if (string.IsNullOrEmpty(command) || !CommandTargetsPath(command, _selfExePath))
                    {
                        error = "“随 Windows 启动”项已被其他程序修改，未做更改。";
                        return false;
                    }

                    key.DeleteValue(_selfValueName, false);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = "修改“随 Windows 启动”失败：" + exception.Message;
                Log.Error("Failed to change self-start", exception);
                return false;
            }
        }

        public string ExtractArguments(string command, string exePath)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return string.Empty;
            }

            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            string target = GetCommandTarget(expanded);
            if (string.IsNullOrEmpty(target))
            {
                return string.Empty;
            }

            if (expanded.StartsWith("\"", StringComparison.Ordinal))
            {
                int closingQuote = expanded.IndexOf('"', 1);
                if (closingQuote > 0)
                {
                    return expanded.Substring(closingQuote + 1).Trim();
                }
            }

            if (expanded.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            {
                return expanded.Substring(target.Length).Trim();
            }

            int index = expanded.IndexOf(target, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return expanded.Substring(index + target.Length).Trim();
            }

            return string.Empty;
        }

        public static string GetCommandTarget(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            if (expanded.Length == 0)
            {
                return null;
            }

            if (expanded[0] == '"')
            {
                int closingQuote = expanded.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    return expanded.Substring(1, closingQuote - 1).Trim();
                }

                return expanded.Trim('"');
            }

            int exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                return expanded.Substring(0, exeIndex + 4).Trim();
            }

            int spaceIndex = expanded.IndexOf(' ');
            return (spaceIndex > 0 ? expanded.Substring(0, spaceIndex) : expanded).Trim();
        }

        public static bool CommandTargetsPath(string command, string exePath)
        {
            string target = GetCommandTarget(command);
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(exePath))
            {
                return false;
            }

            string normalizedTarget = NormalizePath(target);
            string normalizedExe = NormalizePath(exePath);
            if (string.Equals(normalizedTarget, normalizedExe, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Some legacy entries store an unquoted path with arguments. If the
            // target has no directory, compare the file name as a last resort.
            try
            {
                if (!Path.IsPathRooted(target))
                {
                    return string.Equals(
                        Path.GetFileName(target),
                        Path.GetFileName(exePath),
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Ignore malformed paths.
            }

            return false;
        }

        public static string BuildCommand(string exePath, string arguments)
        {
            string command = "\"" + exePath + "\"";
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                command += " " + arguments.Trim();
            }

            return command;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\');
            }
            catch
            {
                return path.Trim().Trim('"').TrimEnd('\\');
            }
        }

        private string ReadRunValue(string valueName)
        {
            if (string.IsNullOrWhiteSpace(valueName))
            {
                return null;
            }

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_runKeyPath, false))
            {
                if (key == null)
                {
                    return null;
                }

                return key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }

        private RegistryKey EnsureRunKey()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(_runKeyPath, true);
            if (key == null)
            {
                key = Registry.CurrentUser.CreateSubKey(_runKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree);
            }

            return key;
        }

        private void CheckUserRunKey(string exePath, string managedRegistryName, List<string> warnings)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_runKeyPath, false))
            {
                if (key == null)
                {
                    return;
                }

                foreach (string valueName in key.GetValueNames())
                {
                    if (!string.IsNullOrEmpty(managedRegistryName) &&
                        string.Equals(valueName, managedRegistryName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string command = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (!string.IsNullOrEmpty(command) && CommandTargetsPath(command, exePath))
                    {
                        AddWarning(warnings, "另一个当前用户启动项");
                    }
                }
            }
        }

        private void CheckMachineRunKey(string exePath, RegistryView view, string description, List<string> warnings)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key = baseKey.OpenSubKey(_runKeyPath, false))
                {
                    if (key == null)
                    {
                        return;
                    }

                    foreach (string valueName in key.GetValueNames())
                    {
                        string command = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (!string.IsNullOrEmpty(command) && CommandTargetsPath(command, exePath))
                        {
                            AddWarning(warnings, description);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Failed to inspect machine startup key", exception);
            }
        }

        private void CheckStartupFolder(string exePath, string folder, string description, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            try
            {
                foreach (string shortcut in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    string target = GetShortcutTarget(shortcut);
                    if (!string.IsNullOrEmpty(target) && CommandTargetsPath(target, exePath))
                    {
                        AddWarning(warnings, description);
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Failed to inspect startup folder " + folder, exception);
            }
        }

        private static string GetShortcutTarget(string shortcutPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return null;
                }

                dynamic shellObject = Activator.CreateInstance(shellType);
                shell = shellObject;
                dynamic shortcutObject = shellObject.CreateShortcut(shortcutPath);
                shortcut = shortcutObject;
                string target = shortcutObject.TargetPath;
                return target;
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    if (shortcut != null && Marshal.IsComObject(shortcut))
                    {
                        Marshal.FinalReleaseComObject(shortcut);
                    }
                }
                catch
                {
                    // Ignore COM cleanup failures.
                }

                try
                {
                    if (shell != null && Marshal.IsComObject(shell))
                    {
                        Marshal.FinalReleaseComObject(shell);
                    }
                }
                catch
                {
                    // Ignore COM cleanup failures.
                }
            }
        }

        private static void AddWarning(List<string> warnings, string warning)
        {
            if (!warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }

        private static string SanitizeValueName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char character in value.Trim())
            {
                if (character == '\\' || character == '/' || character == ':' || char.IsControl(character) ||
                    Array.IndexOf(invalid, character) >= 0)
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(character);
                }
            }

            string cleaned = builder.ToString();
            while (cleaned.Contains("  "))
            {
                cleaned = cleaned.Replace("  ", " ");
            }

            cleaned = cleaned.Trim();
            if (cleaned.Length > 48)
            {
                cleaned = cleaned.Substring(0, 48).Trim();
            }

            return cleaned;
        }
    }
}
