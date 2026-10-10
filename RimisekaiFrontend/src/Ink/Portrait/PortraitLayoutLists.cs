using Godot;

namespace Rimisekai.Portrait;

/// <summary>根页签的卡片流几何（角色名册、委托板、仓储、日志）。</summary>
public static partial class PortraitLayout
{
    public const float ListDragThreshold = 24f;

    /// <summary>名册卡：整卡可点，高 320、间距 26。</summary>
    public const float RosterCardHeight = 320f;
    public const float RosterCardGap = 26f;
    public static Rect2 RosterView => new(0, Body.Position.Y + 24f, CanvasWidth, TabTop - Body.Position.Y - 24f);

    /// <summary>委托卡：高 560、间距 30；「接取」钮在卡右下。</summary>
    public const float QuestCardHeight = 560f;
    public const float QuestCardGap = 30f;
    public static Rect2 QuestView => new(0, Body.Position.Y + 24f, CanvasWidth, TabTop - Body.Position.Y - 24f);

    // ---------- 仓储 ----------

    /// <summary>库存：搜索条 / 品类签 / 三列物品卡。</summary>
    public static Rect2 StockSearch => new(Pad, BodySegment.End.Y + 24f, FullWidth, TouchMin);
    public static float StockChipsY => StockSearch.End.Y + 16f;
    public static Rect2 StockView => new(0, StockChipsY + TouchMin + 16f, CanvasWidth, TabTop - (StockChipsY + TouchMin + 16f));
    /// <summary>库存条：一件一条，高 150（上行物名、下行题签），条距 14。</summary>
    public const float StockRowHeight = 150f;
    public const float StockRowGap = 14f;
    public static Rect2 StockRow(int i, float offset) =>
        new(Pad, StockView.Position.Y + i * (StockRowHeight + StockRowGap) - offset, FullWidth, StockRowHeight);

    /// <summary>交易：买卖段 / 行（带 − n + 步进）/ 底部结算条。</summary>
    public static Rect2 TradeSegment => new(Pad, BodySegment.End.Y + 24f, FullWidth, TouchMin);
    public static Rect2 TradeCheckout => new(30f, TabTop - 24f - 170f, CanvasWidth - 60f, 170f);
    public static Rect2 TradeView => new(0, TradeSegment.End.Y + 24f, CanvasWidth, TradeCheckout.Position.Y - 24f - TradeSegment.End.Y - 24f);
    public const float TradeRowHeight = 160f;

    /// <summary>制作：工种签 / 配方行 / 详情框。</summary>
    public static float CraftChipsY => BodySegment.End.Y + 24f;
    public static Rect2 CraftDetail => new(Pad, TabTop - 24f - 640f, FullWidth, 640f);
    public static Rect2 CraftView => new(0, CraftChipsY + TouchMin + 16f, CanvasWidth,
        CraftDetail.Position.Y - 40f - (CraftChipsY + TouchMin + 16f));
    public const float CraftRowHeight = 150f;

    // ---------- 日志 ----------

    public static Rect2 LogView => new(0, Body.Position.Y + 24f, CanvasWidth, TabTop - Body.Position.Y - 24f);
    public const float LogLineHeight = 72f;
}
