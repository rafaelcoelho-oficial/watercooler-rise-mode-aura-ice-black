using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AuraIce.App;

internal sealed class AuraIceTrayMenuRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color MenuBackground = Color.FromArgb(10, 22, 36);
    public static readonly Color MenuForeground = Color.FromArgb(235, 245, 252);
    private ToolStripItem? _hoveredItem;

    public AuraIceTrayMenuRenderer()
        : base(new AuraIceMenuColorTable())
    {
        RoundedEdges = false;
    }

    public void SetHoveredItem(ToolStripItem? item)
    {
        _hoveredItem = item is ToolStripMenuItem && item.Enabled ? item : null;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuBackground);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(42, 90, 136));
        Rectangle bounds = new(Point.Empty, e.ToolStrip.Size);
        bounds.Width--;
        bounds.Height--;
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || !IsHovered(e.Item))
        {
            return;
        }

        Rectangle bounds = new(
            3,
            e.Item.Bounds.Top + 1,
            Math.Max(0, (e.ToolStrip?.ClientSize.Width ?? 0) - 6),
            Math.Max(0, e.Item.Bounds.Height - 2));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var smoothingMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(bounds, 6);
        using var brush = new SolidBrush(Color.FromArgb(25, 58, 88));
        using var pen = new Pen(Color.FromArgb(57, 137, 190));
        pen.Alignment = PenAlignment.Inset;
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        e.Graphics.SmoothingMode = smoothingMode;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? IsHovered(e.Item) ? Color.FromArgb(245, 252, 255) : MenuForeground
            : Color.FromArgb(112, 137, 157);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Bounds.Top + e.Item.Bounds.Height / 2;
        using var pen = new Pen(Color.FromArgb(36, 67, 94));
        e.Graphics.DrawLine(pen, 10, y, Math.Max(10, (e.ToolStrip?.Width ?? 0) - 11), y);
    }

    private bool IsHovered(ToolStripItem item)
    {
        return ReferenceEquals(_hoveredItem, item)
            || (_hoveredItem is null && item.Selected);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
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
        public override Color MenuBorder => Color.FromArgb(42, 90, 136);
        public override Color MenuItemBorder => Color.FromArgb(57, 137, 190);
        public override Color MenuItemSelected => Color.FromArgb(25, 58, 88);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(25, 58, 88);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(18, 42, 66);
        public override Color SeparatorDark => Color.FromArgb(36, 67, 94);
        public override Color SeparatorLight => MenuBackground;
        public override Color ImageMarginGradientBegin => MenuBackground;
        public override Color ImageMarginGradientMiddle => MenuBackground;
        public override Color ImageMarginGradientEnd => MenuBackground;
    }
}
