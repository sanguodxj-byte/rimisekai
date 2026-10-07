using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 任务页版式：公会板式——左任务列表、右上任务详情、右下队伍编成。
/// </summary>
public static partial class InkLayout
{
    // ---------- 任务页（公会板式：左列表，右上详情，右下队伍编成）----------

    // 走属性而非字段：依赖 FullListPanel（在 InkLayoutPages.cs），
    // 拆成多文件后静态字段的初始化顺序不再由本文件决定，必须延迟求值。
    public static Rect2 QuestListPanel => FullListPanel;
    public static readonly Rect2 QuestDetailPanel = new(842f, 174f, 1000f, 432f);
    public static readonly Rect2 QuestPartyPanel = new(842f, 630f, 1000f, 312f);

    public const int QuestListRowsPerPage = 10;

    /// <summary>列表面板顶部的类别筛选签（无多余标题，直接从 Pad 起）。</summary>
    public static Rect2 QuestFilterChip(int index) => new(
        QuestListPanel.Position.X + Pad + index * 128f,
        QuestListPanel.Position.Y + Pad, 118f, 36f);

    public const float QuestListRowStep = 54f;

    public static Rect2 QuestListRow(int index) => new(
        QuestListPanel.Position.X + 20f, QuestListPanel.Position.Y + Pad + 50f + index * QuestListRowStep,
        QuestListPanel.Size.X - 40f - ScrollBarGutter, 46f);
    public static Rect2 QuestListName(Rect2 row) => new(
        row.Position.X + 16f, row.Position.Y, row.Size.X - 216f, row.Size.Y);
    public static Rect2 QuestListStars(Rect2 row) => new(
        row.End.X - 196f, row.Position.Y, 180f, row.Size.Y);

    /// <summary>详情：◇任务名◇占标题位，下为客观描述，虚线后接备注（难度/奖励/人数）与传言。</summary>
    public static readonly Rect2 QuestDesc = new(
        QuestDetailPanel.Position.X + Pad, QuestDetailPanel.Position.Y + 66f,
        QuestDetailPanel.Size.X - Pad * 2f, 150f);
    public static float QuestRemarkRuleY => QuestDetailPanel.Position.Y + 226f;
    public static readonly Rect2 QuestRemarkCaption = new(
        QuestDetailPanel.Position.X + Pad, QuestDetailPanel.Position.Y + 232f,
        QuestDetailPanel.Size.X - Pad * 2f, 26f);
    public static Rect2 QuestRemarkRow(int index) => new(
        QuestDetailPanel.Position.X + Pad, QuestDetailPanel.Position.Y + 264f + index * 32f,
        QuestDetailPanel.Size.X - Pad * 2f, 30f);
    public static Rect2 QuestRemarkLabel(Rect2 row) => new(
        row.Position.X, row.Position.Y, 110f, row.Size.Y);
    public static Rect2 QuestRemarkValue(Rect2 row) => new(
        row.Position.X + 118f, row.Position.Y, row.Size.X - 118f, row.Size.Y);

    /// <summary>传言：包直角括号暗色展示，不设标签。</summary>
    public static readonly Rect2 QuestRumor = new(
        QuestDetailPanel.Position.X + Pad, QuestDetailPanel.Position.Y + 364f,
        QuestDetailPanel.Size.X - Pad * 2f, 60f);

    /// <summary>队伍槽位：名签在上，头像在下（尺寸走 AvatarSize）。</summary>
    public static Rect2 QuestPartyHeading => new(
        QuestPartyPanel.Position.X + Pad, QuestPartyPanel.Position.Y + 20f,
        QuestPartyPanel.Size.X - Pad * 2f, 38f);
    public static Rect2 QuestPartySlot(int index, int count)
    {
        const float gap = 12f;
        var w = Mathf.Min(150f, (QuestPartyPanel.Size.X - Pad * 2f - gap * (count - 1)) / Mathf.Max(1, count));
        var total = count * w + (count - 1) * gap;
        return new Rect2(
            QuestPartyPanel.Position.X + Pad + (QuestPartyPanel.Size.X - Pad * 2f - total) / 2f + index * (w + gap),
            QuestPartyPanel.Position.Y + 68f, w, 170f);
    }
    public static Rect2 QuestPartyName(Rect2 slot) => new(
        slot.Position.X - 12f, slot.Position.Y, slot.Size.X + 24f, 24f);

    /// <summary>底部操作行：任务开始、随行人数、清空。</summary>
    public static Rect2 QuestPartyActionRow => new(
        QuestPartyPanel.Position.X + Pad, QuestPartyPanel.Position.Y + 250f,
        QuestPartyPanel.Size.X - Pad * 2f, 34f);
    public static Rect2 QuestPartyStart(Rect2 row) => new(
        row.Position.X, row.Position.Y - 2f, 140f, row.Size.Y + 4f);
    public static Rect2 QuestPartyCount(Rect2 row) => new(
        row.GetCenter().X - 160f, row.Position.Y, 320f, row.Size.Y);
    public static Rect2 QuestPartyClear(Rect2 row) => new(
        row.End.X - 100f, row.Position.Y - 2f, 100f, row.Size.Y + 4f);

}
