using System.Runtime.InteropServices;

namespace MCD2SaveEdit;

public static class Theme
{
    public static bool Dark = true;
    public static Color Background => Dark ? Color.FromArgb(24, 26, 31) : Color.White;
    public static Color Surface => Dark ? Color.FromArgb(35, 38, 45) : Color.FromArgb(246, 246, 248);
    public static Color Raised => Dark ? Color.FromArgb(45, 49, 58) : Color.FromArgb(235, 235, 239);
    public static Color Text => Dark ? Color.FromArgb(235, 236, 242) : Color.FromArgb(28, 29, 35);
    public static Color Muted => Dark ? Color.FromArgb(165, 172, 190) : Color.FromArgb(99, 104, 119);
    public static Color Border => Dark ? Color.FromArgb(70, 75, 87) : Color.FromArgb(184, 188, 198);
    public static Color Accent => Color.FromArgb(155, 111, 243);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void Apply(Control root)
    {
        root.BackColor = root is TextBoxBase or ListBox or ListView or NumericUpDown or ComboBox ? Surface : Background;
        root.ForeColor = root.Tag as string == "muted" ? Muted : Text;
        if (root is Label label) label.UseMnemonic = false;
        if (root is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            if (combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.DrawMode != DrawMode.OwnerDrawFixed)
            {
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem += (_, e) =>
                {
                    bool selected = (e.State & DrawItemState.Selected) != 0;
                    using var background = new SolidBrush(selected ? Raised : Surface); e.Graphics.FillRectangle(background, e.Bounds);
                    string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) ?? "" : combo.Text;
                    TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -4, 0), Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                };
            }
        }
        if (root is Button b) { b.BackColor = Raised; b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 55, 82); b.UseVisualStyleBackColor = false; }
        if (root is TabPage tp) tp.UseVisualStyleBackColor = false;
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Surface; grid.EnableHeadersVisualStyles = false;
            grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Raised; grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Raised; grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        }
        if (root is MenuStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            void ColorItems(ToolStripItemCollection items) { foreach (ToolStripItem item in items) { item.ForeColor = Text; if (item is ToolStripDropDownItem drop) ColorItems(drop.DropDownItems); } }
            ColorItems(menu.Items);
        }
        foreach (Control child in root.Controls) Apply(child);
        if (root is Form form && form.IsHandleCreated) { int dark = Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); }
        root.Invalidate();
    }
    sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Raised;
        public override Color MenuItemSelectedGradientBegin => Raised;
        public override Color MenuItemSelectedGradientEnd => Raised;
        public override Color MenuItemBorder => Border;
    }
}
