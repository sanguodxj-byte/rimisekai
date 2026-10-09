using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 领地页签几何：日志栏 / 5×5 领地网格（缺角双线框）/ 「此刻」头像带 / 两枚浮动药丸（出行、建造）。
/// 2026-10-08 主人改：网格与「此刻」带整体自下而上贴着药丸排，顶部腾出的高度全给日志栏。
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

    /// <summary>
    /// 日志字号自 50 往下自动收，下限 36（约 2.3mm，相当于手机系统 13pt 正文，高于 11pt 的系统最小字）。
    /// 这是全局 44 下限之外唯一的例外：日志一次要装下一整次操作，宁可字小一号也不吞条目。
    /// 规范要求 36 号下一次操作的日志必须全部放得下：玩家动作＋环境变化＋「此刻」当前页至多 3 人。
    /// </summary>
    public const int LogFontMax = FontBody;
    public const int LogFontMin = 36;


    /// <summary>「此刻」带：每页 4 人，每人一格 220 宽；人多时第 4 人右侧一枚翻页三角钮（120 宽，≥118）。</summary>
    public const float NowSlot = 220f;
    public const int NowPageSize = 4;

    /// <summary>「此刻」每页除主角外的人数：主角固定第 1 格，其余 3 格翻页（日志角色档同步只显示这 3 人）。</summary>
    public const int NowOthersPerPage = NowPageSize - 1;
    /// <summary>2026-10-09 主人改：名字下的状态行（空闲/睡觉…）去掉，带高只到名字底，省下的给日志。</summary>
    public const float NowStripHeight = 232f;
    public static Rect2 NowStrip => new(0, TravelButton.Position.Y - 12f - NowStripHeight, CanvasWidth, NowStripHeight);
    public static float NowRuleY => NowStrip.Position.Y - 28f;
    public static Rect2 NowCard(int i) => new(Pad + i * NowSlot, NowStrip.Position.Y, NowSlot - 16f, NowStrip.Size.Y);

    /// <summary>翻页三角钮：紧挨第 4 格右侧，垂直对准头像圆心，命中块 120×150。</summary>
    public static Rect2 NowPager => new(Pad + NowPageSize * NowSlot - 4f, NowStrip.Position.Y + 9f, 120f, 150f);

    public static Rect2 MapFrame => MapGrid.Grow(24f);
    public static Rect2 MapGrid => new(MapOrigin, new Vector2(MapCell * GridCols, MapCell * GridRows));
    public static Vector2 MapOrigin => new((CanvasWidth - MapCell * GridCols) / 2f,
        FacilityStrip.Position.Y - FacilityStripGap - 24f - MapCell * GridRows);

    // ---------- 设施牌：网格与「此刻」之间一排 ----------
    // 2026-10-09 主人定：网格不动，当前所在房间的设施直接摆成一排大牌，点牌即使用；
    // 一排放 Room.MaxFacilities（4）块，左右对齐网格外框，每块约 216×140——房间表的设施上限就从这里来。

    public const float FacilityPlaqueHeight = 140f;
    public const float FacilityPlaqueGap = 16f;

    /// <summary>网格框底到设施牌的间隔（南向过界箭头的命中块落在这段与网格底排之间）。</summary>
    public const float FacilityStripGap = 30f;

    /// <summary>左右与网格外框对齐（不依赖 MapFrame，免得与 MapOrigin 互相引用）。</summary>
    public static Rect2 FacilityStrip => new((CanvasWidth - MapCell * GridCols) / 2f - 24f, NowRuleY - 36f - FacilityPlaqueHeight,
        MapCell * GridCols + 48f, FacilityPlaqueHeight);

    public static float FacilityPlaqueWidth =>
        (FacilityStrip.Size.X - FacilityPlaqueGap * (Rimisekai.Housing.Room.MaxFacilities - 1)) / Rimisekai.Housing.Room.MaxFacilities;

    public static Rect2 FacilityPlaque(int i) => new(FacilityStrip.Position.X + i * (FacilityPlaqueWidth + FacilityPlaqueGap),
        FacilityStrip.Position.Y, FacilityPlaqueWidth, FacilityPlaqueHeight);

    public static Rect2 Cell(int x, int y) =>
        new(MapOrigin.X + x * MapCell, MapOrigin.Y + y * MapCell, MapCell, MapCell);

    /// <summary>领地格底的棋子带（主线棋子标识）：底线对齐，棋子高 54、步距 37（172 宽的格放四枚）；多于 4 人时第 4 位换成「+」。</summary>
    public static Rect2 CellPieces(Rect2 cell) => new(cell.Position.X + 12f, cell.End.Y - 80f, cell.Size.X - 24f, 62f);
    public const float PieceHeight = 54f;
    public const float PieceStep = 37f;
    public const int PieceCap = 4;

    /// <summary>「此刻」头像右下角的棋子徽半径。</summary>
    public const float BadgeRadius = 30f;

    /// <summary>浮动药丸：左「出行」（描边）、右「建造」（实心），压在页签带上方。</summary>
    public static Rect2 TravelButton => new(Pad + 20f, TabTop - 152f, 320f, 124f);
    public static Rect2 BuildButton => new(CanvasWidth - Pad - 20f - 320f, TabTop - 152f, 320f, 124f);

    // ---------- 过界：边框通道箭头 ----------
    // 主角站在有对外通道的房间（领地连接点 / 兴趣点边界通道）时，网格外缘对着该房朝外那条边的正中开一道缺口，
    // 缺口里一枚实心三角箭头指向外侧。只占网格与哥特框之间的边框带及其外侧空白，不进任何房间格。

    /// <summary>缺口沿边的长度（盖住框线两道线与上下缘正中的框珠）。</summary>
    public const float CrossGapSpan = 96f;

    /// <summary>箭头：底边长 64、底边到尖 40；底边离网格外缘 4，尖探出框外线 20。</summary>
    public const float CrossArrowBase = 64f;
    public const float CrossArrowDepth = 40f;
    public const float CrossArrowInset = 4f;

    /// <summary>箭头呼吸周期（秒），与弹窗底端三角同拍。</summary>
    public const float CrossBreath = 1.8f;

    /// <summary>过界镜头平移时长（秒）：整格网格平移一屏，三次缓出。</summary>
    public const float CrossPan = 0.4f;

    /// <summary>朝外的单位向量（画布坐标，y 向下）。</summary>
    public static Vector2 CrossOutward(Rimisekai.Housing.Territory.RegionDir dir) => dir switch
    {
        Rimisekai.Housing.Territory.RegionDir.North => Vector2.Up,
        Rimisekai.Housing.Territory.RegionDir.East => Vector2.Right,
        Rimisekai.Housing.Territory.RegionDir.South => Vector2.Down,
        _ => Vector2.Left,
    };

    /// <summary>格朝外那条边的中点（即网格外缘上的通道口）。</summary>
    public static Vector2 CrossMouth(Rimisekai.Housing.Territory.RegionDir dir, Rect2 cell) =>
        cell.GetCenter() + CrossOutward(dir) * (MapCell / 2f);

    /// <summary>缺口：自网格外缘外 2px 起、到哥特框外线外 3px，沿边 <see cref="CrossGapSpan"/>。</summary>
    public static Rect2 CrossGap(Rimisekai.Housing.Territory.RegionDir dir, Rect2 cell)
    {
        var mouth = CrossMouth(dir, cell);
        var near = 2f;
        var far = MapGrid.Position.X - MapFrame.Position.X + 3f;
        return dir switch
        {
            Rimisekai.Housing.Territory.RegionDir.North => new Rect2(mouth.X - CrossGapSpan / 2f, mouth.Y - far, CrossGapSpan, far - near),
            Rimisekai.Housing.Territory.RegionDir.South => new Rect2(mouth.X - CrossGapSpan / 2f, mouth.Y + near, CrossGapSpan, far - near),
            Rimisekai.Housing.Territory.RegionDir.East => new Rect2(mouth.X + near, mouth.Y - CrossGapSpan / 2f, far - near, CrossGapSpan),
            _ => new Rect2(mouth.X - far, mouth.Y - CrossGapSpan / 2f, far - near, CrossGapSpan),
        };
    }

    /// <summary>
    /// 箭头命中块：沿边与该格等长（172），外沿到最近的邻件——东西到画布边、北到日志面板下沿、南到「此刻」头像带上沿；
    /// 再自外沿往格内伸，凑足 <see cref="TouchMin"/>（118）深（2026-10-08 主人定：命中块伸进门房格，只与门房格重叠）。
    /// 东西外伸 110 → 进格 8；南外伸 102 → 进格 16；北外伸 54 → 进格 64（不到格边长 172 的一半，只压门房格自己，不碰别的格）。
    /// </summary>
    public static Rect2 CrossHit(Rimisekai.Housing.Territory.RegionDir dir, Rect2 cell) => dir switch
    {
        Rimisekai.Housing.Territory.RegionDir.North => new Rect2(cell.Position.X, LogPanel.End.Y, MapCell, TouchMin),
        Rimisekai.Housing.Territory.RegionDir.South => new Rect2(cell.Position.X, FacilityStrip.Position.Y - TouchMin, MapCell, TouchMin),
        Rimisekai.Housing.Territory.RegionDir.East => new Rect2(CanvasWidth - TouchMin, cell.Position.Y, TouchMin, MapCell),
        _ => new Rect2(0f, cell.Position.Y, TouchMin, MapCell),
    };

    // ---------- 设施抽屉 ----------

    public const float RoomSheetTop = 1060f;
    public static Rect2 RoomSheetRow(int i) => new(Pad, RoomSheetTop + 520f + i * 140f, FullWidth, 124f);
    public static int RoomSheetRows => (int)((SheetFooter.Position.Y - 24f - RoomSheetRow(0).Position.Y) / 140f);
    public static Rect2 RoomSheetUse(Rect2 row) => new(row.End.X - 230f, row.Position.Y + 3f, 230f, TouchMin);
}
