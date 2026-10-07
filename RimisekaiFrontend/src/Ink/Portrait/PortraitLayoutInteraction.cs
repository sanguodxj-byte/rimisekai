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
    public static Rect2 StorageButton(Rect2 row, int b) =>
        new(row.End.X - (2 - b) * 200f + 10f, row.Position.Y + 3f, 190f, TouchMin);

    // ---------- 对话整屏 ----------

    /// <summary>右上：状态 / 技能 / 日程三枚入口签。</summary>
    public static Rect2 SceneEntry(int i) => new(CanvasWidth - Pad - (3 - i) * 190f, SafeTop + 20f, 174f, TouchMin);

    public const float SceneChoiceStep = 140f;
    public static Rect2 SceneChoice(int visible, int i) =>
        new(60f, CanvasHeight - 40f - visible * SceneChoiceStep + i * SceneChoiceStep, CanvasWidth - 120f, 120f);
    public static Rect2 SceneDialog(int visibleChoices) =>
        new(30f, CanvasHeight - 40f - visibleChoices * SceneChoiceStep - 24f - 470f, CanvasWidth - 60f, 470f);
    public static Rect2 SceneText(Rect2 dialog) => new(dialog.Position.X + 60f, dialog.Position.Y + 50f, dialog.Size.X - 120f, 400f);
    public const float SceneLineHeight = 80f;
    public const int SceneVisibleLines = 5;
}
