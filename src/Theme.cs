using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TrayStartup
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(255, 255, 255);
        public static readonly Color HeaderBackground = Color.FromArgb(250, 251, 253);
        public static readonly Color FooterBackground = Color.FromArgb(252, 252, 254);
        public static readonly Color Text = Color.FromArgb(31, 35, 41);
        public static readonly Color TextSecondary = Color.FromArgb(122, 130, 141);
        public static readonly Color TextMuted = Color.FromArgb(154, 162, 173);
        public static readonly Color Accent = Color.FromArgb(15, 108, 189);
        public static readonly Color AccentDark = Color.FromArgb(10, 88, 158);
        public static readonly Color AccentSoft = Color.FromArgb(237, 244, 253);
        public static readonly Color Border = Color.FromArgb(226, 229, 234);
        public static readonly Color Divider = Color.FromArgb(236, 238, 242);
        public static readonly Color RowHover = Color.FromArgb(245, 247, 250);
        public static readonly Color FooterHover = Color.FromArgb(246, 249, 253);
        public static readonly Color Pill = Color.FromArgb(241, 244, 248);
        public static readonly Color OffTrack = Color.FromArgb(216, 221, 228);
        public static readonly Color OffTrackHover = Color.FromArgb(203, 209, 217);
        public static readonly Color DisabledTrack = Color.FromArgb(231, 234, 238);
        public static readonly Color Warning = Color.FromArgb(180, 95, 6);
        public static readonly Color WarningSoft = Color.FromArgb(255, 247, 235);
        public static readonly Color Danger = Color.FromArgb(190, 48, 48);

        public static string FontFamilyName { get; } = ChooseFontFamily();

        private static string ChooseFontFamily()
        {
            string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            foreach (string candidate in candidates)
            {
                using (var test = new Font(candidate, 9f, FontStyle.Regular, GraphicsUnit.Point))
                {
                    if (string.Equals(test.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            return "Segoe UI";
        }

        public static Font CreateFont(float points, FontStyle style = FontStyle.Regular)
        {
            return new Font(FontFamilyName, points, style, GraphicsUnit.Point);
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            if (amount <= 0f) return from;
            if (amount >= 1f) return to;
            int alpha = (int)Math.Round(from.A + (to.A - from.A) * amount);
            int red = (int)Math.Round(from.R + (to.R - from.R) * amount);
            int green = (int)Math.Round(from.G + (to.G - from.G) * amount);
            int blue = (int)Math.Round(from.B + (to.B - from.B) * amount);
            return Color.FromArgb(alpha, red, green, blue);
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return path;
            }

            float diameter = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2f);
            if (diameter <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }

            float arcX = rect.X;
            float arcY = rect.Y;
            float arcW = rect.Width;
            float arcH = rect.Height;

            path.AddArc(arcX, arcY, diameter, diameter, 180f, 90f);
            path.AddArc(arcX + arcW - diameter, arcY, diameter, diameter, 270f, 90f);
            path.AddArc(arcX + arcW - diameter, arcY + arcH - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(arcX, arcY + arcH - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics graphics, Brush brush, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void FillRounded(Graphics graphics, Color color, RectangleF rect, float radius)
        {
            using (var brush = new SolidBrush(color))
            {
                FillRounded(graphics, brush, rect, radius);
            }
        }

        public static void DrawRounded(Graphics graphics, Pen pen, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            {
                graphics.DrawPath(pen, path);
            }
        }
    }
}
