using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 领地页签几何：日志面板 / 5×5 领地网格（缺角双线框）/ 「此刻」头像带 / 两枚浮动药丸（出行、建造）。
/// 全部引用 Body / TabTop（别的文件的量），一律写成表达式体属性。
/// </summary>
public static partial class PortraitLayout
{
    /// <summary>领地格边长 172（≥118 触控下限），省出的高度让给日志面板。</summary>
    public const float MapCell = 172f;

    /// <summary>
    /// 日志面板：HUD 之下到网格框之上的整块（约 430 高），自下而上排近期日志，点一下进日志页签。
    /// 高度由下方网格与「此刻」带反推：「此刻」带底贴浮动药丸上方 12px。
    /// </summary>
    public static Rect2 LogPanel => new(Pad, Body.Position.Y + 20f, FullWidth, MapFrame.Position.Y - 30f - (Body.Position.Y + 20f));

    /// <summary>日志面板内文字区：四边让 28px。</summary>
    public static Rect2 LogPanelText => LogPanel.Grow(-28f);

    /// <summary>日志字号自 50 往下收，不低于 44；收到 44 仍放不下就裁掉最旧的。</summary>
    public const int LogFontMax = FontBody;
    public const int LogFontMin = FontMeta;

    /// <summary>字号自适应时要求完整放下的最近几条。</summary>
    public const int LogFitEntries = 4;

    /// <summary>「此刻」带：每页 4 人，每人一格 220 宽；人多时第 4 人右侧一枚翻页三角钮（120 宽，≥118）。</summary>
    public const float NowSlot = 220f;
    public const int NowPageSize = 4;
    public const float NowStripHeight = 290f;
    public static Rect2 NowStrip => new(0, TravelButton.Position.Y - 12f - NowStripHeight, CanvasWidth, NowStripHeight);
    public static float NowRuleY => NowStrip.Position.Y - 28f;
    public static Rect2 NowCard(int i) => new(Pad + i * NowSlot, NowStrip.Position.Y, NowSlot - 16f, NowStrip.Size.Y);

    /// <summary>翻页三角钮：紧挨第 4 格右侧，垂直对准头像圆心，命中块 120×150。</summary>
    public static Rect2 NowPager => new(Pad + NowPageSize * NowSlot - 4f, NowStrip.Position.Y + 9f, 120f, 150f);

    public static Rect2 MapFrame => MapGrid.Grow(24f);
    public static Rect2 MapGrid => new(MapOrigin, new Vector2(MapCell * GridCols, MapCell * GridRows));
    public static Vector2 MapOrigin => new((CanvasWidth - MapCell * GridCols) / 2f, NowRuleY - 50f - 24f - MapCell * GridRows);

    public static Rect2 Cell(int x, int y) =>
        new(MapOrigin.X + x * MapCell, MapOrigin.Y + y * MapCell, MapCell, MapCell);

    /// <summary>格内名字下方的棋子带：左右各让 12px，最多排 4 枚。</summary>
    public static Rect2 CellPieces(Rect2 cell) => new(cell.Position.X + 12f, cell.End.Y - 80f, cell.Size.X - 24f, 62f);

    /// <summary>格内棋子：高 54、步距 37（172 宽的格放四枚）；多于 4 人时第 4 位换成「+」。</summary>
    public const float PieceHeight = 54f;
    public const float PieceStep = 37f;
    public const int PieceCap = 4;

    /// <summary>「此刻」头像右下角的棋子徽半径。</summary>
    public const float BadgeRadius = 30f;

    /// <summary>浮动药丸：左「出行」（描边）、右「建造」（实心），压在页签带上方。</summary>
    public static Rect2 TravelButton => new(Pad + 20f, TabTop - 152f, 320f, 124f);
    public static Rect2 BuildButton => new(CanvasWidth - Pad - 20f - 320f, TabTop - 152f, 320f, 124f);

    // ---------- 设施抽屉 ----------

    public const float RoomSheetTop = 1060f;
    public static Rect2 RoomSheetRow(int i) => new(Pad, RoomSheetTop + 520f + i * 140f, FullWidth, 124f);
    public static int RoomSheetRows => (int)((SheetFooter.Position.Y - 24f - RoomSheetRow(0).Position.Y) / 140f);
    public static Rect2 RoomSheetUse(Rect2 row) => new(row.End.X - 230f, row.Position.Y + 3f, 230f, TouchMin);
}
