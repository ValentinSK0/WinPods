using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WinPods;

public sealed class ModernPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 18;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor2 { get; set; } = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(220, 226, 236);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DrawBorder { get; set; } = true;

    public ModernPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var fill = new LinearGradientBrush(bounds, FillColor, FillColor2, 90f);
        e.Graphics.FillRoundedRectangle(fill, bounds, Radius);

        if (DrawBorder)
        {
            using var border = new Pen(BorderColor, 1f);
            e.Graphics.DrawRoundedRectangle(border, bounds, Radius);
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRegion();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRegion();
    }

    private void ApplyRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectanglePath(new Rectangle(0, 0, Width, Height), Radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
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
