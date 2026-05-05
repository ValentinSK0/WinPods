using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace WinPods;

public sealed class AirPodsVisualPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Battery { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Connected { get; set; }

    public AirPodsVisualPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(244, 245, 245);
        Height = 118;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(249, 250, 250), Color.FromArgb(232, 233, 232), 35f);
        e.Graphics.FillRectangle(background, ClientRectangle);

        var centerX = Width / 2;
        DrawCase(e.Graphics, centerX - 94, 28);
        DrawBud(e.Graphics, centerX + 12, 28, -14);
        DrawBud(e.Graphics, centerX + 62, 28, 14);

        var dotColor = Connected ? Color.FromArgb(47, 230, 102) : Color.FromArgb(130, 136, 144);
        using var dotBrush = new SolidBrush(dotColor);
        e.Graphics.FillEllipse(dotBrush, centerX - 24, 96, 8, 8);

        using var textBrush = new SolidBrush(dotColor);
        using var font = new Font("Segoe UI", 9F, FontStyle.Bold);
        e.Graphics.DrawString($"{Battery}%", font, textBrush, centerX - 12, 90);
    }

    private static void DrawBud(Graphics g, int x, int y, float angle)
    {
        var state = g.Save();
        g.TranslateTransform(x + 17, y + 34);
        g.RotateTransform(angle);
        g.TranslateTransform(-(x + 17), -(y + 34));

        using var shadow = new SolidBrush(Color.FromArgb(50, 120, 120, 120));
        using var body = new SolidBrush(Color.FromArgb(245, 246, 247));
        using var shade = new SolidBrush(Color.FromArgb(185, 188, 190));
        g.FillEllipse(shadow, x + 12, y + 12, 36, 48);
        g.FillEllipse(body, x, y, 34, 46);
        g.FillRoundedRectangle(body, new Rectangle(x + 10, y + 36, 15, 48), 7);
        g.FillRoundedRectangle(Brushes.Black, new Rectangle(x + 10, y + 9, 15, 7), 4);
        g.FillEllipse(shade, x + 15, y + 20, 5, 8);
        using var cap = new SolidBrush(Color.FromArgb(210, 212, 214));
        g.FillEllipse(cap, x + 7, y + 42, 8, 6);
        g.Restore(state);
    }

    private static void DrawCase(Graphics g, int x, int y)
    {
        using var shadow = new SolidBrush(Color.FromArgb(40, 110, 110, 110));
        using var caseBrush = new SolidBrush(Color.FromArgb(250, 250, 249));
        using var linePen = new Pen(Color.FromArgb(214, 214, 212), 1);
        using var ledBrush = new SolidBrush(Color.FromArgb(185, 188, 185));
        var outer = new Rectangle(x, y, 92, 70);
        g.FillRoundedRectangle(shadow, new Rectangle(x + 8, y + 8, 92, 70), 18);
        g.FillRoundedRectangle(caseBrush, outer, 16);
        g.DrawLine(linePen, x + 8, y + 28, x + 84, y + 28);
        g.FillEllipse(ledBrush, x + 45, y + 40, 4, 4);
        g.DrawRoundedRectangle(linePen, outer, 16);
    }
}
