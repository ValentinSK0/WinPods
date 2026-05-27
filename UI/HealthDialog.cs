using System.Diagnostics;

namespace WinPods;

public sealed class HealthDialog : Form
{
    private const string MagicAapDocsUrl = "https://help.magicpods.app/fun-magicaap-community/";
    private const string MagicAapInstallCommand = "irm \"https://magicpods.app/utils/magicaap-community-v1.ps1\" | iex";

    public HealthDialog(SystemHealthSnapshot snapshot, bool darkTheme)
    {
        var back = darkTheme ? Color.FromArgb(14, 14, 14) : Color.White;
        var panelBack = darkTheme ? Color.FromArgb(28, 28, 28) : Color.FromArgb(247, 248, 250);
        var text = darkTheme ? Color.White : Color.FromArgb(24, 28, 34);
        var muted = darkTheme ? Color.FromArgb(196, 202, 210) : Color.FromArgb(86, 96, 110);
        var border = darkTheme ? Color.FromArgb(70, 70, 70) : Color.FromArgb(214, 222, 232);
        var accent = StatusColor(snapshot.OverallStatus);

        Text = "WinPods Health";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = back;
        ClientSize = new Size(560, 460);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            BackColor = back,
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"System health: {snapshot.Summary}",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = accent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var itemsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = back,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };

        foreach (var item in snapshot.Items)
        {
            itemsPanel.Controls.Add(CreateItemPanel(item, panelBack, text, muted, border));
        }

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = muted,
            Text = "MagicAAP is needed for exact battery and AirPods listening mode control.",
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = back,
        };
        var closeButton = CreateButton("Close", darkTheme, primary: true);
        closeButton.DialogResult = DialogResult.OK;
        var docsButton = CreateButton("Open install docs", darkTheme, primary: false);
        docsButton.Click += (_, _) => OpenUrl(MagicAapDocsUrl);
        var copyButton = CreateButton("Copy install command", darkTheme, primary: false);
        copyButton.Click += (_, _) => Clipboard.SetText(MagicAapInstallCommand);

        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(docsButton);
        buttons.Controls.Add(copyButton);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(itemsPanel, 0, 1);
        root.Controls.Add(hint, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = closeButton;
    }

    private static Control CreateItemPanel(SystemHealthItem item, Color back, Color text, Color muted, Color border)
    {
        var panel = new ModernPanel
        {
            Width = 502,
            Height = 82,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12),
            BackColor = back,
            FillColor = back,
            FillColor2 = back,
            BorderColor = border,
            Radius = 18,
        };

        var name = new Label
        {
            AutoSize = false,
            Location = new Point(14, 10),
            Size = new Size(330, 22),
            Text = item.Name,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = text,
        };

        var status = new Label
        {
            AutoSize = false,
            Location = new Point(350, 10),
            Size = new Size(132, 22),
            Text = item.Summary,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = StatusColor(item.Status),
        };

        var detail = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Location = new Point(14, 36),
            Size = new Size(468, 34),
            Text = item.Detail,
            ForeColor = muted,
        };

        panel.Controls.Add(name);
        panel.Controls.Add(status);
        panel.Controls.Add(detail);
        return panel;
    }

    private static Button CreateButton(string text, bool darkTheme, bool primary)
    {
        var button = new Button
        {
            Width = text.Length > 12 ? 150 : 98,
            Height = 32,
            Text = text,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            BackColor = primary
                ? Color.FromArgb(10, 92, 190)
                : darkTheme ? Color.FromArgb(38, 38, 38) : Color.White,
            ForeColor = primary ? Color.White : darkTheme ? Color.White : Color.FromArgb(24, 28, 34),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(8, 6, 0, 0),
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Color StatusColor(SystemHealthStatus status) =>
        status switch
        {
            SystemHealthStatus.Ok => Color.FromArgb(36, 150, 92),
            SystemHealthStatus.Warning => Color.FromArgb(202, 133, 24),
            _ => Color.FromArgb(210, 70, 60),
        };

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
