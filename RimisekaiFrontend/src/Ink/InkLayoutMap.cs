using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 据点主界面版式：地图网格（领地 / 兴趣点 / 地下城共用一套）与右上日志面板
/// （日志段 + 此处设施段）。
/// </summary>
public static partial class InkLayout
{
    // ---------- 地图网格 ----------

    public static Rect2 Cell(int col, int row)
    {
        var w = MapContent.Size.X / GridCols;
        var h = MapContent.Size.Y / GridRows;
        return new Rect2(
            MapContent.Position.X + col * w,
            MapContent.Position.Y + row * h,
            w, h);
    }

    public static bool InGrid(int col, int row) =>
        col >= 0 && row >= 0 && col < GridCols && row < GridRows;

    public static int CellIndex(int col, int row) => row * GridCols + col;

    public static int CellCol(int index) => index % GridCols;

    public static int CellRow(int index) => index / GridCols;

    // ---------- 日志 / 此处 ----------

    /// <summary>日志行间步距（适配 26 号字排版）。</summary>
    public const float LogLineStep = 34f;
    public const float HereRowHeight = 44f;

    /// <summary>“此处”地名相对内容区顶部的偏移。</summary>
    public const float HereTitleOffset = 152f;

    /// <summary>地名行下饰分割线的 y：与面板标题下饰同款关系（文字顶 + <see cref="TitleRuleGap"/>）。</summary>
    public static float HereRuleY => LogContent.Position.Y + HereTitleOffset + TitleRuleGap;

    /// <summary>设施列表区顶（相对内容区）；在地名字线之下留 10px。一列到底放不下就从中间分第二列。</summary>
    public const float FixtureListTop = 196f;
    public const float FixtureColumnGap = 20f;
    public const float FixtureStepMin = 30f;
    public const float FixtureStepMax = 54f;

    public static float FixtureColumnWidth =>
        (LogContent.Size.X - FixtureColumnGap) / 2f;

    /// <summary>
    /// 右栏日志开关。未展开时在日志段右下角（地名行之上），展开时落在内容区右下角。
    /// ▼ 收起设施段铺满日志，▲ 还原两段布局。
    /// </summary>
    public static Rect2 LogToggleArrow(bool expanded) => new(
        LogContent.End.X - 44f,
        expanded ? LogContent.End.Y - 30f : LogContent.Position.Y + HereTitleOffset - 32f,
        44f, 30f);

    /// <summary>
    /// 日志行区：未展开时给设施段让出下段，并在右侧让出日志段右下角的开关位；
    /// 展开时铺满内容区。
    /// </summary>
    public static Rect2 LogLinesArea(bool expanded) => new(
        LogContent.Position.X, LogContent.Position.Y,
        expanded ? LogContent.Size.X : LogContent.Size.X - 52f,
        expanded ? LogContent.Size.Y - 38f : HereTitleOffset - 16f);

    /// <summary>设施段为空时的提示行 y（相对内容区顶）。</summary>
    public const float HereEmptyHintOffset = 200f;

    /// <summary>
    /// 设施行格子：塞得下就单列（行距在上下限间自动压缩，字体随之缩），
    /// 塞不下才从中间分割出第二列；两列也一样全量摆下，永无“未显示”。
    /// </summary>
    public static Rect2 FixtureCell(int index, int total)
    {
        // 设施列表一直排到内容区底（开关已移到日志段右下角，不再占设施段的空间）。
        var avail = LogContent.End.Y - 8f - (LogContent.Position.Y + FixtureListTop);
        var singleRows = Mathf.Max(1, (int)(avail / FixtureStepMin));
        var oneColumn = total <= singleRows;
        var rows = oneColumn ? Mathf.Max(1, total) : (total + 1) / 2;
        var step = Mathf.Clamp(avail / rows, FixtureStepMin, FixtureStepMax);
        var col = oneColumn ? 0 : index / rows;
        var row = oneColumn ? index : index % rows;
        var width = oneColumn ? LogContent.Size.X : FixtureColumnWidth;
        var height = Mathf.Min(HereRowHeight, step - 6f);
        return new Rect2(
            LogContent.Position.X + col * (width + (oneColumn ? 0f : FixtureColumnGap)),
            LogContent.Position.Y + FixtureListTop + row * step,
            width, height);
    }

}
