using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏版式：与横屏 InkLayout 完全平行的一套坐标，基准画布 1080×2340。
///
/// 一切尺寸由物理毫米反推（20:9 手机上 1 画布 px = 0.0644mm）：
/// 触控下限 7.6mm(48dp)、字号下限 2.83mm、细线下限 0.32mm。
/// 横屏那套按 0.277mm/px 定的 1px 线与 26px 字在这块屏上会消失，所以不复用它的常量。
/// </summary>
public static partial class PortraitLayout
{
    public const float CanvasWidth = 1080f;
    public const float CanvasHeight = 2340f;

    /// <summary>20:9 手机（2400×1080、393ppi）等比缩放 1.0 时，一个画布像素的物理毫米数。</summary>
    public const float MmPerPx = 0.0644f;

    /// <summary>48dp＝7.6mm 最小可点边长。</summary>
    public const int TouchMin = 118;

    /// <summary>60dp＝9.5mm 舒适边长。</summary>
    public const int TouchComfort = 148;

    /// <summary>细线 0.32mm、主线 0.52mm。低于 5px 的线在手机上是灰影而非线。</summary>
    public const float LineHair = 5f;
    public const float LineBold = 8f;

    /// <summary>
    /// 装饰档：角花边长 88px＝5.7mm、笔画 4.5px＝0.29mm、珠径 6px。
    /// 角花是这套界面的定稿语汇（细双线框＋四角角花），竖屏沿用同一形状，
    /// 只把横屏那套 1.2px 笔画 / 2.4px 珠换成毫米档——1.2px 在这里只有 0.077mm，画出来是灰影。
    /// </summary>
    public const float Flourish = 88f;
    public const float OrnamentWidth = 4.5f;
    public const float OrnamentJewel = 6f;

    /// <summary>贴角排线：区深 300px、线间隔 16px（横屏是 240/7，手机上 7px 间隔会糊成一片灰雾）。</summary>
    public const float EtchZone = 300f;
    public const float EtchSpacing = 16f;

    /// <summary>字号三档：面板标题 60、正文 50、下限 44（2.83mm）。</summary>
    public const int FontTitle = 60;
    public const int FontBody = 50;
    public const int FontMeta = 44;

    /// <summary>框线到内容的留白与标题带高度。</summary>
    public const int Pad = 40;
    public const int TitleBand = 118;

    /// <summary>列表行高＝触控下限，一行一个手指位。</summary>
    public const int RowHeight = TouchMin;

    /// <summary>领地网格：5×5，单格 216px＝13.9mm 方格。</summary>
    public const int GridCols = 5;
    public const int GridRows = 5;
    public const int CellSize = 216;

    // ---------- 三段骨架 ----------

    /// <summary>顶栏：左·地点名（点开改名），右·状态行。整条落在可点高度内。</summary>
    public static readonly Rect2 Header = new(0, 0, CanvasWidth, 120);
    public static readonly Rect2 PlaceNameRect = new(Pad, 0, 420, 120);

    /// <summary>状态行四格，位置固定，数值宽度变化不互相顶动。</summary>
    public static Rect2 HeaderSlot(int i) => new(CanvasWidth - Pad - (4 - i) * 170, 0, 170, 120);

    /// <summary>内容区：页签带以上、顶栏以下的全部空间。</summary>
    public static readonly Rect2 Content = new(0, 120, CanvasWidth, 1980);

    /// <summary>底部页签带：整条都在拇指可达区内（自底 80mm）。</summary>
    public static readonly Rect2 TabBar = new(0, 2100, CanvasWidth, 240);
    public static readonly int TabCount = 4;
    public static Rect2 Tab(int i) => new(i * (CanvasWidth / TabCount), 2100, CanvasWidth / TabCount, 240);

    // ---------- 据点主界面（地图页）----------

    /// <summary>
    /// 三段常显：地图网格（上）／角色头像（中）／设施栏（下）。
    /// 地点插画带已删除（主人 2026-10-05 定）：地图上移到内容区顶端，空出的中部让给角色头像。
    /// 网格 5×216＝1080 高，落 120..1200；头像带 617 高，落 1200..1817；设施栏 283 高，落 1817..2100。
    /// </summary>
    public static readonly Rect2 GridArea = new(0, 120, CanvasWidth, GridRows * CellSize);
    public static readonly Rect2 AvatarArea = new(0, 1200, CanvasWidth, 617);
    public static readonly Rect2 FixtureArea = new(0, 1817, CanvasWidth, 283);

    public static Rect2 Cell(int x, int y) =>
        new(x * CellSize, GridArea.Position.Y + y * CellSize, CellSize, CellSize);

