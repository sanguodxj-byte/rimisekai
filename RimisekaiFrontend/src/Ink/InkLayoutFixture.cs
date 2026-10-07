using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 设施交互页版式：点设施行动（如「打开货架」）后在左上角铺开、盖住地图的操作界面。
/// 品类开关带 + 存储行（物品名·品类｜设施内｜背包｜允许/禁止｜放入/取出/过滤）。
/// </summary>
public static partial class InkLayout
{
    // ---------- 设施交互页（左上角铺开，盖住地图）----------

    /// <summary>
    /// 设施交互页铺在地图面板那块上——点设施行动（如“打开货架”）后，
    /// 左上角从地图网格换成这件设施的操作界面。
    /// 走属性而非字段：依赖 <see cref="MapPanel"/>（在 InkLayout.cs），
    /// 拆成多文件后静态字段的初始化顺序不再由本文件决定，必须延迟求值。
    /// </summary>
    public static Rect2 FixturePanel => MapPanel;

    public static Rect2 FixtureContent => BelowTitle(MapPanel);

    /// <summary>设施交互页的行高与行距，与子页面列表同档。</summary>
    public const float FixtureRowHeight = 38f;
    public const float FixtureRowStep = 44f;

    /// <summary>品类开关带的高度（存储页顶部一排，按大类收放）。</summary>
    public const float CategoryBandHeight = 46f;

    /// <summary>一屏能放下的存储行数（已扣掉顶部品类带）。</summary>
    public static int FixtureVisibleRows =>
        Mathf.Max(1, (int)((FixtureContent.Size.Y - CategoryBandHeight) / FixtureRowStep));

    /// <summary>品类开关带：顶部通栏，一行放全部品类按钮。</summary>
    public static readonly Rect2 CategoryBand = new(
        FixtureContent.Position.X,
        FixtureContent.Position.Y,
        FixtureContent.Size.X, CategoryBandHeight);

    /// <summary>品类带上第 i 个开关。一行排满就折行，行高随按钮高。</summary>
    public static Rect2 StorageCategoryRow(int index)
    {
        const float w = 118f;
        const float h = 38f;
        const float gap = 8f;
        var perRow = Mathf.Max(1, (int)((CategoryBand.Size.X + gap) / (w + gap)));
        var col = index % perRow;
        var row = index / perRow;
        return new Rect2(
            CategoryBand.Position.X + col * (w + gap),
            CategoryBand.Position.Y + 4f + row * (h + 4f), w, h);
    }

    /// <summary>一屏能放下的品类行数（折行后）。</summary>
    public static int CategoryRowsPerScreen =>
        Mathf.Max(1, (int)((CategoryBandHeight - 4f) / 42f));

    /// <summary>
    /// 存储页第 i 行：左侧名称+品类+数量，右侧“放入/取出/过滤”三个小按钮。
    /// 右缘让出 <see cref="ScrollBarGutter"/>，滑条竖排在这条空隙里，不压按钮。
    /// </summary>
    public static Rect2 StorageRow(int i) => new(
        FixtureContent.Position.X,
        FixtureContent.Position.Y + CategoryBandHeight + i * FixtureRowStep,
        FixtureContent.Size.X - ScrollBarGutter, FixtureRowHeight);

    /// <summary>存储行右侧的小按钮。slot：0 放入、1 取出、2 过滤。</summary>
    public static Rect2 StorageRowButton(Rect2 row, int slot)
    {
        const float w = 92f;
        const float gap = 8f;
        const float total = w * 3f + gap * 2f;
        return new Rect2(row.End.X - total + slot * (w + gap), row.Position.Y, w, row.Size.Y);
    }

    /// <summary>设施交互页右上角的关闭按钮。</summary>
    public static Rect2 FixtureClose => new(
        FixturePanel.End.X - Pad - 34f, FixturePanel.Position.Y + Pad + 4f, 30f, 30f);

}
