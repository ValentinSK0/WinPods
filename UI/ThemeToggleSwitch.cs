namespace WinPods;

public sealed class ThemeToggleSwitch : CheckBox
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color TrackOn { get; set; } = Color.FromArgb(20, 20, 20);

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color TrackOff { get; set; } = Color.White;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color KnobOn { get; set; } = Color.White;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color KnobOff { get; set; } = Color.FromArgb(20, 20, 20);

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(80, 88, 98);

    public ThemeToggleSwitch()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        Size = new Size(54, 28);
        Text = string.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

        var track = new Rectangle(1, 3, Width - 3, Height - 7);
        var knobSize = Height - 12;
        var knobX = Checked ? Width - knobSize - 8 : 7;
        var knob = new Rectangle(knobX, 6, knobSize, knobSize);

        using var trackBrush = new SolidBrush(Checked ? TrackOn : TrackOff);
        using var knobBrush = new SolidBrush(Checked ? KnobOn : KnobOff);
        using var borderPen = new Pen(BorderColor, 1.5f);

        e.Graphics.FillRoundedRectangle(trackBrush, track, track.Height / 2);
        e.Graphics.DrawRoundedRectangle(borderPen, track, track.Height / 2);
        e.Graphics.FillEllipse(knobBrush, knob);
    }
}
