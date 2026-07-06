using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TaskPrioMemory.UI
{
    /// <summary>
    /// Builds the tray/window icon at runtime so the app ships as a single .exe
    /// with no embedded binary asset. Cached so we only draw it once.
    /// </summary>
    public static class IconFactory
    {
        private static Icon _cached;

        public static Icon AppIcon => _cached ?? (_cached = Build());

        private static Icon Build()
        {
            try
            {
                using (var bmp = new Bitmap(32, 32))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    using (var bg = new LinearGradientBrush(
                        new Rectangle(0, 0, 32, 32),
                        Color.FromArgb(0x2D, 0x6C, 0xDF),
                        Color.FromArgb(0x1B, 0x3C, 0x8C),
                        45f))
                    {
                        g.FillRectangle(bg, 0, 0, 32, 32);
                    }

                    // A stylised "gauge" tick to hint at priority/speed.
                    using (var pen = new Pen(Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawArc(pen, 6, 8, 20, 20, 150, 240);
                        g.DrawLine(pen, 16, 22, 23, 12);
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    using (var tmp = Icon.FromHandle(hIcon))
                    {
                        // Clone so we can destroy the GDI handle safely.
                        var icon = (Icon)tmp.Clone();
                        NativeMethods.DestroyIcon(hIcon);
                        return icon;
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
