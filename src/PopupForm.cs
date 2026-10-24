using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace TrayStartup
{
    internal sealed class PopupForm : Form
    {
        private const int HeaderHeight96 = 66;
        private const int RowHeight96 = 58;
        private const int FooterHeight96 = 46;
        private const int EmptyHeight96 = 142;
        private const int Width96 = 320;
        private const int MaxVisibleRows = 6;

        private readonly List<AppRowView> _rows = new List<AppRowView>();
        private readonly Dictionary<string, float> _togglePositions =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private readonly Timer _hideTimer;
        private readonly Timer _animationTimer;
        private readonly Timer _fadeTimer;
        private readonly Timer _topMostTimer;
        private readonly ToolTip _toolTip;
        private readonly ContextMenuStrip _rowMenu;
        private readonly ToolStripMenuItem _openLocationItem;
        private readonly ToolStripMenuItem _removeItem;

        private readonly Font _titleFont;
        private readonly Font _subtitleFont;
        private readonly Font _nameFont;
        private readonly Font _statusFont;
        private readonly Font _countFont;
        private readonly Font _footerFont;
        private readonly Font _footerHintFont;
        private readonly Font _emptyTitleFont;
        private readonly Font _emptySubtitleFont;
        private readonly Font _menuFont;
        private readonly Icon _windowIcon;

        private float _scale = 1f;
        private int _contentHeight;
        private int _scrollOffset;
        private int _maxScroll;
        private int _hoverRow = -1;
        private bool _footerHover;
        private bool _menuOpen;
        private bool _previewMode;
        private bool _allowClose;
        private bool _dwmRounded;
        private Point _trayAnchor;
        private int _awayTicks;
        private DateTime _holdUntil = DateTime.MinValue;
        private AppEntry _menuEntry;

        public PopupForm()
        {
            _scale = NativeMethods.GetSystemDpi() / 96f;

            _titleFont = Theme.CreateFont(11.5f, FontStyle.Bold);
            _subtitleFont = Theme.CreateFont(8.5f);
            _nameFont = Theme.CreateFont(9.75f);
            _statusFont = Theme.CreateFont(8.25f);
            _countFont = Theme.CreateFont(8.25f);
            _footerFont = Theme.CreateFont(9.5f);
            _footerHintFont = Theme.CreateFont(8f);
            _emptyTitleFont = Theme.CreateFont(10.5f, FontStyle.Bold);
            _emptySubtitleFont = Theme.CreateFont(8.75f);
            _menuFont = Theme.CreateFont(9.5f);

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96f, 96f);
            DoubleBuffered = true;
            BackColor = Theme.Background;
            Padding = Padding.Empty;
            Text = "自启开关";
            AccessibleName = "开机启动管理";
            Font = _nameFont;

            _windowIcon = IconFactory.LoadWindowIcon();
            Icon = _windowIcon;

            _hideTimer = new Timer { Interval = 80 };
            _hideTimer.Tick += HideTimerOnTick;
            _animationTimer = new Timer { Interval = 15 };
            _animationTimer.Tick += AnimationTimerOnTick;
            _fadeTimer = new Timer { Interval = 15 };
            _fadeTimer.Tick += FadeTimerOnTick;
            _topMostTimer = new Timer { Interval = 350 };
            _topMostTimer.Tick += TopMostTimerOnTick;

            _toolTip = new ToolTip
            {
                InitialDelay = 550,
                ReshowDelay = 100,
                AutoPopDelay = 7000,
                ShowAlways = true
            };

            _rowMenu = new ContextMenuStrip
            {
                Font = _menuFont,
                ForeColor = Theme.Text,
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Renderer = new ToolStripProfessionalRenderer(new MenuColorTable()),
                Padding = new Padding(S(4), S(4), S(4), S(4)),
                MinimumSize = new Size(S(176), 0),
                DropShadowEnabled = true
            };

            _openLocationItem = new ToolStripMenuItem("打开文件所在位置")
            {
                Padding = new Padding(S(8), S(4), S(8), S(4))
            };
            _openLocationItem.Click += (sender, args) =>
            {
                AppEntry entry = _openLocationItem.Tag as AppEntry ?? _menuEntry;
                if (entry == null)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!IsDisposed)
                        {
                            OpenLocationRequested?.Invoke(entry);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // The popup is closing; no action is needed.
                }
            };

            _removeItem = new ToolStripMenuItem("移除并关闭自启")
            {
                Padding = new Padding(S(8), S(4), S(8), S(4))
            };
            _removeItem.Click += (sender, args) =>
            {
                AppEntry entry = _removeItem.Tag as AppEntry ?? _menuEntry;
                Log.Info("Remove menu clicked: " + (entry == null ? "(null)" : entry.Name));
                if (entry == null)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!IsDisposed)
                        {
                            RemoveRequested?.Invoke(entry);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // The popup is closing; no action is needed.
                }
            };

            _rowMenu.Items.Add(_openLocationItem);
            _rowMenu.Items.Add(_removeItem);
            _rowMenu.Closed += (sender, args) =>
            {
                _menuOpen = false;
                _awayTicks = 0;
            };

            ApplyLayout();
        }

        public event Action AddRequested;

        public event Action<AppEntry> ToggleRequested;

        public event Action<AppEntry> RemoveRequested;

        public event Action<AppEntry> OpenLocationRequested;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WsExToolWindow;
                parameters.ClassStyle |= NativeMethods.CsDropShadow;
                return parameters;
            }
        }

        public void SetRows(IList<AppRowView> rows, bool animate)
        {
            _rows.Clear();
            if (rows != null)
            {
                _rows.AddRange(rows);
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppRowView row in _rows)
            {
                string id = GetRowId(row);
                ids.Add(id);
                if (!animate || !_togglePositions.ContainsKey(id))
                {
                    _togglePositions[id] = row.Enabled ? 1f : 0f;
                }
            }

            var stale = new List<string>();
            foreach (string key in _togglePositions.Keys)
            {
                if (!ids.Contains(key))
                {
                    stale.Add(key);
                }
            }

            foreach (string key in stale)
            {
                _togglePositions.Remove(key);
            }

            if (_hoverRow >= _rows.Count)
            {
                _hoverRow = -1;
            }

            if (!animate || !Visible)
            {
                SnapAnimations();
            }

            ApplyLayout();

            if (animate && Visible)
            {
                _animationTimer.Start();
            }

            Invalidate();
        }

        public void ShowNearTray(bool atTrayCorner = false)
        {
            if (IsDisposed)
            {
                return;
            }

            ApplyLayout();

            Point cursor = Cursor.Position;
            Screen screen = atTrayCorner ? Screen.PrimaryScreen : Screen.FromPoint(cursor);
            Rectangle work = screen.WorkingArea;
            Rectangle bounds = screen.Bounds;
            int margin = S(10);
            int x;
            int y;

            bool taskbarBottom = work.Bottom < bounds.Bottom;
            bool taskbarTop = work.Top > bounds.Top;
            bool taskbarLeft = work.Left > bounds.Left;
            bool taskbarRight = work.Right < bounds.Right;

            if (atTrayCorner)
            {
                x = work.Right - Width - margin;
                y = work.Bottom - Height - margin;
            }
            else if (taskbarBottom)
            {
                x = cursor.X - Width + S(30);
                y = work.Bottom - Height - margin;
            }
            else if (taskbarTop)
            {
                x = cursor.X - Width + S(30);
                y = work.Top + margin;
            }
            else if (taskbarLeft)
            {
                x = work.Left + margin;
                y = cursor.Y - Height / 2;
            }
            else if (taskbarRight)
            {
                x = work.Right - Width - margin;
                y = cursor.Y - Height / 2;
            }
            else
            {
                x = cursor.X - Width + S(30);
                y = work.Bottom - Height - margin;
            }

            int leftLimit = work.Left + margin;
            int topLimit = work.Top + margin;
            int rightLimit = work.Right - Width - margin;
            int bottomLimit = work.Bottom - Height - margin;
            x = Math.Max(leftLimit, Math.Min(x, rightLimit));
            y = Math.Max(topLimit, Math.Min(y, bottomLimit));

            Point anchor = atTrayCorner
                ? new Point(work.Right - margin, work.Bottom - margin)
                : cursor;
            PresentAt(new Point(x, y), anchor, atTrayCorner);
        }

        public void ShowAtIconCenter(Point iconCenter)
        {
            if (IsDisposed)
            {
                return;
            }

            ApplyLayout();

            Screen screen = Screen.FromPoint(iconCenter);
            Rectangle work = screen.WorkingArea;
            int margin = S(10);
            int left = iconCenter.X;
            int top = iconCenter.Y - Height;

            // Normally the bottom-left corner sits on the icon center. If the
            // icon is too close to the right edge, extend left instead so the
            // whole panel stays visible (the bottom-right corner then touches
            // the icon center).
            if (left + Width > work.Right - margin)
            {
                left = iconCenter.X - Width;
            }

            if (left < work.Left + margin)
            {
                left = work.Left + margin;
            }

            if (top < work.Top + margin)
            {
                top = iconCenter.Y;
                if (top + Height > screen.Bounds.Bottom - margin)
                {
                    top = Math.Max(work.Top + margin, screen.Bounds.Bottom - Height - margin);
                }
            }

            PresentAt(new Point(left, top), iconCenter, false);
        }

        private void PresentAt(Point location, Point anchor, bool holdOpen)
        {
            _trayAnchor = anchor;
            Location = location;
            _awayTicks = 0;
            _holdUntil = holdOpen ? DateTime.UtcNow.AddSeconds(4) : DateTime.MinValue;

            if (!Visible)
            {
                Opacity = 0d;
                Show();
                _fadeTimer.Start();
            }
            else
            {
                BringToFront();
            }

            TopMost = true;
            try
            {
                Activate();
            }
            catch (InvalidOperationException)
            {
                // The form can be closing while a click is being handled.
            }

            NativeMethods.ForceForeground(Handle);
            NativeMethods.BringToTop(Handle);
            _topMostTimer.Stop();
            _topMostTimer.Start();

            _hideTimer.Start();
            if (!_previewMode)
            {
                _animationTimer.Start();
            }
        }

        public void ShowForPreview(Point location)
        {
            _previewMode = true;
            ApplyLayout();
            Location = location;
            Opacity = 1d;
            Show();
            _hideTimer.Stop();
            _fadeTimer.Stop();
            _animationTimer.Stop();
            SnapAnimations();
            Invalidate();
            Application.DoEvents();
        }

        public void HidePopup()
        {
            if (!Visible)
            {
                return;
            }

            _hideTimer.Stop();
            _fadeTimer.Stop();
            _animationTimer.Stop();
            _topMostTimer.Stop();
            _toolTip.SetToolTip(this, string.Empty);
            _hoverRow = -1;
            _footerHover = false;
            _holdUntil = DateTime.MinValue;
            Hide();
            Opacity = 1d;
        }

        public void AllowCloseAndClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    _scale = graphics.DpiX > 0 ? graphics.DpiX / 96f : _scale;
                }
            }
            catch
            {
                _scale = NativeMethods.GetSystemDpi() / 96f;
            }

            _dwmRounded = NativeMethods.TryEnableRoundedCorners(Handle);
            NativeMethods.TrySetBorderColor(Handle, Theme.Border);
            ApplyLayout();
            UpdateWindowRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateWindowRegion();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                _awayTicks = 0;
                _hideTimer.Start();
            }
            else
            {
                _hideTimer.Stop();
                _fadeTimer.Stop();
                _animationTimer.Stop();
                _topMostTimer.Stop();
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_previewMode || _menuOpen)
            {
                return;
            }

            if (IsHandleCreated)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && Visible && !_menuOpen && !ContainsFocus)
                    {
                        HidePopup();
                    }
                }));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePopup();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                HidePopup();
                return true;
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int row = HitTestRow(e.Location);
            bool footer = FooterRectangle.Contains(e.Location);
            bool empty = _rows.Count == 0 && ContentRectangle.Contains(e.Location);

            if (row != _hoverRow || footer != _footerHover)
            {
                _hoverRow = row;
                _footerHover = footer;
                _toolTip.SetToolTip(this, row >= 0 ? _rows[row].Detail : string.Empty);
                Invalidate();
            }

            bool overToggle = row >= 0 && ToggleRectangle(row).Contains(e.Location) && _rows[row].CanToggle;
            Cursor = overToggle || footer || empty ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != -1 || _footerHover)
            {
                _hoverRow = -1;
                _footerHover = false;
                _toolTip.SetToolTip(this, string.Empty);
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_maxScroll > 0)
            {
                _scrollOffset += e.Delta > 0 ? -RowHeight : RowHeight;
                _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, _maxScroll));
                Invalidate();
                return;
            }

            base.OnMouseWheel(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right)
            {
                int menuRow = HitTestRow(e.Location);
                if (menuRow >= 0)
                {
                    OpenRowMenu(menuRow, e.Location);
                }

                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (FooterRectangle.Contains(e.Location) ||
                (_rows.Count == 0 && ContentRectangle.Contains(e.Location)))
            {
                AddRequested?.Invoke();
                return;
            }

            int row = HitTestRow(e.Location);
            if (row >= 0 && ToggleRectangle(row).Contains(e.Location) && _rows[row].CanToggle)
            {
                ToggleRequested?.Invoke(_rows[row].Entry);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(Theme.Background))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            DrawHeader(graphics);
            DrawContent(graphics);
            DrawFooter(graphics);
            DrawBorder(graphics);
        }

        private void DrawHeader(Graphics graphics)
        {
            int headerHeight = HeaderHeight;
            using (var background = new SolidBrush(Theme.HeaderBackground))
            {
                graphics.FillRectangle(background, 0, 0, ClientSize.Width, headerHeight);
            }

            int titleWidth = Math.Max(S(80), ClientSize.Width - S(120));
            TextRenderer.DrawText(
                graphics,
                "开机启动",
                _titleFont,
                new Rectangle(S(18), S(9), titleWidth, S(28)),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(
                graphics,
                "只管理你手动添加的软件",
                _subtitleFont,
                new Rectangle(S(18), S(36), titleWidth, S(18)),
                Theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            string countText = _rows.Count + " 个";
            Size countSize = TextRenderer.MeasureText(
                countText,
                _countFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int pillHeight = S(22);
            int pillWidth = countSize.Width + S(18);
            int pillX = ClientSize.Width - S(18) - pillWidth;
            int pillY = S(15);

            Theme.FillRounded(
                graphics,
                Theme.Pill,
                new RectangleF(pillX, pillY, pillWidth, pillHeight),
                pillHeight / 2f);

            TextRenderer.DrawText(
                graphics,
                countText,
                _countFont,
                new Rectangle(pillX, pillY, pillWidth, pillHeight),
                Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            using (var divider = new Pen(Theme.Divider, 1f))
            {
                graphics.DrawLine(divider, 0, headerHeight - 1, ClientSize.Width, headerHeight - 1);
            }
        }

        private void DrawContent(Graphics graphics)
        {
            if (_rows.Count == 0)
            {
                DrawEmpty(graphics);
                return;
            }

            GraphicsState state = graphics.Save();
            graphics.SetClip(ContentRectangle);

            int first = Math.Max(0, _scrollOffset / RowHeight - 1);
            int last = Math.Min(_rows.Count - 1, first + MaxVisibleRows + 2);
            for (int index = first; index <= last; index++)
            {
                int top = RowTop(index);
                if (top > ContentRectangle.Bottom + RowHeight)
                {
                    break;
                }

                if (top + RowHeight < ContentRectangle.Top)
                {
                    continue;
                }

                DrawRow(graphics, index, top);
            }

            graphics.Restore(state);
            DrawScrollbar(graphics);
        }

        private void DrawEmpty(Graphics graphics)
        {
            int top = ContentTop;
            int diameter = S(42);
            int circleX = (ClientSize.Width - diameter) / 2;
            int circleY = top + S(17);

            using (var circleBrush = new SolidBrush(Theme.AccentSoft))
            {
                graphics.FillEllipse(circleBrush, circleX, circleY, diameter, diameter);
            }

            using (var plusPen = new Pen(Theme.Accent, Math.Max(1.4f, SF(2f))))
            {
                plusPen.StartCap = LineCap.Round;
                plusPen.EndCap = LineCap.Round;
                int centerX = circleX + diameter / 2;
                int centerY = circleY + diameter / 2;
                int arm = S(6);
                graphics.DrawLine(plusPen, centerX - arm, centerY, centerX + arm, centerY);
                graphics.DrawLine(plusPen, centerX, centerY - arm, centerX, centerY + arm);
            }

            TextRenderer.DrawText(
                graphics,
                "还没有添加软件",
                _emptyTitleFont,
                new Rectangle(0, top + S(66), ClientSize.Width, S(24)),
                Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(
                graphics,
                "点击「添加软件」选择 .exe 程序",
                _emptySubtitleFont,
                new Rectangle(0, top + S(90), ClientSize.Width, S(20)),
                Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(
                graphics,
                "托盘若没有图标，请点 ^ 拖出来",
                _footerHintFont,
                new Rectangle(0, top + S(111), ClientSize.Width, S(17)),
                Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        private void DrawRow(Graphics graphics, int index, int top)
        {
            AppRowView row = _rows[index];
            bool hover = index == _hoverRow;

            if (hover)
            {
                Theme.FillRounded(
                    graphics,
                    Theme.RowHover,
                    new RectangleF(S(6), top + S(1), ClientSize.Width - S(12), S(56)),
                    S(9));
            }

            var iconRect = new Rectangle(S(16), top + S(13), S(32), S(32));
            if (row.Icon != null)
            {
                graphics.DrawImage(row.Icon, iconRect);
            }

            int toggleX = ToggleX;
            int textWidth = Math.Max(S(30), toggleX - S(58) - S(12));

            TextRenderer.DrawText(
                graphics,
                row.Name ?? string.Empty,
                _nameFont,
                new Rectangle(S(58), top + S(9), textWidth, S(22)),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            Color statusColor;
            switch (row.Tone)
            {
                case StatusTone.On:
                    statusColor = Theme.Accent;
                    break;
                case StatusTone.Warning:
                    statusColor = Theme.Warning;
                    break;
                case StatusTone.Error:
                    statusColor = Theme.Danger;
                    break;
                default:
                    statusColor = Theme.TextSecondary;
                    break;
            }

            TextRenderer.DrawText(
                graphics,
                row.Status ?? string.Empty,
                _statusFont,
                new Rectangle(S(58), top + S(31), textWidth, S(18)),
                statusColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            DrawToggle(graphics, index, row, top);
        }

        private void DrawToggle(Graphics graphics, int index, AppRowView row, int top)
        {
            var track = new RectangleF(ToggleX, top + S(17), S(44), S(24));
            bool hover = index == _hoverRow;
            float position = GetTogglePosition(row);

            if (!row.CanToggle)
            {
                Theme.FillRounded(graphics, Theme.DisabledTrack, track, track.Height / 2f);
                using (var border = new Pen(Theme.Border, 1f))
                {
                    Theme.DrawRounded(graphics, border, track, track.Height / 2f);
                }

                float disabledKnob = S(20);
                float disabledX = track.X + S(2) + (track.Width - disabledKnob - S(4)) * position;
                using (var knobBrush = new SolidBrush(Color.FromArgb(250, 250, 252)))
                {
                    graphics.FillEllipse(knobBrush, disabledX, track.Y + S(2), disabledKnob, disabledKnob);
                }

                return;
            }

            Color from = hover ? Theme.OffTrackHover : Theme.OffTrack;
            Color to = hover ? Theme.AccentDark : Theme.Accent;
            Color trackColor = Theme.Blend(from, to, position);
            Theme.FillRounded(graphics, trackColor, track, track.Height / 2f);

            using (var border = new Pen(Theme.Blend(Color.FromArgb(205, 211, 219), Theme.Accent, position), 1f))
            {
                Theme.DrawRounded(graphics, border, track, track.Height / 2f);
            }

            float knobSize = S(20);
            float travel = track.Width - knobSize - S(4);
            float knobX = track.X + S(2) + travel * position;
            float knobY = track.Y + S(2);

            using (var shadow = new SolidBrush(Color.FromArgb(34, 18, 24, 34)))
            {
                graphics.FillEllipse(shadow, knobX, knobY + S(1), knobSize, knobSize);
            }

            using (var knob = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(knob, knobX, knobY, knobSize, knobSize);
            }
        }

        private void DrawScrollbar(Graphics graphics)
        {
            if (_maxScroll <= 0)
            {
                return;
            }

            int trackX = ClientSize.Width - S(4);
            int trackY = ContentTop + S(8);
            int trackHeight = Math.Max(S(20), _contentHeight - S(16));
            int visibleRows = Math.Min(_rows.Count, MaxVisibleRows);
            int thumbHeight = Math.Max(
                S(24),
                (int)Math.Round((double)trackHeight * visibleRows / Math.Max(1, _rows.Count)));
            int thumbY = trackY + (int)Math.Round(
                (double)(trackHeight - thumbHeight) * _scrollOffset / Math.Max(1, _maxScroll));

            Theme.FillRounded(
                graphics,
                Color.FromArgb(235, 238, 242),
                new RectangleF(trackX, trackY, S(3), trackHeight),
                SF(1.5f));
            Theme.FillRounded(
                graphics,
                Color.FromArgb(196, 203, 213),
                new RectangleF(trackX, thumbY, S(3), thumbHeight),
                SF(1.5f));
        }

        private void DrawFooter(Graphics graphics)
        {
            int footerTop = ContentTop + _contentHeight;
            int footerHeight = FooterHeight;

            using (var background = new SolidBrush(_footerHover ? Theme.FooterHover : Theme.FooterBackground))
            {
                graphics.FillRectangle(background, 0, footerTop, ClientSize.Width, footerHeight);
            }

            using (var divider = new Pen(Theme.Divider, 1f))
            {
                graphics.DrawLine(divider, 0, footerTop, ClientSize.Width, footerTop);
            }

            int centerY = footerTop + footerHeight / 2;
            int plusCenterX = S(21);
            int arm = S(5);
            using (var plusPen = new Pen(_footerHover ? Theme.AccentDark : Theme.Accent, Math.Max(1.5f, SF(1.8f))))
            {
                plusPen.StartCap = LineCap.Round;
                plusPen.EndCap = LineCap.Round;
                graphics.DrawLine(plusPen, plusCenterX - arm, centerY, plusCenterX + arm, centerY);
                graphics.DrawLine(plusPen, plusCenterX, centerY - arm, plusCenterX, centerY + arm);
            }

            TextRenderer.DrawText(
                graphics,
                "添加软件",
                _footerFont,
                new Rectangle(S(33), footerTop + S(11), S(110), S(24)),
                _footerHover ? Theme.AccentDark : Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);

            if (_rows.Count > 0)
            {
                const string hint = "右键软件可移除";
                Size hintSize = TextRenderer.MeasureText(
                    hint,
                    _footerHintFont,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int hintX = ClientSize.Width - S(18) - hintSize.Width;
                if (hintX > S(140))
                {
                    TextRenderer.DrawText(
                        graphics,
                        hint,
                        _footerHintFont,
                        new Rectangle(hintX, footerTop + S(14), hintSize.Width, S(18)),
                        Theme.TextMuted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.SingleLine);
                }
            }
        }

        private void DrawBorder(Graphics graphics)
        {
            using (var border = new Pen(Theme.Border, 1f))
            {
                Theme.DrawRounded(
                    graphics,
                    border,
                    new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f),
                    S(14));
            }
        }

        private void OpenRowMenu(int rowIndex, Point point)
        {
            AppRowView row = _rows[rowIndex];
            _menuEntry = row.Entry;
            _openLocationItem.Tag = row.Entry;
            _removeItem.Tag = row.Entry;
            _menuOpen = true;
            _openLocationItem.Enabled = row.Entry != null && File.Exists(row.Entry.ExePath);
            _removeItem.Enabled = row.Entry != null;
            _rowMenu.Show(Cursor.Position);
        }

        private int HitTestRow(Point point)
        {
            if (_rows.Count == 0 ||
                point.Y < ContentTop ||
                point.Y >= ContentTop + _contentHeight)
            {
                return -1;
            }

            int localY = point.Y - ContentTop + _scrollOffset;
            int index = localY / RowHeight;
            if (index < 0 || index >= _rows.Count)
            {
                return -1;
            }

            int top = RowTop(index);
            if (point.Y < top || point.Y >= top + RowHeight)
            {
                return -1;
            }

            return index;
        }

        private Rectangle ToggleRectangle(int rowIndex)
        {
            return new Rectangle(ToggleX, RowTop(rowIndex) + S(17), S(44), S(24));
        }

        private int RowTop(int rowIndex)
        {
            return ContentTop + rowIndex * RowHeight - _scrollOffset;
        }

        private float GetTogglePosition(AppRowView row)
        {
            float position;
            if (!_togglePositions.TryGetValue(GetRowId(row), out position))
            {
                position = row.Enabled ? 1f : 0f;
            }

            return Math.Max(0f, Math.Min(1f, position));
        }

        private static string GetRowId(AppRowView row)
        {
            return row.Entry != null && !string.IsNullOrWhiteSpace(row.Entry.Id)
                ? row.Entry.Id
                : (row.Name ?? string.Empty);
        }

        private void SnapAnimations()
        {
            foreach (AppRowView row in _rows)
            {
                _togglePositions[GetRowId(row)] = row.Enabled ? 1f : 0f;
            }
        }

        private void AnimationTimerOnTick(object sender, EventArgs e)
        {
            bool changed = false;
            foreach (AppRowView row in _rows)
            {
                string id = GetRowId(row);
                float target = row.Enabled ? 1f : 0f;
                float current;
                if (!_togglePositions.TryGetValue(id, out current))
                {
                    current = target;
                }

                if (Math.Abs(current - target) > 0.002f)
                {
                    current += (target - current) * 0.32f;
                    if (Math.Abs(current - target) < 0.008f)
                    {
                        current = target;
                    }

                    _togglePositions[id] = current;
                    changed = true;
                }
                else if (current != target)
                {
                    _togglePositions[id] = target;
                    changed = true;
                }
            }

            if (changed)
            {
                Invalidate();
            }
            else
            {
                _animationTimer.Stop();
            }
        }

        private void FadeTimerOnTick(object sender, EventArgs e)
        {
            Opacity = Math.Min(1d, Opacity + 0.22d);
            if (Opacity >= 1d)
            {
                _fadeTimer.Stop();
            }
        }

        private void TopMostTimerOnTick(object sender, EventArgs e)
        {
            if (Visible && !_previewMode && !_menuOpen && IsHandleCreated)
            {
                NativeMethods.BringToTop(Handle);
            }
        }

        private void HideTimerOnTick(object sender, EventArgs e)
        {
            if (!Visible || _menuOpen || _previewMode)
            {
                return;
            }

            if (DateTime.UtcNow < _holdUntil)
            {
                _awayTicks = 0;
                return;
            }

            Point cursor = Cursor.Position;
            bool inside = Bounds.Contains(cursor);
            bool nearTray = Math.Abs(cursor.X - _trayAnchor.X) <= S(90) &&
                            Math.Abs(cursor.Y - _trayAnchor.Y) <= S(90);
            if (inside || nearTray)
            {
                _awayTicks = 0;
                return;
            }

            _awayTicks++;
            if (_awayTicks >= 5)
            {
                HidePopup();
            }
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            // Layout is scaled manually from the real window DPI.
        }

        private void ApplyLayout()
        {
            int contentHeight = _rows.Count == 0
                ? S(EmptyHeight96)
                : Math.Min(_rows.Count, MaxVisibleRows) * S(RowHeight96);
            int width = S(Width96);
            int height = S(HeaderHeight96) + contentHeight + S(FooterHeight96);

            _contentHeight = contentHeight;
            _maxScroll = Math.Max(0, _rows.Count * S(RowHeight96) - contentHeight);
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, _maxScroll));

            if (ClientSize.Width != width || ClientSize.Height != height)
            {
                int oldBottom = Bottom;
                ClientSize = new Size(width, height);
                if (Visible)
                {
                    Top = oldBottom - Height;
                }
            }

            UpdateWindowRegion();
            Invalidate();
        }

        private void UpdateWindowRegion()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            if (_dwmRounded)
            {
                Region previous = Region;
                Region = null;
                if (previous != null)
                {
                    previous.Dispose();
                }

                return;
            }

            using (GraphicsPath path = Theme.RoundedRect(
                new RectangleF(0, 0, Width, Height),
                S(14)))
            {
                Region region = new Region(path);
                Region previous = Region;
                Region = region;
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private float SF(float value)
        {
            return value * _scale;
        }

        private int HeaderHeight
        {
            get { return S(HeaderHeight96); }
        }

        private int RowHeight
        {
            get { return S(RowHeight96); }
        }

        private int FooterHeight
        {
            get { return S(FooterHeight96); }
        }

        private int ContentTop
        {
            get { return HeaderHeight; }
        }

        private int ToggleX
        {
            get { return ClientSize.Width - S(18) - S(44); }
        }

        private Rectangle ContentRectangle
        {
            get { return new Rectangle(0, ContentTop, ClientSize.Width, _contentHeight); }
        }

        private Rectangle FooterRectangle
        {
            get { return new Rectangle(0, ContentTop + _contentHeight, ClientSize.Width, FooterHeight); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hideTimer.Dispose();
                _animationTimer.Dispose();
                _fadeTimer.Dispose();
                _topMostTimer.Dispose();
                _toolTip.Dispose();
                _rowMenu.Dispose();
                _titleFont.Dispose();
                _subtitleFont.Dispose();
                _nameFont.Dispose();
                _statusFont.Dispose();
                _countFont.Dispose();
                _footerFont.Dispose();
                _footerHintFont.Dispose();
                _emptyTitleFont.Dispose();
                _emptySubtitleFont.Dispose();
                _menuFont.Dispose();
                _windowIcon.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class MenuColorTable : ProfessionalColorTable
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
