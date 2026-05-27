using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WinPods;

public static class TrayIconFactory
{
    private const string TrayIconRelativePath = "Assets\\WinPodsTray.png";

    public static Icon Create(bool online)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        using var source = LoadTrayIconImage();
        if (source is not null)
        {
            DrawProductIcon(graphics, source, online);
        }
        else
        {
            DrawFallbackIcon(graphics, online);
        }

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

    private static Image? LoadTrayIconImage()
    {
        foreach (var path in EnumerateTrayIconPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTrayIconPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, TrayIconRelativePath);
        yield return Path.Combine(AppContext.BaseDirectory, "WinPodsTray.png");
        yield return Path.Combine(Environment.CurrentDirectory, TrayIconRelativePath);
    }

    private static void DrawProductIcon(Graphics graphics, Image source, bool online)
    {
        graphics.DrawImage(source, new Rectangle(0, 0, 32, 32));

        var dotColor = online ? Color.FromArgb(48, 209, 88) : Color.FromArgb(150, 156, 166);
        using var dotShadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        using var dotBrush = new SolidBrush(dotColor);
        graphics.FillEllipse(dotShadowBrush, 14.6f, 26.2f, 5.8f, 5.8f);
        graphics.FillEllipse(dotBrush, 14.4f, 26.0f, 5.2f, 5.2f);
    }

    private static void DrawFallbackIcon(Graphics graphics, bool online)
    {
        using var bgBrush = new LinearGradientBrush(
            new Rectangle(0, 0, 32, 32),
            Color.FromArgb(19, 24, 72),
            Color.FromArgb(18, 172, 184),
            135f);
        using var path = CreateRoundRectPath(new RectangleF(0.5f, 0.5f, 31f, 31f), 7f);
        graphics.FillPath(bgBrush, path);

        using var shellBrush = new SolidBrush(Color.White);
        graphics.FillEllipse(shellBrush, 8, 9, 7, 8);
        graphics.FillEllipse(shellBrush, 17, 7, 8, 9);
        using var stemPen = new Pen(Color.White, 4.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(stemPen, 10, 16, 8, 25);
        graphics.DrawLine(stemPen, 22, 15, 24, 24);

        var dotColor = online ? Color.FromArgb(48, 209, 88) : Color.FromArgb(150, 156, 166);
        using var dotBrush = new SolidBrush(dotColor);
        graphics.FillEllipse(dotBrush, 14, 26, 5, 5);
    }

    private static GraphicsPath CreateRoundRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);
}
