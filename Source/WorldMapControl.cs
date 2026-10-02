using System.Drawing.Drawing2D;
using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed class WorldMapControl : Control
{
    public WorldDefinition? Map { get; private set; }
    public JsonObject? Character { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AreaMode { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FogPreview { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string SelectedQuest { get; set; } = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public WorldLabel? SelectedArea { get; set; }
    public event Action<WorldMarker>? MarkerSelected;
    public event Action<WorldLabel>? AreaSelected;
    float zoom = 1;
    PointF pan;
    Point dragOrigin;
    PointF panOrigin;
    bool dragged;
    readonly ToolTip tip = new();
    public WorldMapControl()
    {
        Dock = DockStyle.Fill; DoubleBuffered = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        tip.SetToolTip(this, "Click a quest or area • Mouse wheel to zoom • Drag to pan");
    }
    public void SetMap(WorldDefinition map, JsonObject? character)
    {
        bool changed = Map?.Id != map.Id; Map = map; Character = character;
        if (changed) { SelectedQuest = ""; SelectedArea = null; Fit(); }
        Invalidate();
    }
    public void Fit() { zoom = 1; pan = PointF.Empty; Invalidate(); }
    RectangleF ImageBounds
    {
        get
        {
            if (Map is null) return RectangleF.Empty;
            var image = WorldCatalog.MapImage(Map.Image);
            float scale = Math.Min(Math.Max(1, ClientSize.Width - 30) / (float)image.Width, Math.Max(1, ClientSize.Height - 30) / (float)image.Height) * zoom;
            return new((ClientSize.Width - image.Width * scale) / 2 + pan.X, (ClientSize.Height - image.Height * scale) / 2 + pan.Y, image.Width * scale, image.Height * scale);
        }
    }
    PointF ScreenPoint(PointF imagePoint)
    {
        var bounds = ImageBounds; var image = WorldCatalog.MapImage(Map!.Image);
        return new(bounds.X + imagePoint.X * bounds.Width / image.Width, bounds.Y + imagePoint.Y * bounds.Height / image.Height);
    }
    PointF NativePoint(Point screen)
    {
        var bounds = ImageBounds; var image = WorldCatalog.MapImage(Map!.Image);
        return new((float)(((screen.X - bounds.X) / bounds.Width * image.Width + Map!.CropX) * Map.Divisor),
            (float)(((screen.Y - bounds.Y) / bounds.Height * image.Height + Map.CropY) * Map.Divisor));
    }
    public WorldLabel? NearestArea(double nativeX, double nativeY) => Map?.Labels.MinBy(l => Math.Pow(l.X - nativeX, 2) + Math.Pow(l.Y - nativeY, 2));
    // The texture projection is calibrated against authored locations. The save-grid orientation
    // and 32 m cell pitch are inferred, so the UI explicitly labels local reveal as experimental.
    public PointF FogCellPoint(JsonObject fog, int index)
    {
        int width = fog["Size"]!["X"]!.GetValue<int>();
        double wx = (WorldEditing.Number(fog["WorldPosition"]!["X"]) + (index / width + .5) * 32) * 100;
        double wy = (WorldEditing.Number(fog["WorldPosition"]!["Y"]) + (index % width + .5) * 32) * 100;
        return Map!.WorldPoint(wx, wy);
    }
    public int[] AreaCells(WorldLabel area)
    {
        var fog = WorldEditing.Fog(Character, Map!.Tag); if (fog is null) return [];
        WorldEditing.ValidateFog(fog);
        return Enumerable.Range(0, fog["Data"]!.AsArray().Count).Where(i =>
        {
            var point = FogCellPoint(fog, i);
            return NearestArea((point.X + Map.CropX) * Map.Divisor, (point.Y + Map.CropY) * Map.Divisor)?.Name == area.Name;
        }).ToArray();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.Clear(Theme.Background);
        if (Map is null) return;
        var image = WorldCatalog.MapImage(Map.Image); var bounds = ImageBounds;
        g.InterpolationMode = zoom > 3 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, bounds);
        if (FogPreview && WorldEditing.Fog(Character, Map.Tag) is { } fog)
        {
            try
            {
                WorldEditing.ValidateFog(fog); var data = fog["Data"]!.AsArray();
                float cellSize = (float)(32 * 100 * Math.Abs(Map.ScaleX) / Map.Divisor * bounds.Width / image.Width);
                for (int i = 0; i < data.Count; i++)
                {
                    int alpha = (int)(160 * (1 - data[i]!.GetValue<int>() / 255.0)); if (alpha == 0) continue;
                    var center = ScreenPoint(FogCellPoint(fog, i));
                    if (!bounds.Contains(center)) continue;
                    using var brush = new SolidBrush(Color.FromArgb(alpha, Theme.Dark ? Color.FromArgb(18, 20, 29) : Color.FromArgb(224, 205, 167)));
                    g.FillRectangle(brush, center.X - cellSize / 2, center.Y - cellSize / 2, cellSize + 1, cellSize + 1);
                }
            }
            catch (InvalidDataException) { /* Missing/unknown grids never prevent quest browsing. */ }
        }
        foreach (var label in Map.Labels)
        {
            var p = ScreenPoint(Map.ImagePoint(label.X, label.Y));
            var color = SelectedArea?.Name == label.Name ? Color.FromArgb(255, 218, 119) : Color.White;
            using var font = new Font("Arial", 10, FontStyle.Bold);
            var box = new Rectangle((int)p.X - 105, (int)p.Y - 14, 210, 28);
            using var back = new SolidBrush(Color.FromArgb(175, 20, 23, 29)); g.FillRectangle(back, box);
            TextRenderer.DrawText(g, label.Name.ToUpperInvariant(), font, box, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        float iconSize = 32 * DeviceDpi / 96F;
        foreach (var marker in Map.Markers)
        {
            var p = ScreenPoint(Map.ImagePoint(marker.X, marker.Y));
            var rect = new RectangleF(p.X - iconSize / 2, p.Y - iconSize / 2, iconSize, iconSize);
            if (!ClientRectangle.IntersectsWith(Rectangle.Ceiling(rect))) continue;
            bool completed = marker.Quest.Length > 0 && WorldEditing.QuestState(Character, marker.Quest) == "Completed";
            g.DrawImage(WorldCatalog.MapImage(marker.Icon), rect);
            if (completed)
            {
                using var brush = new SolidBrush(Color.FromArgb(185, 22, 28, 35)); g.FillEllipse(brush, rect);
                TextRenderer.DrawText(g, "✓", Font, Rectangle.Ceiling(rect), Color.FromArgb(102, 224, 165), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (marker.Quest.Length > 0 && marker.Quest == SelectedQuest)
            { using var pen = new Pen(Theme.Accent, 3); g.DrawEllipse(pen, rect.X - 5, rect.Y - 5, rect.Width + 10, rect.Height + 10); }
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); Capture = true;
        dragOrigin = e.Location; panOrigin = pan; dragged = false;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!Capture || e.Button != MouseButtons.Left) return;
        if (Math.Abs(e.X - dragOrigin.X) + Math.Abs(e.Y - dragOrigin.Y) > 6) dragged = true;
        if (dragged) { pan = new(panOrigin.X + e.X - dragOrigin.X, panOrigin.Y + e.Y - dragOrigin.Y); Invalidate(); }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (!Capture) return; Capture = false;
        if (dragged || Map is null) return;
        if (!AreaMode)
        {
            var marker = Map.Markers.Where(m => m.Quest.Length > 0).MinBy(m =>
            { var p = ScreenPoint(Map.ImagePoint(m.X, m.Y)); return Math.Pow(p.X - e.X, 2) + Math.Pow(p.Y - e.Y, 2); });
            if (marker is not null)
            {
                var p = ScreenPoint(Map.ImagePoint(marker.X, marker.Y));
                if (Math.Sqrt(Math.Pow(p.X - e.X, 2) + Math.Pow(p.Y - e.Y, 2)) <= 26 * DeviceDpi / 96F)
                { SelectedQuest = marker.Quest; SelectedArea = null; MarkerSelected?.Invoke(marker); Invalidate(); return; }
            }
        }
        if (ImageBounds.Contains(e.Location))
        {
            var native = NativePoint(e.Location); var area = NearestArea(native.X, native.Y);
            if (area is not null) { SelectedArea = area; SelectedQuest = ""; AreaSelected?.Invoke(area); Invalidate(); }
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e); if (Map is null) return;
        var old = ImageBounds; float newZoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.2F : 1 / 1.2F), 1, 12);
        float ratio = newZoom / zoom; zoom = newZoom;
        pan = new(e.X - ClientSize.Width / 2 - (e.X - ClientSize.Width / 2 - pan.X) * ratio,
            e.Y - ClientSize.Height / 2 - (e.Y - ClientSize.Height / 2 - pan.Y) * ratio);
        if (zoom == 1) pan = PointF.Empty; Invalidate();
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
