using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WinPods;

public static class TrayIconFactory
{
    public static Icon Create(int? battery, bool online)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.Transparent);

        var fill = !online ? Color.FromArgb(120, 120, 120) : BatteryColor(battery);
        using var outlinePen = new Pen(Color.FromArgb(32, 32, 32), 2);
        using var fillBrush = new SolidBrush(fill);
        using var textBrush = new SolidBrush(Color.White);

        var body = new Rectangle(3, 9, 24, 14);
        var cap = new Rectangle(27, 13, 3, 6);
        using var bodyPath = RoundedRectangle(body, 3);
        graphics.FillPath(fillBrush, bodyPath);
        graphics.DrawPath(outlinePen, bodyPath);
        graphics.FillRectangle(fillBrush, cap);

        var text = battery is null ? "?" : Math.Clamp(battery.Value / 10, 0, 9).ToString();
        using var font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Pixel);
        var textSize = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, textBrush, 15 - textSize.Width / 2, 10);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Color BatteryColor(int? battery) =>
        battery switch
        {
            null => Color.FromArgb(90, 100, 110),
            <= 20 => Color.FromArgb(210, 60, 50),
            <= 50 => Color.FromArgb(218, 150, 34),
            _ => Color.FromArgb(34, 150, 85),
        };

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);
}
