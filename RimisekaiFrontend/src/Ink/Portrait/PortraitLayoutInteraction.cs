using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    // ---------- 对话（地图网格区变为对话框）----------
    //
    // 主人 2026-10-05 定：对话时不再整屏铺开，只把**地图网格区**换成对话框，
    // 下方的角色头像带与设施栏仍常显。对话框内部自上而下：页面入口行 / 说话人 / 正文 / 选项。

    public static Rect2 InteractionList => PageListArea(OverlayRows);

    /// <summary>对话框＝地图网格区（0,120,1080,1080），与网格同一 footprint。</summary>
    public static Rect2 ConversationBox => GridArea;

    /// <summary>页面入口行（状态 / 技能 / 日程），对话框顶部一行三格。</summary>
    public static Rect2 ConversationEntry(int index) => new(
        Pad + index * ((CanvasWidth - Pad * 2f) / 3f), ConversationBox.Position.Y + Pad,
        (CanvasWidth - Pad * 2f) / 3f, RowHeight);

    public static Rect2 ConversationName => new(
        Pad, ConversationEntry(0).End.Y + 24f, CanvasWidth - Pad * 2f, 96f);

    /// <summary>正文区：高 408＝5 行（行高 80），放不下走滑条。</summary>
    public static Rect2 ConversationText => new(
        Pad, ConversationName.End.Y + 12f, CanvasWidth - Pad * 2f, 408f);

    /// <summary>选项区：最多三行。</summary>
    public static Rect2 ConversationChoices => new(
        Pad, ConversationText.End.Y + 12f, CanvasWidth - Pad * 2f, RowHeight * 3f);

    /// <summary>
    /// 无选项时「点击继续」的命中块：入口行以下的整块（含说话人、正文、选项区），
    /// 右侧让出滑条列避免与滚动条抢命中。高度远大于触控下限。
    /// </summary>
    public static Rect2 ConversationAdvance => new(
        Pad, ConversationName.Position.Y, CanvasWidth - Pad * 2f - TouchMin - 16f,
        ConversationBox.End.Y - ConversationName.Position.Y);

    /// <summary>继续提示：纯实心向下三角，落在对话框底部中央。</summary>
    public static Vector2[] ConversationArrow
    {
        get
        {
            var center = new Vector2(ConversationBox.GetCenter().X, ConversationChoices.End.Y - 40f);
            return new[]
            {
                center + new Vector2(-22f, -16f), center + new Vector2(22f, -16f), center + new Vector2(0f, 16f),
            };
        }
    }

    public const int ConversationVisibleLines = 5;
    public const int ConversationVisibleChoices = 3;
}
