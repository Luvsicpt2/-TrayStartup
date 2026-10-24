using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TrayStartup
{
    internal static class SelfTest
    {
        public static int Run(string logPath)
        {
            var lines = new List<string>();
            int failures = 0;
            string root = Path.Combine(Path.GetTempPath(), "TrayStartupSelfTest-" + Guid.NewGuid().ToString("N"));
            string keyPath = @"Software\TrayStartupSelfTest\" + Guid.NewGuid().ToString("N");

            Action<string, bool, string> check = (name, condition, detail) =>
            {
                if (condition)
                {
                    lines.Add("PASS  " + name);
                }
                else
                {
                    failures++;
                    lines.Add("FAIL  " + name + (string.IsNullOrWhiteSpace(detail) ? string.Empty : "  [" + detail + "]"));
                }
            };

            try
            {
                try
                {
                    Directory.CreateDirectory(root);
                }
                catch (Exception exception)
                {
                    lines.Add("INFO  temp directory unavailable: " + exception.Message);
                    root = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "selftest-temp-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(root);
                }

                string probeDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TrayStartup",
                    "selftest-probe-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(probeDirectory);
                    File.WriteAllText(Path.Combine(probeDirectory, "probe.txt"), "ok");
                    check("AppData config directory is writable", true, null);
                    Directory.Delete(probeDirectory, true);
                }
                catch (Exception exception)
                {
                    check("AppData config directory is writable", false, exception.Message);
                    try
                    {
                        if (Directory.Exists(probeDirectory))
                        {
                            Directory.Delete(probeDirectory, true);
                        }
                    }
                    catch
                    {
                        // Ignore probe cleanup failures.
                    }
                }
                string fakeExe = Path.Combine(root, "Fake App.exe");
                File.WriteAllText(fakeExe, "stub");

                var manager = new StartupManager(keyPath, "SelfTest", fakeExe);
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    key.SetValue("Unrelated", "\"C:\\Other\\Other.exe\"", Microsoft.Win32.RegistryValueKind.String);
                }

                var entry = new AppEntry
                {
                    Id = "test0001",
                    Name = "Fake App",
                    ExePath = fakeExe,
                    RegistryName = "TestApp",
                    Arguments = string.Empty
                };

                string error;
                check("enable writes Run value", manager.Enable(entry, out error), error);
                check("enabled state", manager.GetState(entry).Enabled, "state should be enabled");
                check(
                    "Run command is quoted",
                    manager.GetState(entry).CurrentCommand == "\"" + fakeExe + "\"",
                    manager.GetState(entry).CurrentCommand);
                check("unrelated Run value preserved", ReadKey(keyPath, "Unrelated") != null, "unrelated value was removed");
                check("disable removes only managed value", manager.Disable(entry, out error), error);
                check("disabled state", !manager.GetState(entry).Enabled, "state should be disabled");
                check("unrelated Run value still preserved", ReadKey(keyPath, "Unrelated") != null, "unrelated value was removed");

                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    key.SetValue(
                        "Existing",
                        "\"" + fakeExe + "\" --tray",
                        Microsoft.Win32.RegistryValueKind.String);
                }

                StartupManager.RunEntryMatch match =
                    manager.FindExistingUserRunEntry(fakeExe, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                check("finds an existing Run entry", match != null && match.ValueName == "Existing",
                    match == null ? "not found" : match.ValueName);
                check("extracts existing arguments",
                    match != null && manager.ExtractArguments(match.Command, fakeExe) == "--tray",
                    match == null ? "no match" : manager.ExtractArguments(match.Command, fakeExe));
                check("parses quoted command line",
                    StartupManager.CommandTargetsPath("\"" + fakeExe + "\" --tray", fakeExe),
                    "quoted path mismatch");
                check("parses unquoted command line",
                    StartupManager.CommandTargetsPath(fakeExe + " -silent", fakeExe),
                    "unquoted path mismatch");

                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    key.SetValue("Fake App", "\"C:\\Other\\Other.exe\"", Microsoft.Win32.RegistryValueKind.String);
                }

                string chosen = manager.ChooseValueName("Fake App", fakeExe, new HashSet<string>());
                check("chooses a non-conflicting value name",
                    chosen != "Fake App" && chosen.StartsWith("Fake App", StringComparison.Ordinal),
                    chosen);

                check("self-start can be enabled", manager.SetSelfStart(true, out error), error);
                check("self-start reads enabled", manager.IsSelfStartEnabled(), "self-start should be on");
                check("self-start can be disabled", manager.SetSelfStart(false, out error), error);
                check("self-start reads disabled", !manager.IsSelfStartEnabled(), "self-start should be off");

                string configPath = Path.Combine(root, "apps.json");
                var store = new AppStore(configPath);
                store.Add(entry);
                store.Save();
                check("config file written", File.Exists(configPath), configPath);
                string json = File.ReadAllText(configPath, Encoding.UTF8);
                check("config is pretty JSON", json.Contains("\n") && json.Contains("\"apps\""), json);
                check("config contains app", json.Contains("Fake App") && json.Contains("Fake App.exe"), json);

                var reloaded = new AppStore(configPath);
                reloaded.Load();
                check("config reloads", reloaded.LoadError == null && reloaded.Config.Apps.Count == 1,
                    reloaded.LoadError);
                if (reloaded.Config.Apps.Count == 1)
                {
                    check("config path survives", reloaded.Config.Apps[0].ExePath == fakeExe,
                        reloaded.Config.Apps[0].ExePath);
                }

                reloaded.Save();
                check("config backup created", File.Exists(configPath + ".bak"), configPath + ".bak");

                string brokenPath = Path.Combine(root, "broken.json");
                File.WriteAllText(brokenPath, "this is not json", Encoding.UTF8);
                var broken = new AppStore(brokenPath);
                broken.Load();
                check("corrupt config is handled", broken.LoadError != null && broken.Config.Apps.Count == 0,
                    broken.LoadError);
            }
            catch (Exception exception)
            {
                failures++;
                lines.Add("FAIL  unexpected exception: " + exception);
            }
            finally
            {
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
                }
                catch
                {
                    // Ignore cleanup failures.
                }

                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch
                {
                    // Ignore cleanup failures.
                }
            }

            lines.Add(failures == 0 ? "SELFTEST PASS" : "SELFTEST FAIL (" + failures + " failure(s))");
            try
            {
                string directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllLines(logPath, lines, new UTF8Encoding(false));
            }
            catch
            {
                return 2;
            }

            return failures == 0 ? 0 : 1;
        }

        private static string ReadKey(string keyPath, string valueName)
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                if (key == null)
                {
                    return null;
                }

                return key.GetValue(valueName, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }
    }
}
