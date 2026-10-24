using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TrayStartup
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly AppStore _store;
        private readonly StartupManager _startup;
        private readonly IconCache _icons;
        private readonly PopupForm _popup;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _selfStartItem;
        private readonly Icon _trayIcon;
        private readonly Font _menuFont;
        private readonly MessageWindow _messageWindow;
        private readonly List<AppEntry> _newEntries = new List<AppEntry>();

        private bool _exiting;
        private bool _disposed;
        private bool _resourcesDisposed;

        public TrayApplicationContext()
        {
            _store = new AppStore();
            _store.Load();

            _startup = new StartupManager(null, "TrayStartup", Application.ExecutablePath);
            _icons = new IconCache();

            _popup = new PopupForm();
            _popup.AddRequested += AddSoftware;
            _popup.ToggleRequested += ToggleApp;
            _popup.RemoveRequested += RemoveApp;
            _popup.OpenLocationRequested += OpenLocation;

            _menuFont = Theme.CreateFont(9.5f);
            _trayMenu = new ContextMenuStrip
            {
                Font = _menuFont,
                ForeColor = Theme.Text,
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Renderer = new ToolStripProfessionalRenderer(new TrayMenuColorTable()),
                Padding = new Padding(10, 5, 10, 5),
                MinimumSize = new Size(196, 0),
                DropShadowEnabled = true
            };

            var addItem = new ToolStripMenuItem("添加软件…")
            {
                Padding = new Padding(10, 5, 10, 5)
            };
            addItem.Click += (sender, args) => AddSoftware();

            _selfStartItem = new ToolStripMenuItem("随 Windows 启动")
            {
                Padding = new Padding(10, 5, 28, 5)
            };
            _selfStartItem.Click += (sender, args) => ToggleSelfStart();

            var exitItem = new ToolStripMenuItem("退出")
            {
                Padding = new Padding(10, 5, 10, 5)
            };
            exitItem.Click += (sender, args) => ExitApplication();

            _trayMenu.Items.Add(addItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_selfStartItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);
            _trayMenu.Opening += (sender, args) =>
            {
                _popup.HidePopup();
                _selfStartItem.Checked = _startup.IsSelfStartEnabled();
            };

            _trayIcon = IconFactory.LoadTrayIcon();
            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "自启开关 · 左键单击管理开机启动",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _notifyIcon.MouseClick += NotifyIconOnMouseClick;

            _messageWindow = new MessageWindow();
            _messageWindow.ShowRequested += () =>
            {
                if (_disposed)
                {
                    return;
                }

                RefreshRows();
                _popup.ShowNearTray(true);
            };

            Log.Info("TrayStartup started. Config: " + _store.FilePath);

            if (!string.IsNullOrEmpty(_store.LoadError))
            {
                MessageBox.Show(
                    _store.LoadError,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void NotifyIconOnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _disposed)
            {
                return;
            }

            ShowPopupAtIcon(GetNotifyIconCenter());
        }

        private Point GetNotifyIconCenter()
        {
            Point click = Cursor.Position;
            Rectangle rectangle;
            if (NativeMethods.TryGetNotifyIconRect(_notifyIcon, out rectangle))
            {
                var center = new Point(
                    rectangle.Left + rectangle.Width / 2,
                    rectangle.Top + rectangle.Height / 2);

                // A hidden overflow icon can report a stale rect. Keep the
                // real click position when the reported icon is far away.
                if (Math.Abs(center.X - click.X) <= 80 && Math.Abs(center.Y - click.Y) <= 80)
                {
                    return center;
                }
            }

            return click;
        }

        private void ShowPopupAtIcon(Point iconCenter)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Log.Info("Show panel at " + iconCenter.X + "," + iconCenter.Y);
                RefreshRows();
                _popup.ShowAtIconCenter(iconCenter);
            }
            catch (Exception exception)
            {
                Log.Error("Failed to show popup", exception);
            }
        }

        private void ShowPopup(bool atTrayCorner)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                RefreshRows();
                _popup.ShowNearTray(atTrayCorner);
            }
            catch (Exception exception)
            {
                Log.Error("Failed to show popup", exception);
            }
        }

        private void RefreshRows()
        {
            var rows = new List<AppRowView>();
            foreach (AppEntry entry in _store.Config.Apps)
            {
                StartupManager.StartupState state = _startup.GetState(entry);
                var row = new AppRowView
                {
                    Entry = entry,
                    Name = entry.Name,
                    Path = entry.ExePath,
                    Enabled = state.Enabled,
                    CanToggle = state.CanToggle,
                    Icon = _icons.Get(entry.ExePath, entry.Name, 64),
                    Tone = StatusTone.Off
                };

                var detail = new StringBuilder();
                detail.Append(entry.ExePath);
                if (!string.IsNullOrWhiteSpace(entry.Arguments))
                {
                    detail.Append(' ');
                    detail.Append(entry.Arguments);
                }

                if (state.Missing)
                {
                    row.Status = "文件不存在";
                    row.Tone = StatusTone.Error;
                    row.CanToggle = false;
                    detail.AppendLine();
                    detail.Append("程序文件不存在。");
                }
                else if (state.Conflict)
                {
                    row.Status = "启动项异常";
                    row.Tone = StatusTone.Error;
                    row.CanToggle = false;
                    detail.AppendLine();
                    detail.Append(state.Error);
                }
                else if (state.Enabled)
                {
                    row.Status = entry.Adopted ? "已开启 · 已接管" : "已开启";
                    row.Tone = StatusTone.On;
                }
                else
                {
                    row.Status = "已关闭";
                    row.Tone = StatusTone.Off;
                }

                if (entry.HasOtherStartup)
                {
                    row.HasWarning = true;
                    if (row.Tone != StatusTone.Error)
                    {
                        row.Tone = StatusTone.Warning;
                        row.Status += " · 另有启动方式";
                    }

                    detail.AppendLine();
                    detail.Append("另有开机启动方式：");
                    detail.Append(entry.OtherStartup);
                    detail.AppendLine();
                    detail.Append("本工具只能管理当前用户的注册表启动项，可能无法完全关闭。");
                }

                row.Detail = detail.ToString();
                rows.Add(row);
            }

            _popup.SetRows(rows, _popup.Visible);
        }

        private void AddSoftware()
        {
            if (_disposed)
            {
                return;
            }

            bool wasVisible = _popup.Visible;
            _popup.HidePopup();

            string[] files;
            using (var dialog = new OpenFileDialog
            {
                Title = "选择要管理的软件",
                Filter = "应用程序 (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = true,
                DereferenceLinks = true,
                RestoreDirectory = true
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    if (wasVisible)
                    {
                        ShowPopup(true);
                    }

                    return;
                }

                files = dialog.FileNames;
            }

            AddApps(files);
        }

        private void AddApps(IEnumerable<string> files)
        {
            _newEntries.Clear();
            var reserved = _store.GetRegistryNames();
            int skipped = 0;
            int adopted = 0;
            var warnings = new List<string>();

            foreach (string file in files)
            {
                string exePath = file;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    skipped++;
                    continue;
                }

                if (_store.FindByPath(exePath) != null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    string name = GetDisplayName(exePath);
                    StartupManager.RunEntryMatch existing =
                        _startup.FindExistingUserRunEntry(exePath, reserved);
                    string arguments = string.Empty;
                    string registryName;
                    bool isAdopted = false;

                    if (existing != null)
                    {
                        registryName = existing.ValueName;
                        arguments = _startup.ExtractArguments(existing.Command, exePath);
                        isAdopted = true;
                        adopted++;
                    }
                    else
                    {
                        registryName = _startup.ChooseValueName(name, exePath, reserved);
                    }

                    var entry = new AppEntry
                    {
                        Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = name,
                        ExePath = exePath,
                        Arguments = arguments,
                        RegistryName = registryName,
                        Adopted = isAdopted,
                        OtherStartup = _startup.GetOtherStartupSources(
                            exePath,
                            isAdopted ? registryName : null)
                    };

                    reserved.Add(registryName);
                    _store.Add(entry);
                    _newEntries.Add(entry);

                    if (entry.HasOtherStartup)
                    {
                        warnings.Add(entry.Name + "：" + entry.OtherStartup);
                    }
                }
                catch (Exception exception)
                {
                    skipped++;
                    Log.Error("Failed to add " + exePath, exception);
                }
            }

            if (_newEntries.Count == 0)
            {
                MessageBox.Show(
                    skipped > 0 ? "没有添加新软件，选中的程序可能已经在列表里了。" : "没有选择可添加的软件。",
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                _store.Save();
            }
            catch (Exception exception)
            {
                foreach (AppEntry entry in _newEntries)
                {
                    _store.Remove(entry);
                }

                _newEntries.Clear();
                MessageBox.Show(
                    "保存配置失败：" + exception.Message,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            RefreshRows();
            _popup.ShowNearTray(true);

            var message = new StringBuilder();
            if (adopted > 0)
            {
                message.Append("已接管 ");
                message.Append(adopted);
                message.AppendLine(" 个现有启动项。");
            }

            if (warnings.Count > 0)
            {
                if (message.Length > 0)
                {
                    message.AppendLine();
                }

                message.AppendLine("以下软件还有别的开机启动方式，本工具无法完全关闭：");
                foreach (string warning in warnings)
                {
                    message.Append("· ");
                    message.AppendLine(warning);
                }

                message.AppendLine();
                message.Append("本工具只管理当前用户的注册表启动项。");
            }

            if (message.Length > 0)
            {
                MessageBox.Show(
                    _popup,
                    message.ToString().TrimEnd(),
                    "添加完成",
                    MessageBoxButtons.OK,
                    warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }

            _newEntries.Clear();
        }

        private void ToggleApp(AppEntry entry)
        {
            if (entry == null || _disposed)
            {
                return;
            }

            StartupManager.StartupState state = _startup.GetState(entry);
            if (!state.CanToggle)
            {
                MessageBox.Show(
                    _popup,
                    string.IsNullOrWhiteSpace(state.Error) ? "当前无法修改这个启动项。" : state.Error,
                    "无法修改",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string error;
            bool success = state.Enabled
                ? _startup.Disable(entry, out error)
                : _startup.Enable(entry, out error);

            if (!success)
            {
                MessageBox.Show(
                    _popup,
                    error ?? "修改启动项失败。",
                    "操作失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RefreshRows();
                return;
            }

            try
            {
                _store.Save();
            }
            catch (Exception exception)
            {
                Log.Error("Failed to save config after toggle", exception);
                MessageBox.Show(
                    _popup,
                    "启动项已修改，但保存配置失败：" + exception.Message,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            RefreshRows();
        }

        private void RemoveApp(AppEntry entry)
        {
            if (entry == null || _disposed)
            {
                return;
            }

            Log.Info("Remove requested: " + entry.Name);

            var message = new StringBuilder();
            message.Append("从列表移除「");
            message.Append(entry.Name);
            message.AppendLine("」并关闭它的开机启动？");
            if (entry.HasOtherStartup)
            {
                message.AppendLine();
                message.Append("注意：它还有 ");
                message.Append(entry.OtherStartup);
                message.AppendLine("。");
                message.Append("本工具只能移除自己管理的那一项。");
            }

            if (MessageBox.Show(
                    _popup,
                    message.ToString(),
                    "移除软件",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            StartupManager.StartupState state = _startup.GetState(entry);
            if (state.Enabled && !state.Conflict)
            {
                string error;
                if (!_startup.Disable(entry, out error))
                {
                    if (MessageBox.Show(
                            _popup,
                            (error ?? "关闭开机启动失败。") + "\n\n仍要从列表移除吗？",
                            "自启开关",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return;
                    }
                }
            }

            _store.Remove(entry);
            try
            {
                _store.Save();
            }
            catch (Exception exception)
            {
                _store.Add(entry);
                Log.Error("Failed to save config after removing " + entry.Name, exception);
                MessageBox.Show(
                    _popup,
                    "保存配置失败，未移除：" + exception.Message,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            Log.Info("Removed: " + entry.Name);
            RefreshRows();
            _popup.ShowNearTray(true);
        }

        private void OpenLocation(AppEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                if (File.Exists(entry.ExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + entry.ExePath + "\"",
                        UseShellExecute = true
                    });
                    return;
                }

                string directory = Path.GetDirectoryName(entry.ExePath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true
                    });
                    return;
                }

                MessageBox.Show(
                    _popup,
                    "程序文件和所在文件夹都不存在。",
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception exception)
            {
                Log.Error("Failed to open location", exception);
                MessageBox.Show(
                    _popup,
                    "无法打开文件位置：" + exception.Message,
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ToggleSelfStart()
        {
            bool current = _startup.IsSelfStartEnabled();
            string error;
            if (!_startup.SetSelfStart(!current, out error))
            {
                MessageBox.Show(
                    error ?? "修改失败。",
                    "自启开关",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            _selfStartItem.Checked = _startup.IsSelfStartEnabled();
        }

        private void ExitApplication()
        {
            if (_exiting || _disposed)
            {
                return;
            }

            _exiting = true;
            _disposed = true;
            _notifyIcon.Visible = false;
            _popup.HidePopup();
            _popup.AllowCloseAndClose();
            Log.Info("TrayStartup exiting.");
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_resourcesDisposed)
            {
                _resourcesDisposed = true;
                _disposed = true;

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }

                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                }

                if (_popup != null)
                {
                    _popup.Dispose();
                }

                if (_icons != null)
                {
                    _icons.Dispose();
                }

                if (_trayIcon != null)
                {
                    _trayIcon.Dispose();
                }

                if (_menuFont != null)
                {
                    _menuFont.Dispose();
                }

                if (_messageWindow != null)
                {
                    _messageWindow.DestroyHandle();
                }
            }

            base.Dispose(disposing);
        }

        private static string GetDisplayName(string exePath)
        {
            string name = null;
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(info.FileDescription))
                {
                    name = info.FileDescription;
                }
                else if (!string.IsNullOrWhiteSpace(info.ProductName))
                {
                    name = info.ProductName;
                }
            }
            catch
            {
                // Fall back to the file name.
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(exePath);
            }

            name = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (name.Contains("  "))
            {
                name = name.Replace("  ", " ");
            }

            if (name.Length > 48)
            {
                name = name.Substring(0, 48).Trim();
            }

            return name;
        }

        private sealed class MessageWindow : NativeWindow
        {
            public MessageWindow()
            {
                var parameters = new CreateParams
                {
                    Caption = AppIdentity.MessageWindowTitle,
                    ClassName = "STATIC",
                    X = 0,
                    Y = 0,
                    Width = 0,
                    Height = 0,
                    Style = 0,
                    ExStyle = 0
                };
                CreateHandle(parameters);
            }

            public event Action ShowRequested;

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NativeMethods.WmAppShow)
                {
                    Action handler = ShowRequested;
                    if (handler != null)
                    {
                        handler();
                    }

                    return;
                }

                base.WndProc(ref message);
            }
        }

        private sealed class TrayMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground
            {
                get { return Color.White; }
            }

            public override Color ImageMarginGradientBegin
            {
                get { return Color.White; }
            }

            public override Color ImageMarginGradientMiddle
            {
                get { return Color.White; }
            }

            public override Color ImageMarginGradientEnd
            {
                get { return Color.White; }
            }

            public override Color MenuBorder
            {
                get { return Color.FromArgb(219, 224, 231); }
            }

            public override Color MenuItemBorder
            {
                get { return Color.FromArgb(232, 235, 240); }
            }

            public override Color MenuItemSelected
            {
                get { return Color.FromArgb(242, 245, 249); }
            }

            public override Color MenuItemSelectedGradientBegin
            {
                get { return Color.FromArgb(242, 245, 249); }
            }

            public override Color MenuItemSelectedGradientEnd
            {
                get { return Color.FromArgb(242, 245, 249); }
            }

            public override Color SeparatorDark
            {
                get { return Color.FromArgb(236, 238, 242); }
            }

            public override Color SeparatorLight
            {
                get { return Color.White; }
            }

            public override Color CheckBackground
            {
                get { return Color.FromArgb(242, 245, 249); }
            }

            public override Color CheckSelectedBackground
            {
                get { return Color.FromArgb(236, 241, 247); }
            }
        }
    }
}
