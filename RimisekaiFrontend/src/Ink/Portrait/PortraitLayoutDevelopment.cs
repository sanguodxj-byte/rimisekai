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
    public static Rect2 DevelopmentUndo => new(Pad, DevelopmentPanel.Position.Y + 200f, FullWidth, BuildButtonHeight);

    // ---------- 全高建造抽屉（2026-10-11 v3：手机触控放大，一切可点块 ≥ TouchComfort 148） ----------

    /// <summary>次级钮高（撤销、拆除确认外的一般钮）与主钮高：手机上主操作要一眼一指。</summary>
    public const float BuildButtonHeight = 150f;
    public const float BuildPrimaryHeight = 170f;

    /// <summary>建造页顶栏：返回与「完成」的命中块撑满顶栏高（150），宽 200，画法与别的推入页相同。</summary>
    public static Rect2 BuildBack => new(0, SafeTop, 200f, PageTopHeight);
    public static Rect2 BuildDoneHit => new(CanvasWidth - 200f, SafeTop, 200f, PageTopHeight);

    public static float BuildTop => PageTop.End.Y;
    public static Rect2 BuildSheet => new(0, BuildTop, CanvasWidth, CanvasHeight - BuildTop);
    public static Rect2 BuildSegment => new(Pad, BuildTop + 24f, FullWidth, TouchComfort);

    /// <summary>分类页签：5 列两行铭牌，每块 200×148（四字类名 44 号 176 宽，左右各留 12）；页签带比正文多伸 16 到屏边。</summary>
    public const int BuildTabCols = 5;
    public const float BuildTabGap = 8f;
    public const float BuildTabPad = 24f;
    public const float BuildTabHeight = TouchComfort;
    public static float BuildTabWidth => (CanvasWidth - BuildTabPad * 2f - BuildTabGap * (BuildTabCols - 1)) / BuildTabCols;
    public static float BuildTabsTop => BuildSegment.End.Y + 20f;
    public static Rect2 BuildTab(int i) => new(BuildTabPad + i % BuildTabCols * (BuildTabWidth + BuildTabGap),
        BuildTabsTop + i / BuildTabCols * (BuildTabHeight + 14f), BuildTabWidth, BuildTabHeight);
    public static Rect2 BuildTabs => new(0, BuildTabsTop, CanvasWidth, BuildTabHeight * 2f + 14f);

    public static float BuildBodyTop(bool tabs) => (tabs ? BuildTabs.End.Y : BuildSegment.End.Y) + 22f;

    /// <summary>详情卡：哥特框，标题带（名＋题签，压窗花底纹）→ 两列三行条件 → 主钮行。</summary>
    public const float BuildCardHeight = 600f;
    public static Rect2 BuildCard => new(Pad, CanvasHeight - 28f - BuildCardHeight, FullWidth, BuildCardHeight);
    public static Rect2 BuildCardBand => new(BuildCard.Position.X + 150f, BuildCard.Position.Y + 22f, BuildCard.Size.X - 300f, 128f);
    public static float BuildCardNameY => BuildCard.Position.Y + 64f;
    public static float BuildCardTagY => BuildCard.Position.Y + 122f;

    /// <summary>格子：4 列见方 238，行距 254；一屏 4 行 16 格。</summary>
    public const int BuildCols = 4;
    public const float BuildTileGap = 16f;
    public static float BuildTileWidth => (FullWidth - BuildTileGap * (BuildCols - 1)) / BuildCols;
    public static float BuildTileHeight => BuildTileWidth;
    public static float BuildTileStep => BuildTileHeight + BuildTileGap;

    public static int BuildTileRows(float top) => (int)((BuildCard.Position.Y - 22f - top + BuildTileGap) / BuildTileStep);
    public static Rect2 BuildTile(float top, int i) => new(Pad + i % BuildCols * (BuildTileWidth + BuildTileGap),
        top + i / BuildCols * BuildTileStep, BuildTileWidth, BuildTileHeight);
    public static Rect2 BuildTileArea(float top) => new(0, top, CanvasWidth, BuildTileRows(top) * BuildTileStep - BuildTileGap);

    /// <summary>门行：148 高，行距 164。</summary>
    public const float BuildRowStep = TouchComfort + 16f;
    public static Rect2 BuildRow(float top, int i) => new(Pad, top + i * BuildRowStep, FullWidth, TouchComfort);

    public const float BuildCondStep = 72f;
    public static Rect2 BuildCond(int i)
    {
        var card = BuildCard;
        var w = (card.Size.X - 72f) / 2f;
        return new Rect2(card.Position.X + 36f + i % 2 * w, card.Position.Y + 168f + i / 2 * BuildCondStep, w, BuildCondStep);
    }

    private static float BuildButtonsY => BuildCard.End.Y - 28f - BuildPrimaryHeight;
    public const float BuildUndoWidth = 340f;
    public static Rect2 BuildMain(bool undo) => undo
        ? new Rect2(BuildCard.Position.X + 36f + BuildUndoWidth + 20f, BuildButtonsY, BuildCard.Size.X - 72f - BuildUndoWidth - 20f, BuildPrimaryHeight)
        : new Rect2(BuildCard.Position.X + 36f, BuildButtonsY, BuildCard.Size.X - 72f, BuildPrimaryHeight);
    public static Rect2 BuildUndo => new(BuildCard.Position.X + 36f, BuildButtonsY, BuildUndoWidth, BuildPrimaryHeight);
}
