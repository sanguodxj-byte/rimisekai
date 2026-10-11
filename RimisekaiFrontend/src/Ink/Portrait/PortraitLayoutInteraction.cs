using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 交互几何：交互抽屉（设施行动 / 交流）、存取抽屉（仓库类设施）、对话整屏。
/// </summary>
public static partial class PortraitLayout
{
    public const float SheetRowStep = 140f;

    /// <summary>交互抽屉：行数决定抽屉高度，最多铺到 900。</summary>
    public static float InteractionSheetTop(int rows) =>
        Mathf.Max(900f, CanvasHeight - 60f - Mathf.Min(rows, 8) * SheetRowStep - SheetContentOffset);
    public static Rect2 InteractionRow(float top, int i) =>
        new(Pad, top + SheetContentOffset + i * SheetRowStep, FullWidth, TouchMin);
    public static int InteractionRows(float top) => (int)((CanvasHeight - 60f - top - SheetContentOffset) / SheetRowStep);

    /// <summary>存取抽屉：上沿 760；每行＝菱形首字＋名称＋仓/包数量＋放入/取出两枚药丸。</summary>
    public const float StorageSheetTop = 760f;
    public static Rect2 StorageRow(int i) => new(Pad, StorageSheetTop + SheetContentOffset + 40f + i * SheetRowStep, FullWidth, 124f);
    public static int StorageRows => (int)((CanvasHeight - 60f - StorageRow(0).Position.Y) / SheetRowStep);
    /// <summary>存取行的「放入 / 取出」：各 190 宽，两钮相隔 30（原 10，太挤易误触）；「取出」贴行右缘不动，「放入」左移 20。</summary>
    public static Rect2 StorageButton(Rect2 row, int b) =>
        new(row.End.X - 190f - (1 - b) * (190f + StorageButtonGap), row.Position.Y + 3f, 190f, TouchMin);
    public const float StorageButtonGap = 30f;
    /// <summary>存取抽屉的存储设置入口：收起钮左侧、隔 16 的一枚方钮（标题框右缘 760 之外的空位）。</summary>
    public static Rect2 StorageSettingsEntry(float top) =>
        new(SheetClose(top).Position.X - 16f - TouchMin, top + 50f, TouchMin, TouchMin);

    /// <summary>存储设置抽屉（与存取抽屉同上沿）：优先级五段 → 全部允许/全部清除 → 过滤树（可滚）。</summary>
    public static Rect2 StoragePrioritySegment => new(Pad, StorageSheetTop + 190f, FullWidth, TouchMin);
    public static Rect2 StorageAllowAll => new(Pad, StorageSheetTop + 330f, (FullWidth - 24f) / 2f, TouchMin);
    public static Rect2 StorageClearAll => new(Pad + (FullWidth + 24f) / 2f, StorageSheetTop + 330f, (FullWidth - 24f) / 2f, TouchMin);
    public static Rect2 StorageFilterRow(int i) => new(Pad, StorageSheetTop + 470f + i * SheetRowStep, FullWidth, TouchMin);
    public static int StorageFilterRows => (int)((CanvasHeight - 60f - StorageFilterRow(0).Position.Y + SheetRowStep - TouchMin) / SheetRowStep);
    /// <summary>过滤行右侧的收放钮（允许 / 部分 / 禁止），贴行右缘。</summary>
    public static Rect2 StorageFilterToggle(Rect2 row) => new(row.End.X - 190f, row.Position.Y, 190f, TouchMin);
    /// <summary>每深一级缩进。</summary>
    public const float StorageFilterIndent = 56f;

    // ---------- 对话整屏 ----------

    /// <summary>右上：状态 / 技能 / 日程三枚入口签。</summary>
    public static Rect2 SceneEntry(int i) => new(CanvasWidth - Pad - (3 - i) * 190f, SafeTop + 20f, 174f, TouchMin);

    public const float SceneChoiceStep = 140f;
    public static Rect2 SceneChoice(int visible, int i) =>
        new(60f, CanvasHeight - 40f - visible * SceneChoiceStep + i * SceneChoiceStep, CanvasWidth - 120f, 120f);
    public static Rect2 SceneDialog(int visibleChoices) =>
        new(30f, CanvasHeight - 40f - visibleChoices * SceneChoiceStep - 24f - SceneDialogHeight, CanvasWidth - 60f, SceneDialogHeight);
    /// <summary>对白框高：顶部名牌行 + 分隔线 + 5 行正文。</summary>
    public const float SceneDialogHeight = 560f;

    /// <summary>对话立绘（上半身）：贴左上，顶栏下起，左缘出血 40，宽 920、高 1250。</summary>
    public static Rect2 SceneBust => new(-40f, SafeTop + 20f + TouchMin + 20f, 920f, 1250f);
    /// <summary>名牌行（框内左上）：说话人名字 + 好感签，竖向中线。</summary>
    public static float SceneNameY(Rect2 dialog) => dialog.Position.Y + 62f;
    /// <summary>名牌行下的分隔线 y。</summary>
    public static float SceneNameRule(Rect2 dialog) => dialog.Position.Y + 108f;
    public static Rect2 SceneText(Rect2 dialog) => new(dialog.Position.X + 60f, dialog.Position.Y + 130f, dialog.Size.X - 120f, 400f);
    public const float SceneLineHeight = 80f;
    public const int SceneVisibleLines = 5;
}
