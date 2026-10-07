using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    public static Rect2 ScheduleMembers => new(Pad, 380f, 316f, 1652f);
    public static Rect2 ScheduleSlots => new(380f, 380f, 660f, 472f);
    public static Rect2 ScheduleSlot(int index) => new(ScheduleSlots.Position.X + index % 2 * 338f,
        ScheduleSlots.Position.Y + index / 2 * 236f, 322f, 236f);
    public static Rect2 ScheduleSlotTop(int index) => new(ScheduleSlot(index).Position,
        new Vector2(ScheduleSlot(index).Size.X - TouchMin, TouchMin));
    public static Rect2 ScheduleSlotBottom(int index) => new(ScheduleSlot(index).Position.X,
        ScheduleSlot(index).Position.Y + TouchMin, ScheduleSlot(index).Size.X, TouchMin);
    public static Rect2 ScheduleCancel(int index) => new(ScheduleSlot(index).End.X - TouchMin,
        ScheduleSlot(index).Position.Y, TouchMin, TouchMin);
    public static Rect2 ScheduleGrid => new(380f, 868f, 660f, 660f);
    public static Rect2 ScheduleCell(int x, int y) => new(ScheduleGrid.Position.X + x * 132f,
        ScheduleGrid.Position.Y + y * 132f, 132f, 132f);
    public static Rect2 ScheduleFacilities => new(380f, 1544f, 660f, RowHeight * 2f);
    public static Rect2 ScheduleOutputs => new(380f, 1796f, 660f, RowHeight * 2f);
    public static int ScheduleMemberRows => (int)(ScheduleMembers.Size.Y / RowHeight);
}
