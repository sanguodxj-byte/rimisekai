using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    public const float ListDragThreshold = 24f;

    public static Rect2 ListArea => new(Pad, Content.Position.Y + 24f,
        CanvasWidth - Pad * 2f, VisibleRows * RowHeight);
    public static Rect2 PageListArea(int rows) => new(Pad, OverlayRowsTop,
        CanvasWidth - Pad * 2f, rows * RowHeight);
    public static Rect2 ListTrack(Rect2 area) => new(area.End.X - TouchMin,
        area.Position.Y, TouchMin, area.Size.Y);
    public static Rect2 ScrolledRow(Rect2 area, int index, float height = RowHeight, bool hasScroll = false) =>
        new(area.Position.X, area.Position.Y + index * height,
            hasScroll ? area.Size.X - TouchMin - 16f : area.Size.X, height);
    public static Rect2 ListThumb(Rect2 track, int total, int visible, int first)
    {
        var height = Mathf.Max(TouchMin, track.Size.Y * visible / total);
        var travel = track.Size.Y - height;
        var y = track.Position.Y + travel * first / (total - visible);
        return new Rect2(track.GetCenter().X - LineBold, y, LineBold * 2f, height);
    }

    public static int FixtureVisibleRows => (int)((FixtureArea.Size.Y - 40f) / RowHeight);
    public static Rect2 FixtureList => new(Pad, FixtureArea.Position.Y + 24f,
        CanvasWidth - Pad * 2f, FixtureVisibleRows * RowHeight);
    public static Rect2 FixtureListRow(int index) => ScrolledRow(FixtureList, index);

    public static Rect2 SystemPageButton(int index) => new(Pad,
        OverlayRowsTop + (index + 2) * RowHeight, FullWidth, RowHeight);
    public const float StorageItemHeight = 148f;
    public static int StorageItemRows => (int)((TabBar.Position.Y - OverlayRowsTop) / StorageItemHeight);
    public static Rect2 StorageList => new(Pad, OverlayRowsTop, CanvasWidth - Pad * 2f, StorageItemRows * StorageItemHeight);
    public static Rect2 StorageItemName(float y, bool hasScroll) => new(Pad, y,
        CanvasWidth - Pad * 2f - TouchMin * 2f - 28f - (hasScroll ? TouchMin + 16f : 0f), StorageItemHeight);
    public static Rect2 StorageItemButton(float y, int index, bool hasScroll) => new(
        CanvasWidth - Pad - (2 - index) * TouchMin - (1 - index) * 12f - (hasScroll ? TouchMin + 16f : 0f),
        y, TouchMin, StorageItemHeight);

    public static int TradeVisibleRows => (OverlayRows - 2) / 2;
    public static Rect2 TradeHeldArea => new(Pad, OverlayRowsTop + 60f,
        CanvasWidth - Pad * 2f, TradeVisibleRows * RowHeight);
    public static Rect2 TradeMarketArea => new(Pad, TradeHeldArea.End.Y + 60f,
        CanvasWidth - Pad * 2f, TradeVisibleRows * RowHeight);
}
