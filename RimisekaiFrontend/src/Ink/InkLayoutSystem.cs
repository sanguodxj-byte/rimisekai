using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 系统页与行旅页版式：设置项、存档栏、世界地图格与地区行、据点底部三个入口钮。
/// </summary>
public static partial class InkLayout
{
    // ---------- 系统与行旅页面 ----------

    // 走属性而非字段：依赖 InkLayoutCharacter.cs 的 CharacterRail / CharacterBody，
    // 拆成多文件后静态字段的初始化顺序不再由本文件决定，必须延迟求值。
    public static Rect2 SystemSidebar => CharacterRail;
    public static Rect2 SystemContent => CharacterBody;
    public static Rect2 SystemSidebarEntry(int index) => new(108f, 204f + index * 64f, 340f, 48f);
    public static Rect2 SystemRow(int index) => new(532f, 204f + index * 76f, 1280f, 60f);
    public static Rect2 SystemOption(Rect2 row, int index, int count)
    {
        var w = (row.Size.X * 0.62f - 12f * (count - 1)) / Mathf.Max(1, count);
        return new Rect2(row.End.X - row.Size.X * 0.62f + index * (w + 12f), row.Position.Y + 6f, w, 48f);
    }
    public static readonly Rect2 JourneyMap = new(78, 174, 1060, 600);
    public static readonly Rect2 JourneyList = new(1162, 174, 680, 600);
    public static readonly Rect2 JourneyDetail = new(78, 798, 1764, 144);
    public static Rect2 JourneyCell(int col, int row) => new(108f + col * 200f, 262f + row * 92f, 200f, 92f);
    public static Rect2 JourneyRow(int index) => new(1192f, 250f + index * 56f, 620f, 48f);
    public static Rect2 JourneyAction(int slot, int count) => new(1842f - Pad - count * 172f + slot * 172f, 844f, 156f, 52f);
    public static Rect2 JourneyTab(int index) => PageTab(index, 3);
    public static readonly Rect2 PageHeaderDivider = new(78, 128, 1764, 0f);
    public static readonly Rect2 HubSystemEntry = new(1742, 1012, 130, 42);
    public static readonly Rect2 HubWorldEntry = new(1598, 1012, 130, 42);

}
