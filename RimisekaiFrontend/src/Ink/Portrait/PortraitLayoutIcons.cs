using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 领地页签几何：提示条 / 5×5 领地网格（缺角双线框）/ 「此刻」头像带 / 两枚浮动药丸（出行、建造）。
/// 全部引用 Body / TabTop（别的文件的量），一律写成表达式体属性。
/// </summary>
public static partial class PortraitLayout
{
    public const float MapCell = 184f;

    /// <summary>提示条：最新一条操作反馈，空时不画（位置保留，网格不跳动）。</summary>
    public static Rect2 AlertStrip => new(Pad, Body.Position.Y + 24f, FullWidth, 110f);

    public static Vector2 MapOrigin => new((CanvasWidth - MapCell * GridCols) / 2f, AlertStrip.End.Y + 48f);
    public static Rect2 MapGrid => new(MapOrigin, new Vector2(MapCell * GridCols, MapCell * GridRows));
    public static Rect2 MapFrame => MapGrid.Grow(24f);

    public static Rect2 Cell(int x, int y) =>
        new(MapOrigin.X + x * MapCell, MapOrigin.Y + y * MapCell, MapCell, MapCell);

    /// <summary>格内名字下方的棋子带：底线贴格内框上方 14px，左右各让 16px。</summary>
    public static Rect2 CellPieces(Rect2 cell) => new(cell.Position.X + 22f, cell.End.Y - 84f, cell.Size.X - 44f, 64f);

    /// <summary>格内棋子：高 58、步距 44（184 宽的格放三枚）。</summary>
    public const float PieceHeight = 58f;
    public const float PieceStep = 44f;

    /// <summary>「此刻」头像右下角的棋子徽半径。</summary>
    public const float BadgeRadius = 30f;

    public static float NowRuleY => MapFrame.End.Y + 60f;

    /// <summary>「此刻」头像带：横向可拖，每人一格 250 宽；头像圆 r=66。</summary>
    public const float NowSlot = 250f;
    public static Rect2 NowStrip => new(0, NowRuleY + 28f, CanvasWidth, 290f);
    public static Rect2 NowCard(int i, int offset) => new(Pad + i * NowSlot - offset, NowStrip.Position.Y, NowSlot - 16f, NowStrip.Size.Y);

    /// <summary>浮动药丸：左「出行」（描边）、右「建造」（实心），压在页签带上方。</summary>
    public static Rect2 TravelButton => new(Pad + 20f, TabTop - 152f, 320f, 124f);
    public static Rect2 BuildButton => new(CanvasWidth - Pad - 20f - 320f, TabTop - 152f, 320f, 124f);

    // ---------- 设施抽屉 ----------

    public const float RoomSheetTop = 1060f;
    public static Rect2 RoomSheetRow(int i) => new(Pad, RoomSheetTop + 520f + i * 140f, FullWidth, 124f);
    public static int RoomSheetRows => (int)((SheetFooter.Position.Y - 24f - RoomSheetRow(0).Position.Y) / 140f);
    public static Rect2 RoomSheetUse(Rect2 row) => new(row.End.X - 230f, row.Position.Y + 3f, 230f, TouchMin);
}
