using System.Drawing.Drawing2D;

namespace MCD2SaveEdit;

public sealed class ModernButton : Button
{
    bool hover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Navigation { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Background);
        var bounds = new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        float radius = Math.Min(8 * DeviceDpi / 96F, bounds.Height / 2);
        using var shape = new GraphicsPath();
        shape.AddArc(bounds.X, bounds.Y, radius * 2, radius * 2, 180, 90);
        shape.AddArc(bounds.Right - radius * 2, bounds.Y, radius * 2, radius * 2, 270, 90);
        shape.AddArc(bounds.Right - radius * 2, bounds.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        shape.AddArc(bounds.X, bounds.Bottom - radius * 2, radius * 2, radius * 2, 90, 90); shape.CloseFigure();
        bool accented = Enabled && (Primary || Selected);
        var fillColor = !Enabled ? Theme.Surface : accented ? (hover ? Color.FromArgb(130, 87, 224) : Theme.Accent) : hover ? Theme.Raised : Navigation ? Theme.Background : Theme.Surface;
        using var fill = new SolidBrush(fillColor); g.FillPath(fill, shape);
        if (!Navigation && !accented) { using var pen = new Pen(Theme.Border); g.DrawPath(pen, shape); }
        var rect = Rectangle.Inflate(ClientRectangle, -8, -3);
        TextRenderer.DrawText(g, Text, Font, rect, !Enabled ? Theme.Muted : accented ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (Focused) { using var outline = new Pen(Theme.Accent, 2); g.DrawPath(outline, shape); }
    }
}

public sealed partial class MainForm
{
    readonly FlowLayoutPanel navigationBar = new() { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 4), Margin = Padding.Empty };
    readonly Panel pageViewport = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly Dictionary<TabPage, ModernButton> navigationButtons = new();
    Control BuildBrandHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4, 8, 0, 0), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = AppBrand.Icon.ToBitmap(), Padding = new Padding(5) }, 0, 0);
        var words = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        words.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); words.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        words.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "MCD2 Save Editor", Font = new Font("Arial", 21, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        words.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Character tools  •  Inventory, enchantments and world progress", Tag = "muted", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        header.Controls.Add(words, 1, 0); return header;
    }
    void BuildNavigationHost(TableLayoutPanel parent)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.Controls.Add(navigationBar, 0, 0); host.Controls.Add(pageViewport, 0, 1); parent.Controls.Add(host, 0, 3);
        tabs.Dock = DockStyle.None; pageViewport.Controls.Add(tabs);
        pageViewport.SizeChanged += (_, _) => FitPageViewport();
        tabs.SelectedIndexChanged += (_, _) =>
        {
            foreach (var pair in navigationButtons) { pair.Value.Selected = pair.Key == tabs.SelectedTab; pair.Value.Invalidate(); }
        };
    }
    void FitPageViewport()
    {
        if (pageViewport.ClientSize.Width <= 0) return;
        int header = Math.Max(24, tabs.DisplayRectangle.Top);
        tabs.SetBounds(-4, -header, pageViewport.ClientSize.Width + 8, pageViewport.ClientSize.Height + header + 4);
    }
    void AddNavigation(TabPage page)
    {
        var button = new ModernButton { Text = page.Text, Navigation = true, AutoSize = true, Padding = new Padding(17, 7, 17, 7), Margin = new Padding(0, 0, 7, 0), Height = 40, Selected = tabs.SelectedTab == page };
        button.Click += (_, _) => tabs.SelectedTab = page;
        navigationBar.Controls.Add(button); navigationButtons.Add(page, button); FitPageViewport();
    }
}
