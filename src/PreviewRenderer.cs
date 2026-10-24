using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TrayStartup
{
    internal static class PreviewRenderer
    {
        public static void Render(string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string system = Environment.SystemDirectory;
            using (var icons = new IconCache())
            using (var popup = new PopupForm())
            {
                var rows = new List<AppRowView>
                {
                    CreateRow(icons, Path.Combine(system, "notepad.exe"), "记事本", true, true, "已开启 · 已接管", StatusTone.On),
                    CreateRow(icons, Path.Combine(system, "mspaint.exe"), "画图", false, true, "已关闭", StatusTone.Off),
                    CreateRow(icons, Path.Combine(system, "explorer.exe"), "文件资源管理器", true, true, "已开启", StatusTone.On),
                    CreateRow(icons, Path.Combine(system, "calc.exe"), "计算器", false, true, "已关闭", StatusTone.Off)
                };

                popup.SetRows(rows, false);
                popup.ShowForPreview(new Point(80, 80));
                Thread.Sleep(280);
                Application.DoEvents();

                using (Bitmap shot = Capture(popup))
                {
                    SaveComposite(shot, outputPath);
                }

                popup.Hide();
            }

            string emptyPath = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? ".",
                Path.GetFileNameWithoutExtension(outputPath) + "-empty" + Path.GetExtension(outputPath));
            using (var popup = new PopupForm())
            {
                popup.SetRows(new List<AppRowView>(), false);
                popup.ShowForPreview(new Point(80, 80));
                Thread.Sleep(220);
                Application.DoEvents();

                using (Bitmap shot = Capture(popup))
                {
                    SaveComposite(shot, emptyPath);
                }

                popup.Hide();
            }
        }

        private static AppRowView CreateRow(
            IconCache icons,
            string path,
            string name,
            bool enabled,
            bool canToggle,
            string status,
            StatusTone tone)
        {
            return new AppRowView
            {
                Entry = new AppEntry
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                    Name = name,
                    ExePath = path,
                    RegistryName = name
                },
                Name = name,
                Path = path,
                Enabled = enabled,
                CanToggle = canToggle,
                Status = status,
                Tone = tone,
                Icon = icons.Get(path, name, 64)
            };
        }

        private static Bitmap Capture(PopupForm popup)
        {
            var bitmap = new Bitmap(popup.Width, popup.Height);
            try
            {
                popup.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (!IsMostlyBlank(bitmap))
                {
                    return bitmap;
                }
            }
            catch
            {
                // Fall back to a screen capture below.
            }

            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(popup.Left, popup.Top, 0, 0, popup.Size);
                }

                return bitmap;
            }
            catch
            {
                return bitmap;
            }
        }

        private static bool IsMostlyBlank(Bitmap bitmap)
        {
            int samples = 0;
            int nonWhite = 0;
            for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 12))
            {
                for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 12))
                {
                    samples++;
                    Color color = bitmap.GetPixel(x, y);
                    if (color.R < 248 || color.G < 248 || color.B < 248)
                    {
                        nonWhite++;
                    }
                }
            }

            return samples == 0 || nonWhite < 4;
        }

        private static void SaveComposite(Bitmap shot, string outputPath)
        {
            int margin = 36;
            int shadowOffset = 8;
            int width = shot.Width + margin * 2;
            int height = shot.Height + margin * 2;
            using (var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;

                using (var background = new LinearGradientBrush(
                    new Rectangle(0, 0, width, height),
                    Color.FromArgb(235, 240, 247),
                    Color.FromArgb(249, 251, 253),
                    90f))
                {
                    graphics.FillRectangle(background, 0, 0, width, height);
                }

                for (int layer = 14; layer >= 1; layer--)
                {
                    int inflate = layer;
                    var shadowRect = new RectangleF(
                        margin - inflate,
                        margin + shadowOffset - inflate,
                        shot.Width + inflate * 2,
                        shot.Height + inflate * 2);
                    Theme.FillRounded(
                        graphics,
                        Color.FromArgb(5, 22, 42, 70),
                        shadowRect,
                        14 + inflate);
                }

                using (Bitmap rounded = RoundCorners(shot, 14))
                {
                    graphics.DrawImageUnscaled(rounded, margin, margin);
                }

                canvas.Save(outputPath, ImageFormat.Png);
            }
        }

        private static Bitmap RoundCorners(Bitmap source, float radius)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.Clear(Color.Transparent);

                using (GraphicsPath path = Theme.RoundedRect(
                    new RectangleF(0, 0, source.Width, source.Height),
                    radius))
                using (var brush = new TextureBrush(source))
                {
                    brush.WrapMode = WrapMode.TileFlipXY;
                    graphics.FillPath(brush, path);
                }
            }

            return result;
        }
    }
}
