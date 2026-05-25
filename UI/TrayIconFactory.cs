using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WinPods;

public static class TrayIconFactory
{
    public static Icon Create(bool online)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var shell = online ? Color.White : Color.FromArgb(190, 194, 200);
        var accent = online ? Color.FromArgb(22, 116, 210) : Color.FromArgb(118, 126, 138);
        var shadow = Color.FromArgb(70, 0, 0, 0);

        DrawAirPods(graphics, 1, 1, shadow, Color.FromArgb(45, 0, 0, 0));
        DrawAirPods(graphics, 0, 0, shell, accent);

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

    private static void DrawAirPods(Graphics graphics, int offsetX, int offsetY, Color shell, Color accent)
    {
        using var shellBrush = new SolidBrush(shell);
        using var accentPen = new Pen(accent, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var stemPen = new Pen(shell, 4.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var detailPen = new Pen(Color.FromArgb(150, accent), 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        graphics.DrawLine(stemPen, 11 + offsetX, 15 + offsetY, 11 + offsetX, 27 + offsetY);
        graphics.DrawLine(stemPen, 21 + offsetX, 15 + offsetY, 21 + offsetX, 27 + offsetY);
        graphics.FillEllipse(shellBrush, 5 + offsetX, 5 + offsetY, 12, 13);
        graphics.FillEllipse(shellBrush, 15 + offsetX, 5 + offsetY, 12, 13);
        graphics.DrawArc(accentPen, 5 + offsetX, 5 + offsetY, 12, 13, 205, 118);
        graphics.DrawArc(accentPen, 15 + offsetX, 5 + offsetY, 12, 13, 217, 118);
        graphics.DrawLine(detailPen, 10 + offsetX, 15 + offsetY, 10 + offsetX, 24 + offsetY);
        graphics.DrawLine(detailPen, 22 + offsetX, 15 + offsetY, 22 + offsetX, 24 + offsetY);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);
}
