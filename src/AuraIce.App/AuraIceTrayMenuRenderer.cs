using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AuraIce.App;

internal sealed class AuraIceTrayMenuRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color MenuBackground = Color.FromArgb(10, 22, 36);
    public static readonly Color MenuForeground = Color.FromArgb(235, 245, 252);
    private static readonly Color MenuBorder = Color.FromArgb(42, 90, 136);
    private static readonly Color ItemBackground = Color.FromArgb(25, 58, 88);
    private static readonly Color ItemBorder = Color.FromArgb(57, 137, 190);
    private static readonly Color PressedBackground = Color.FromArgb(18, 42, 66);
    private static readonly Color SeparatorColor = Color.FromArgb(36, 67, 94);
    private static readonly Color ActiveForeground = Color.FromArgb(245, 252, 255);
    private static readonly Color DisabledForeground = Color.FromArgb(112, 137, 157);

    private const float LogicalBorderWidth = 1F;
    internal const float LogicalMenuCornerRadius = 14F;
    private const float LogicalSelectionInset = 1F;
    private const float LogicalCornerRadius = 6F;
    private const float LogicalSeparatorInset = 8F;
    private ToolStripMenuItem? _pointerPressedItem;

    public AuraIceTrayMenuRenderer()
        : base(new AuraIceMenuColorTable())
    {
        RoundedEdges = false;
    }

    public void SetPointerPressedItem(ToolStripMenuItem? item)
    {
        if (ReferenceEquals(_pointerPressedItem, item))
        {
            return;
        }

        ToolStripMenuItem? previous = _pointerPressedItem;
        _pointerPressedItem = item;
        previous?.Invalidate();
        item?.Invalidate();
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuBackground);
        e.Graphics.FillRectangle(brush, e.ToolStrip.ClientRectangle);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        Rectangle bounds = e.ToolStrip.ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        float scale = DpiScale(e.ToolStrip);
        float borderWidth = LogicalBorderWidth * scale;
        float inset = borderWidth / 2F;
        float radius = Math.Max(0, LogicalMenuCornerRadius * scale - inset);
        var borderBounds = new RectangleF(
            bounds.X + inset,
            bounds.Y + inset,
            bounds.Width - borderWidth,
            bounds.Height - borderWidth);
        radius = Math.Min(radius, Math.Min(borderBounds.Width, borderBounds.Height) / 2F);
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var path = RoundedRectangle(borderBounds, radius);
            using var pen = new Pen(MenuBorder, borderWidth);
            e.Graphics.DrawPath(pen, path);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem item || !item.Enabled ||
            (!item.Selected && !item.Pressed && !ReferenceEquals(item, _pointerPressedItem)))
        {
            return;
        }

        float scale = DpiScale(e.ToolStrip);
        float borderWidth = LogicalBorderWidth * scale;
        // ToolStrip paints each item with a graphics origin at the item itself.
        // Using item.Bounds would offset the outline a second time and clip its corners.
        float inset = (LogicalSelectionInset + LogicalBorderWidth / 2F) * scale;
        var bounds = new RectangleF(
            inset,
            inset,
            item.Width - inset * 2F,
            item.Height - inset * 2F);
        if (bounds.Width <= borderWidth * 2F || bounds.Height <= borderWidth * 2F)
        {
            return;
        }

        Color background = item.Pressed || ReferenceEquals(item, _pointerPressedItem)
            ? PressedBackground
            : ItemBackground;
        float radius = Math.Min(LogicalCornerRadius * scale, Math.Min(bounds.Width, bounds.Height) / 2F);
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var path = RoundedRectangle(bounds, radius);
            using var brush = new SolidBrush(background);
            using var pen = new Pen(ItemBorder, borderWidth);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled
            ? DisabledForeground
            : e.Item.Selected || e.Item.Pressed ? ActiveForeground : MenuForeground;

        Rectangle textBounds = e.TextRectangle;
        textBounds.Y = (e.Item.Height - textBounds.Height) / 2;
        e.TextRectangle = textBounds;

        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        Rectangle bounds = new(Point.Empty, e.Item.Size);
        float scale = DpiScale(e.ToolStrip);
        int height = Math.Max(1, (int)Math.Round(scale));
        int inset = (int)Math.Round(LogicalSeparatorInset * scale);
        int y = bounds.Top + (bounds.Height - height) / 2;
        int width = bounds.Width - inset * 2;
        if (width <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(SeparatorColor);
        e.Graphics.FillRectangle(brush, inset, y, width, height);
    }

    private static float DpiScale(ToolStrip? toolStrip)
    {
        return (toolStrip?.DeviceDpi ?? 96) / 96F;
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2F;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class AuraIceMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => MenuBackground;
        public override Color MenuBorder => AuraIceTrayMenuRenderer.MenuBorder;
        public override Color MenuItemBorder => ItemBorder;
        public override Color MenuItemSelected => ItemBackground;
        public override Color MenuItemSelectedGradientBegin => ItemBackground;
        public override Color MenuItemSelectedGradientEnd => PressedBackground;
        public override Color SeparatorDark => SeparatorColor;
        public override Color SeparatorLight => MenuBackground;
        public override Color ImageMarginGradientBegin => MenuBackground;
        public override Color ImageMarginGradientMiddle => MenuBackground;
        public override Color ImageMarginGradientEnd => MenuBackground;
    }
}
