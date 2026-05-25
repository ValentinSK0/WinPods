using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WinPods;

public sealed class AdaptiveModeButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool UseAdaptiveGradient { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(205, 213, 224);

    public AdaptiveModeButton()
    {
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!UseAdaptiveGradient)
        {
            base.OnPaint(e);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectangle(bounds, Math.Min(14, Height / 2));
        using var brush = new LinearGradientBrush(bounds, Color.Red, Color.Blue, 0f);
        var blend = new ColorBlend
        {
            Positions = [0f, 0.16f, 0.31f, 0.46f, 0.62f, 0.78f, 1f],
            Colors =
            [
                Color.FromArgb(255, 24, 0),
                Color.FromArgb(255, 122, 0),
                Color.FromArgb(255, 238, 0),
                Color.FromArgb(66, 220, 0),
                Color.FromArgb(0, 175, 255),
                Color.FromArgb(0, 92, 255),
                Color.FromArgb(142, 0, 255),
            ],
        };
        brush.InterpolationColors = blend;

        e.Graphics.FillPath(brush, path);
        using var border = new Pen(Color.FromArgb(210, 255, 255, 255), 1f);
        e.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            bounds,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
