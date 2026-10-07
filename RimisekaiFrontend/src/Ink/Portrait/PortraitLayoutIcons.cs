using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    public static Rect2 RowIcon(Rect2 row) => new(row.Position.X + 24, row.GetCenter().Y - 40, 80, 80);
    public static Rect2 RowText(Rect2 row, bool icon) => new(row.Position.X + (icon ? 120 : 34), row.Position.Y + 14,
        row.Size.X - (icon ? 154 : 68), row.Size.Y - 28);
    public static Rect2 RoomMarkers(Rect2 cell) => new(cell.Position.X + 16, cell.Position.Y + 84, cell.Size.X - 32, cell.Size.Y - 108);
    public static Rect2 FixtureMarkers(Rect2 row) => new(row.Position.X + row.Size.X * 0.46f,
        row.Position.Y + 22, row.Size.X * 0.31f, row.Size.Y - 44);
    public static Rect2 RoomPicture(Rect2 cell) => cell.Grow(-12);
    public const float MarkerHeight = 64;
    public const float MarkerStep = 46;
}
