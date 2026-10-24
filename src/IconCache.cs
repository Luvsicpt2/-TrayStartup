using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace TrayStartup
{
    internal sealed class IconCache : IDisposable
    {
        private readonly Dictionary<string, Image> _cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public Image Get(string exePath, string appName, int size = 64)
        {
            string key = BuildKey(exePath, size);
            Image cached;
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            Image image = LoadImage(exePath, appName, size);
            _cache[key] = image;
            return image;
        }

        private static string BuildKey(string exePath, int size)
        {
            long ticks = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    ticks = File.GetLastWriteTimeUtc(exePath).Ticks;
                }
            }
            catch
            {
                // Ignore metadata failures.
            }

            return (exePath ?? string.Empty) + "|" + ticks + "|" + size;
        }

        private static Image LoadImage(string exePath, string appName, int size)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    using (Icon icon = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                        {
                            using (Bitmap bitmap = icon.ToBitmap())
                            {
                                return Resize(bitmap, size);
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Failed to extract icon for " + exePath, exception);
            }

            return CreateFallback(appName, size);
        }

        private static Image Resize(Bitmap source, int size)
        {
            var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            target.SetResolution(96f, 96f);
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                int x = (size - width) / 2;
                int y = (size - height) / 2;

                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(
                        source,
                        new Rectangle(x, y, width, height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
            }

            return target;
        }

        private static Image CreateFallback(string appName, int size)
        {
            var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            target.SetResolution(96f, 96f);

            string text = "?";
            if (!string.IsNullOrWhiteSpace(appName))
            {
                string trimmed = appName.Trim();
                if (trimmed.Length > 0)
                {
                    text = trimmed.Substring(0, 1).ToUpperInvariant();
                }
            }

            Color[] palette =
            {
                Color.FromArgb(15, 108, 189),
                Color.FromArgb(45, 125, 70),
                Color.FromArgb(153, 84, 30),
                Color.FromArgb(108, 76, 168),
                Color.FromArgb(176, 63, 92),
                Color.FromArgb(30, 120, 128)
            };

            int hash = 0;
            foreach (char character in appName ?? string.Empty)
            {
                hash = unchecked(hash * 31 + character);
            }

            Color color = palette[Math.Abs(hash) % palette.Length];
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);

                var rectangle = new RectangleF(1f, 1f, size - 2f, size - 2f);
                using (GraphicsPath path = Theme.RoundedRect(rectangle, size * 0.23f))
                using (var brush = new LinearGradientBrush(
                    rectangle,
                    Theme.Blend(color, Color.White, 0.12f),
                    Theme.Blend(color, Color.Black, 0.14f),
                    90f))
                {
                    graphics.FillPath(brush, path);
                }

                using (var font = new Font(Theme.FontFamilyName, size * 0.40f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.White))
                using (var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    graphics.DrawString(text, font, brush, new RectangleF(0, 1, size, size), format);
                }
            }

            return target;
        }

        public void Dispose()
        {
            foreach (Image image in _cache.Values)
            {
                image.Dispose();
            }

            _cache.Clear();
        }
    }
}
