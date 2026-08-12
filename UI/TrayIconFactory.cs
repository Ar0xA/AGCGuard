using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace HamstuffAgcGuard.UI
{
    /// <summary>Draws a small tray icon at runtime so the project needs no binary .ico asset.</summary>
    internal static class TrayIconFactory
    {
        public static Icon CreateIcon()
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var backgroundBrush = new SolidBrush(Color.FromArgb(255, 30, 90, 160));
                g.FillEllipse(backgroundBrush, 1, 1, 30, 30);

                using var pen = new Pen(Color.White, 2.5f);
                g.DrawArc(pen, 6, 8, 20, 20, 200, 140);
                g.DrawArc(pen, 10, 12, 12, 12, 200, 140);

                using var dotBrush = new SolidBrush(Color.White);
                g.FillEllipse(dotBrush, 14, 17, 4, 4);
            }

            IntPtr hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }
    }
}