    public static Rect2 FixtureRow(int i) =>
        new(Pad, FixtureArea.Position.Y + 24 + i * RowHeight, FixtureArea.Size.X - Pad * 2f, RowHeight);

    // ---------- 列表页（日志 / 角色 / 操作共用） ----------

    public static float FullWidth => CanvasWidth - Pad * 2f;
    public static float ListWidth => CanvasWidth - Pad * 2f - TouchMin - 16f;

    public static Rect2 ListRow(int i, bool hasScroll = false) =>
        new(Pad, Content.Position.Y + 24 + i * RowHeight, hasScroll ? ListWidth : FullWidth, RowHeight);

    public static Rect2 OpRow(int i) =>
        new(Pad, Content.Position.Y + 24 + i * RowHeight, FullWidth, RowHeight);

    /// <summary>可视行数：整除后余量留给滑条呼吸，不静默裁切。</summary>
    public static int VisibleRows => (int)((Content.Size.Y - 48) / RowHeight);

    public static Rect2 ScrollBarRect =>
        new(CanvasWidth - Pad - TouchMin, Content.Position.Y + 24, TouchMin, VisibleRows * RowHeight);

    /// <summary>详情抽屉：从页签带上沿顶出来的半屏区。</summary>
    public static readonly Rect2 Drawer = new(0, 1250, CanvasWidth, 850);

    // ---------- 标题画面 ----------

    // 只取参考图中的标志，不把其中的横屏按钮再画一遍。
    public static readonly Rect2 TitleArtSource = new(310, 80, 1060, 390);
    public static readonly Rect2 TitleArt = new(Pad, 380, CanvasWidth - Pad * 2f,
        (CanvasWidth - Pad * 2f) * 390f / 1060f);

    /// <summary>整屏花框：参考稿那圈"细双线＋四角卷草＋贴角排线"的外框。</summary>
    public static readonly Rect2 TitleFrame = new(28, 28, CanvasWidth - 56f, CanvasHeight - 56f);

    /// <summary>
    /// 菜单钮：参考稿是居中一列带菱珠的短钮，不是通栏长条。
    /// 660px＝42.5mm，仍远在拇指舒适区内；上下留白对称（顶 352、底 354）。
    /// </summary>
    public const float TitleButtonWidth = 660f;

    public static Rect2 TitleButton(int i) => new((CanvasWidth - TitleButtonWidth) / 2f, 1140 + i * 204,
        TitleButtonWidth, TouchComfort);

    // ---------- 弹窗 ----------

    public static readonly Rect2 ModalPanel = new(Pad, 640, CanvasWidth - Pad * 2f, 1180);

    // ---------- 设施交互页 ----------

    public const int StorageRowHeight = RowHeight;
    public static int StorageRows => OverlayRows - 1;
    public static Rect2 StorageNameRect(float y) =>
        new(Pad, y, CanvasWidth - Pad * 2f - TouchMin * 2 - 56, StorageRowHeight);
    public static Rect2 StorageButton(float y, int b) =>
        new(CanvasWidth - Pad - (2 - b) * TouchMin - 16, y, TouchMin, StorageRowHeight);

    // ---------- 行内动作钮（开发页 / 交易 / 任务 / 系统） ----------

    public static Rect2 DevActionButton(float y) =>
        new(CanvasWidth - Pad - TouchMin * 2, y, TouchMin * 2, RowHeight);
    /// <summary>覆盖页底部动作钮：压在页签带上方 24px 处，不侵入页签带。</summary>
    public static readonly Rect2 BottomActionRow = new(Pad, 2100 - TouchComfort - 24, CanvasWidth - Pad * 2f, TouchComfort);
    public static readonly Rect2 DevBuildRow = BottomActionRow;
    public static readonly Rect2 TradeRunRow = BottomActionRow;
    public static readonly Rect2 SystemSaveRow = BottomActionRow;

    /// <summary>覆盖页的行起点：标题带下让出一整行给「返回」，再留 16px 安全空隙。</summary>
    public const int OverlayRowsTop = 120 + TitleBand + RowHeight + 16;
    public static int OverlayRows => (int)((TabBar.Position.Y - OverlayRowsTop) / RowHeight);
    public static Rect2 OverlayBack => new(Pad, 120 + TitleBand, FullWidth, RowHeight);
    public const int PartySlots = 4;
    public static Rect2 PartySlot(int i, float y) =>
        new(Pad + i * ((1080 - Pad * 2f) / PartySlots), y, (1080 - Pad * 2f) / PartySlots - 16, TouchComfort);
    public static Rect2 QuestStartRow(float y) =>
        new(Pad, y + TouchComfort + 24, 1080 - Pad * 2f, TouchComfort);
}
