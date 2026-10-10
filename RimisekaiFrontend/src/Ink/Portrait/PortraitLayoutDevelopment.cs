using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 建造推入页：网格态＝缺角双线框里的 5×5 开发网格＋下方底座（钱料计数、撤销）；
/// 点格进全高建造抽屉：分段（可建/已有/门）→ 两行分类页签 → 5 列格子 → 底部常驻详情卡。
/// </summary>
public static partial class PortraitLayout
{
    public const float DevCell = 196f;
    public static Vector2 DevOrigin => new((CanvasWidth - DevCell * GridCols) / 2f, PageTop.End.Y + 64f);
    public static Rect2 DevelopmentGrid => new(DevOrigin, new Vector2(DevCell * GridCols, DevCell * GridRows));
    public static Rect2 DevelopmentFrame => DevelopmentGrid.Grow(24f);
    public static Rect2 DevelopmentCell(int x, int y) => new(DevOrigin.X + x * DevCell, DevOrigin.Y + y * DevCell, DevCell, DevCell);

    public static Rect2 DevelopmentPanel => new(0, DevelopmentFrame.End.Y + 36f, CanvasWidth, CanvasHeight - DevelopmentFrame.End.Y - 36f);

    /// <summary>网格态底座里的钱料计数行（中线 y）。</summary>
    public static float DevelopmentStockY => DevelopmentPanel.Position.Y + 110f;

    /// <summary>网格态底座里的撤销钮。</summary>
    public static Rect2 DevelopmentUndo => new(Pad, DevelopmentPanel.Position.Y + 200f, FullWidth, TouchMin);

    // ---------- 全高建造抽屉 ----------

    public static float BuildTop => PageTop.End.Y;
    public static Rect2 BuildSheet => new(0, BuildTop, CanvasWidth, CanvasHeight - BuildTop);
    public static Rect2 BuildSegment => new(Pad, BuildTop + 36f, FullWidth, TouchMin);

    public const int BuildCols = 5;
    public const float BuildTabGap = 12f;
    public static float BuildTabWidth => (FullWidth - BuildTabGap * (BuildCols - 1)) / BuildCols;
    public static float BuildTabsTop => BuildSegment.End.Y + 20f;
    public static Rect2 BuildTab(int i) => new(Pad + i % BuildCols * (BuildTabWidth + BuildTabGap),
        BuildTabsTop + i / BuildCols * (TouchMin + BuildTabGap), BuildTabWidth, TouchMin);
    public static Rect2 BuildTabs => new(0, BuildTabsTop, CanvasWidth, TouchMin * 2f + BuildTabGap);

    /// <summary>格子区上沿：有分类页签时在页签下，否则紧贴分段下。</summary>
    public static float BuildBodyTop(bool tabs) => (tabs ? BuildTabs.End.Y : BuildSegment.End.Y) + 24f;

    public const float BuildCardHeight = 520f;
    public static Rect2 BuildCard => new(Pad, CanvasHeight - 40f - BuildCardHeight, FullWidth, BuildCardHeight);

    public const float BuildTileGap = 12f;
    public const float BuildTileHeight = 210f;
    public const float BuildTileStep = BuildTileHeight + 16f;
    public static float BuildTileWidth => (FullWidth - BuildTileGap * (BuildCols - 1)) / BuildCols;

    /// <summary>格子区一屏放得下几行（格子区下沿离详情卡 24）。</summary>
    public static int BuildTileRows(float top) => (int)((BuildCard.Position.Y - 24f - top + 16f) / BuildTileStep);
    public static Rect2 BuildTile(float top, int i) => new(Pad + i % BuildCols * (BuildTileWidth + BuildTileGap),
        top + i / BuildCols * BuildTileStep, BuildTileWidth, BuildTileHeight);
    public static Rect2 BuildTileArea(float top) => new(0, top, CanvasWidth, BuildTileRows(top) * BuildTileStep - 16f);

    /// <summary>门分段的一行（与旧建造页门行同高同步距）。</summary>
    public static Rect2 BuildRow(float top, int i) => new(Pad, top + i * SheetRowStep, FullWidth, TouchMin);

    // 详情卡内部
    public static float BuildCardNameY => BuildCard.Position.Y + 70f;
    public const float BuildCondStep = 72f;
    public static Rect2 BuildCond(int i)
    {
        var card = BuildCard;
        var w = (card.Size.X - 80f) / 2f;
        return new Rect2(card.Position.X + 40f + i % 2 * w, card.Position.Y + 130f + i / 2 * BuildCondStep, w, BuildCondStep);
    }
    public static Rect2 BuildMain(bool undo) => undo
        ? new Rect2(BuildCard.Position.X + 40f + 360f + 24f, BuildCard.End.Y - 40f - TouchMin, BuildCard.Size.X - 80f - 384f, TouchMin)
        : new Rect2(BuildCard.Position.X + 40f, BuildCard.End.Y - 40f - TouchMin, BuildCard.Size.X - 80f, TouchMin);
    public static Rect2 BuildUndo => new(BuildCard.Position.X + 40f, BuildCard.End.Y - 40f - TouchMin, 360f, TouchMin);
}
