using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    public static Rect2 DevelopmentGrid => new(Pad, 380f, 1000f, 1000f);
    public static Rect2 DevelopmentCell(int x, int y) => new(DevelopmentGrid.Position.X + x * 200f,
        DevelopmentGrid.Position.Y + y * 200f, 200f, 200f);
    public static Rect2 DevelopmentFacilities => new(Pad, 1404f, 486f, RowHeight * 2f);
    public static Rect2 DevelopmentRooms => new(554f, 1404f, 486f, RowHeight * 2f);
    public static Rect2 DevelopmentActions => new(Pad, 1664f, 1000f, RowHeight * 3f);
}
