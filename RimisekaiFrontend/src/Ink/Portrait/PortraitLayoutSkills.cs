using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    public static readonly Rect2 SkillDiscPanel = new(40, 380, 1000, 1000);
    public static Rect2 SkillDiscClip => SkillDiscPanel.Grow(-18f);
    public const float SkillOverviewZoom = 1.16f;
    public const float SkillFocusedZoom = 3f;
    public static float SkillViewZoom(int sector) => sector < 0 ? SkillOverviewZoom : SkillFocusedZoom;
    public static float SkillViewRotation(int sector) => sector < 0 ? 0f : InkLayout.SkillDiscSectorRotation(sector);
    public static Vector2 SkillViewPivot(int sector) => sector < 0
        ? SkillDiscPanel.GetCenter() : new Vector2(SkillDiscClip.Position.X - 10f, SkillDiscClip.End.Y + 10f);
    public static readonly Rect2 SkillFocusedLabel = new(40, 1400, 460, TouchMin);
    public static readonly Rect2 SkillResetButton = new(550, 1400, 490, TouchMin);
    public static Rect2 SkillControls => SkillFocusedLabel.Merge(SkillResetButton);
    public static Rect2 SkillNavigation => new(SkillDiscClip.End.X - 320f, SkillDiscClip.Position.Y, 320f, 320f);

    public static Rect2 SkillSectorLabel(InkSkillDiscModel disc, int sector)
    {
        var center = InkLayout.TransformDiscRect(InkLayout.SkillDiscSectorLabelRect(sector),
            disc.ViewPivot, disc.ViewZoom, disc.ViewRotation).GetCenter();
        return new Rect2(center - new Vector2(70f, 32f), new Vector2(140f, 64f));
    }
    public static Rect2 SkillSectorLabelTarget(InkSkillDiscModel disc, int sector)
    {
        var center = SkillSectorLabel(disc, sector).GetCenter();
        return new Rect2(center - new Vector2(70f, TouchMin / 2f), new Vector2(140f, TouchMin)).Intersection(SkillDiscClip);
    }
    public static Rect2 SkillHubName(InkSkillDiscModel disc) => new(disc.ViewPivot.X - 110f, disc.ViewPivot.Y - 76f, 220f, 60f);
    public static Rect2 SkillHubStyleLine(InkSkillDiscModel disc, int index) =>
        new(disc.ViewPivot.X - 110f, disc.ViewPivot.Y - 10f + index * 54f, 220f, 54f);
    public static Vector2[] SkillSectorPolygon(InkSkillDiscModel disc, int sector) =>
        SkillProjectedPolygon(disc, InkLayout.SkillDiscSectorWedgePolygon(sector));
    public static Vector2[] SkillNodePolygon(InkSkillDiscModel disc, InkSkillTile tile) =>
        SkillProjectedPolygon(disc, InkLayout.SkillDiscTilePolygon(tile.Sector, tile.Ring, tile.Col, tile.Upper));
    private static Vector2[] SkillProjectedPolygon(InkSkillDiscModel disc, Vector2[] polygon) =>
        InkDraw.ClipPolygon(InkLayout.TransformDiscPolygon(polygon, disc.ViewPivot, disc.ViewZoom, disc.ViewRotation), SkillDiscClip);
    public static Vector2[] SkillNavigationPolygon(bool previous) => InkDraw.ClipPolygon(previous
        ? InkLayout.SkillDiscCornerNavTriangleUpper(SkillNavigation)
        : InkLayout.SkillDiscCornerNavTriangleLower(SkillNavigation), SkillDiscClip);
    public static Rect2 SkillPolygonBounds(Vector2[] polygon) => InkLayout.PolygonBounds(polygon).Grow(-2f);
    public static bool SkillTouchTarget(Rect2 rect) => Mathf.Min(rect.Size.X, rect.Size.Y) >= TouchMin;

    public static readonly Rect2 SkillDetailArea = new(40, 1550, 1000, 510);
    public const int SkillDetailLineHeight = 80;
    public const float SkillDragThreshold = 24f;
    public static int SkillDetailVisibleRows => (int)(SkillDetailArea.Size.Y / SkillDetailLineHeight);
    public static Rect2 SkillDetailText => new(SkillDetailArea.Position,
        new Vector2(SkillDetailArea.Size.X - TouchMin - 24f, SkillDetailArea.Size.Y));
    public static Rect2 SkillDetailRow(int index) => new(SkillDetailText.Position.X,
        SkillDetailText.Position.Y + index * SkillDetailLineHeight, SkillDetailText.Size.X, SkillDetailLineHeight);
    public static float SkillDetailRuleY(int index) => SkillDetailRow(index).End.Y - LineHair;
    public static Rect2 SkillDetailScroll => new(SkillDetailArea.End.X - TouchMin, SkillDetailArea.Position.Y, TouchMin, SkillDetailArea.Size.Y);
    public static Vector2 SkillDetailTrackTop => new(SkillDetailScroll.GetCenter().X, SkillDetailScroll.Position.Y);
    public static Vector2 SkillDetailTrackBottom => new(SkillDetailScroll.GetCenter().X, SkillDetailScroll.End.Y);
    public static Rect2 SkillDetailThumb(int first, int total)
    {
        var bar = SkillDetailScroll;
        var height = Mathf.Max(TouchMin, bar.Size.Y * SkillDetailVisibleRows / total);
        var y = bar.Position.Y + (bar.Size.Y - height) * first / (total - SkillDetailVisibleRows);
        return new Rect2(bar.GetCenter().X - LineBold, y, LineBold * 2f, height);
    }
}
