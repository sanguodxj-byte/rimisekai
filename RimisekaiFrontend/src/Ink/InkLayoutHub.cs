using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 据点主界面版式：右侧行动区（交流 / 设施行动 / 房间行动）、
/// 聊天层遮盖层（立绘 / 量表 / 正文 / 分支选项）与顶栏右侧四项固定格子。
/// </summary>
public static partial class InkLayout
{
    /// <summary>左上角标题的可点区域，覆盖标题文字一带。</summary>
    public static readonly Rect2 MapTitleRect = new(56f, 26f, 460f, 46f);

    // ---------- 行动区 ----------

    public const float ActionButtonWidth = 210f;
    public const float ActionButtonHeight = 44f;
    public const float ActionButtonStep = 56f;

    /// <summary>
    /// 行动区按钮栅格。按钮数量不定（交流可能很多项），
    /// 按可用高度算出列数后**均分列高**：各列行数相差不超过一，
    /// 9 个按钮排成 3×3，而不是 4+4+1 的孤尾列。
    /// </summary>
    public static Rect2 ActionButton(int index, int count, float originX, float originY)
    {
        var maxRows = Mathf.Max(1, (int)((ActContent.End.Y - originY - 6f) / ActionButtonStep));
        var cols = Mathf.Max(1, (count + maxRows - 1) / maxRows);

        // 均分：前 extra 列各多一行，列高差不超过一行。
        var baseRows = count / cols;
        var extra = count % cols;

        var col = 0;
        var row = index;
        for (var c = 0; c < cols; c++)
        {
            var rowsInCol = baseRows + (c < extra ? 1 : 0);
            if (row < rowsInCol)
            {
                col = c;
                break;
            }
            row -= rowsInCol;
        }

        return new Rect2(
            originX + col * (ActionButtonWidth + 14f),
            originY + row * ActionButtonStep,
            ActionButtonWidth, ActionButtonHeight);
    }

    /// <summary>
    /// 交流面板 4x3 网格：竖排优先，一列满四行才向右开新列；
    /// 第 0 列是起始钮（交谈/接触/观察/离开），点开的类别子项从第 1 列起排。
    /// 单元格沿用原行动按钮几何，长宽不再另立。
    /// </summary>
    public static Rect2 SocialGridButton(int col, int row) => new(
        ActContent.Position.X + col * (ActionButtonWidth + 14f),
        ActContent.Position.Y + 2f + row * ActionButtonStep,
        ActionButtonWidth, ActionButtonHeight);

    /// <summary>
    /// 行动按钮。与交流互斥显示，因此不再有分隔线，直接从左起排。
    /// </summary>
    public static Rect2 PlaceButton(int i, int count = 1) =>
        ActionButton(i, count, ActContent.Position.X + 10f, ActContent.Position.Y + 2f);

    // ---------- 遮盖层 ----------

    /// <summary>聊天层左侧立绘区。</summary>
    public static readonly Rect2 ChatPortrait = new(108f, 204f, 192f, 200f);

    public static readonly Rect2 ChatMeters = new(108f, 420f, 192f, 58f);

    public static readonly Rect2 ChatBody = new(328f, 204f, 730f, 236f);
    public static readonly Rect2 ChatHeaderRect = new(328f, 128f, 470f, 44f);
    public static readonly Rect2 ChatContinueRect = new(786f, 456f, 272f, 30f);
    public static Rect2 ChatMeterRow(int index) => new(116f, 423f + index * 27f, 176f, 24f);
    public static Rect2 ChatMeterLabel(Rect2 row) => new(row.Position, new Vector2(38f, row.Size.Y));
    public static Rect2 ChatMeterValue(Rect2 row) => new(row.End.X - 46f, row.Position.Y, 46f, row.Size.Y);
    public static Rect2 ChatMeterBar(Rect2 row) => new(row.Position.X + 42f, row.GetCenter().Y - 3f,
        row.Size.X - 92f, 6f);
    public static Rect2 ChatTextRect(bool hasChoices, int choiceCount) => new(
        ChatBody.Position, new Vector2(ChatBody.Size.X, hasChoices
            ? Mathf.Max(0f, OverlayChoice(0, choiceCount).Position.Y - ChatBody.Position.Y - 12f)
            : ChatBody.Size.Y));
    public static Rect2 StoryTextRect(int choices) => new(108f, 184f, 980f,
        choices > 0 ? Mathf.Max(0f, OverlayChoice(0, choices).Position.Y - 196f) : 264f);
    public static readonly Rect2 StorySpeakerRect = new(108f, 126f, 660f, 40f);

    /// <summary>聊天层右上角的入口（状态/能力/技能/日程）。</summary>
    // ---------- 顶栏右侧状态项（四项位置固定） ----------

    /// <summary>状态项字号（统一提升至 26）。</summary>
    public const float HeaderLabelSize = 26f;
    public const float HeaderValueSize = 26f;

    /// <summary>
    /// 日志面板标题行右侧同级的四个状态项格子（移入日志面板内，和日志标题同级）。
    /// 从右往左：金钱 / 时刻 / 天气 / 季节。基线与“日志”标题对齐（Y = LogPanel.Position.Y + 22f）。
    /// </summary>
    public static Rect2 HeaderSlotMoney => new(1700f, LogPanel.Position.Y + 22f, 130f, 32f);
    public static Rect2 HeaderSlotTime => new(1560f, LogPanel.Position.Y + 22f, 120f, 32f);
    public static Rect2 HeaderSlotWeather => new(1470f, LogPanel.Position.Y + 22f, 70f, 32f);
    public static Rect2 HeaderSlotSeason => new(1380f, LogPanel.Position.Y + 22f, 70f, 32f);

    /// <summary>四项固定格子，按绘制顺序（季节 → 天气 → 时刻 → 金钱）。</summary>
    public static Rect2 HeaderSlot(int index) => index switch
    {
        0 => HeaderSlotSeason,
        1 => HeaderSlotWeather,
        2 => HeaderSlotTime,
        3 => HeaderSlotMoney,
        _ => HeaderSlotSeason,
    };

    /// <summary>行动面板右上角“设置”按钮（对齐标题行最右缘，字号与“行动”等大=26）。</summary>
    public static Rect2 ActPanelSettingsButton => new(
        ActPanel.End.X - Pad - 90f, ActPanel.Position.Y + 18f, 90f, 38f);

    /// <summary>状态项个数（季节 / 天气 / 时刻 / 金钱）。</summary>
    public const int HeaderItemCount = 4;

    public static Rect2 ChatEntry(int index, int count)
    {
        const float w = 84f;
        const float gap = 10f;
        var total = w * count + gap * (count - 1);
        var x = MapPanel.End.X - Pad - total + index * (w + gap);
        return new Rect2(x, MapPanel.Position.Y + Pad + 12f, w, 34f);
    }

    public static Rect2 OverlayChoice(int index, int count) => new(
        MapPanel.Position.X + 120f,
        MapPanel.End.Y - 40f - (count - index) * 52f,
        MapPanel.Size.X - 240f, 44f);

}
