using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 交易页版式：三面板——左玩家领地、中交易键（顶部让位给 9:16 竖版大插画位）、右市场。
/// 左右两栏与全屏页同一套外沿（142..1002），各占 620 宽；中栏 452 宽。
/// </summary>
public static partial class InkLayout
{
    // ---------- 交易页三面板 ----------

    /// <summary>左：领地物品清单（玩家背包内可出售物品）。</summary>
    public static readonly Rect2 TradePlayerPanel = new(78, FullPageTop, 620, FullPageHeight);

    /// <summary>中：交易操作面板。下移让位给 9:16 插画位。</summary>
    public static readonly Rect2 TradeCenterPanel = new(734, 796, 452, FullPageBottom - 796f);

    /// <summary>右：市场在售清单（今日集市货品）。</summary>
    public static readonly Rect2 TradeMarketPanel = new(1222, FullPageTop, 620, FullPageHeight);

    public static Rect2 TradePlayerArea => TradePlayerPanel.Grow(-Pad);
    public static Rect2 TradeCenterArea => TradeCenterPanel.Grow(-Pad);
    public static Rect2 TradeMarketArea => TradeMarketPanel.Grow(-Pad);

    public const float TradeRowHeight = 44f;
    public const float TradeRowStep = 50f;

    /// <summary>交易页一屏可见行数（扣除标题带与底部汇总带）。</summary>
    public static int TradeVisibleRows(Rect2 panel) =>
        Mathf.Max(1, (int)((panel.Size.Y - TitleBand - Pad - 36f) / TradeRowStep));

    /// <summary>
    /// 两栏清单的第 i 行。行区从标题带下沿自然向下排列（行首不悬空，行多时滑条滚动）。
    /// 右侧留出 45 像素供滑条轨道使用，绝不压线。
    /// </summary>
    public static Rect2 TradeRow(Rect2 panel, int i) => new(
        panel.Position.X + Pad * 0.5f,
        panel.Position.Y + TitleBand + TradeRowGap + i * TradeRowStep,
        panel.Size.X - Pad - 15f, TradeRowHeight);

    /// <summary>标题带下饰线与首行之间的净空。</summary>
    public const float TradeRowGap = 10f;

    /// <summary>行内三段的固定列位：名称（左）、数量、价钱（右）。</summary>
    public static float TradeNameWidth(Rect2 row) => row.Size.X - 235f;
    public static float TradeCountRight(Rect2 row) => row.End.X - 130f;
    public static float TradePriceRight(Rect2 row) => row.End.X - 10f;

    /// <summary>
    /// 中栏顶部 9:16 竖幅大插画位：居中于 452 宽的中栏，
    /// 宽 360，高 640（360×16/9 = 640），宽高比严格 0.5625 (9:16)。
    /// 纵向自 142 延伸至 782，两端各留 46px 呼吸空隙。
    /// </summary>
    public static readonly Rect2 TradeIllustrationRect = new(780, FullPageTop, 360, 640);

    /// <summary>
    /// 左栏（领地）标题带右侧的金钱：与领地标题同一基线、右对齐。
    /// </summary>
    public static readonly Rect2 TradeMoneyRect = new(108, FullPageTop + 20f, 548, 32);

    /// <summary>中栏交易面板：选中项名称（居中）。</summary>
    public static readonly Rect2 TradeSelectRect = new(754, 812, 412, 28);

    /// <summary>中栏交易面板：准备状态与金额（居中：左侧点击准备卖出，右侧准备购买）。</summary>
    public static readonly Rect2 TradeDescRect = new(754, 844, 412, 24);

    /// <summary>
    /// 成交按钮：标签为「成交」，无任何箭头，完全居中（260×54）。
    /// 点击后成交。
    /// </summary>
    public static readonly Rect2 TradeTradeButton = new(830, 880, 260, 54);

    /// <summary>中栏交易面板：交易结果反馈文本（居中）。</summary>
    public static readonly Rect2 TradeNoticeRect = new(754, 948, 412, 24);


}
