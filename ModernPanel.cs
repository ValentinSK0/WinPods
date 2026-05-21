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
}
