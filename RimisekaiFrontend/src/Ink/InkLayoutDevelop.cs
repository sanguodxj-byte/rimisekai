using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 开发页版式：左上领地网格、右上选中房间的设施、左下已建未安装的房间、
/// 中下操作面板（与主界面同一套落位）、右下详情，以及开拓确认弹窗。
/// </summary>
public static partial class InkLayout
{
    // ---------- 开发页（2026-10-01 主人定） ----------
    // 五块的用途重新分工：
    //   左上＝领地网格（已开发房间 ＋ 邻近可开拓的未开发房间，点未开发格弹确认窗）
    //   右上＝当前选中房间的设施（只列这一间房里的）
    //   左下＝已建但还没安装的房间（选中后点网格空格放上去）
    //   中下＝操作面板（所有操作按钮都归拢到这里，标题带右端常驻「退出开发」）
    //   右下＝详情（只有文字，**不画任何内部边框**）

    /// <summary>开发页：非空房间格右上角的拆除 X（点击热区，复用地图格）。</summary>
    public static Rect2 DevRoomX(Rect2 cell) => new(
        cell.End.X - 24f, cell.Position.Y + 4f, 20f, 20f);

    /// <summary>开发页：右上面板的设施行区（日志面板内容区）。</summary>
    public static Rect2 DevFacilityArea => LogContent;

    /// <summary>开发页：右上面板的设施行。</summary>
    public static Rect2 DevFacilityRow(int j) => new(
        LogContent.Position.X, LogContent.Position.Y + j * PageRowStep,
        LogContent.Size.X - ScrollBarGutter, PageRowHeight);

    /// <summary>开发页：右上面板能放下的设施行数。</summary>
    public static int DevFacilityVisibleRows =>
        Mathf.Max(1, (int)(LogContent.Size.Y / PageRowStep));

    /// <summary>开发页：左下面板的房间行区（角色面板内容区）。</summary>
    public static Rect2 DevRoomArea => CharContent;

    /// <summary>开发页：左下面板的房间行。</summary>
    public static Rect2 DevRoomRow(int j) => new(
        CharContent.Position.X, CharContent.Position.Y + j * PageRowStep,
        CharContent.Size.X - ScrollBarGutter, PageRowHeight);

    /// <summary>开发页：左下面板能放下的房间行数。</summary>
    public static int DevRoomVisibleRows =>
        Mathf.Max(1, (int)(CharContent.Size.Y / PageRowStep));

    /// <summary>
    /// 开发页：中下操作面板的行区。
    /// 与主界面**同一套落位**——大按钮（<see cref="WorkEntry"/>）下面接行，
    /// 行区起于大按钮下沿 + 16，行位与主界面的页面入口 <see cref="PageEntry"/> 逐格重合。
    /// 两界面只换文字，几何一致。
    /// 走属性而非字段：依赖 <see cref="WorkEntry"/> 与 <see cref="WorkPanel"/>（在 InkLayout.cs），
    /// 拆成多文件后静态字段的初始化顺序不再由本文件决定，必须延迟求值。
    /// </summary>
    public static Rect2 DevActionArea => new(
        WorkEntry.Position.X, WorkEntry.End.Y + 16f,
        WorkEntry.Size.X, WorkPanel.End.Y - Pad - (WorkEntry.End.Y + 16f));

    /// <summary>开发页：操作面板的按钮行高与行距，与主界面页面入口一致（46 高 / 54 步）。</summary>
    public const float DevActionHeight = 46f;
    public const float DevActionStep = 54f;

    /// <summary>开发页：操作面板的第 j 个按钮行。</summary>
    public static Rect2 DevActionRow(int j) => new(
        DevActionArea.Position.X, DevActionArea.Position.Y + j * DevActionStep,
        DevActionArea.Size.X, DevActionHeight);

    /// <summary>开发页：操作面板一屏能放下的按钮行数（末行整行放得下才算）。</summary>
    public static int DevActionVisibleRows => Mathf.Max(1,
        1 + (int)((DevActionArea.Size.Y - DevActionHeight) / DevActionStep));

    /// <summary>
    /// 开发页：大按钮位常驻的「退出开发」——直接占主界面「工作安排」那一格。
    /// 两界面中下共用一套落位，只换文字；不占行区，始终看得见。
    /// </summary>
    public static Rect2 DevExitButton => WorkEntry;

    // ---------- 开发页：开拓确认弹窗 ----------

    /// <summary>开拓确认弹窗：居中一块小面板，问「是否消耗材料和钱开发成空房间」。</summary>
    public static readonly Rect2 DevConfirmPanel = new(560, 390, 800, 300);
    public static readonly Rect2 DevConfirmTitle = new(592, 418, 736, 40);
    public static readonly Rect2 DevConfirmBody = new(592, 468, 736, 112);

    public static Rect2 DevConfirmButton(int slot)
    {
        const float w = 180f;
        const float gap = 20f;
        var total = w * 2f + gap;
        var x = DevConfirmPanel.GetCenter().X - total / 2f + slot * (w + gap);
        return new Rect2(x, DevConfirmPanel.End.Y - Pad - 52f, w, 52f);
    }

}
