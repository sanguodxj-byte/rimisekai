using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 全屏子页通用版式：木框外框、页签带、关闭钮、左列表 + 右详情的通用列表板式。
/// 库存 / 交易 / 制作 / 开发 / 角色三页 / 日程 共用这一套骨架。
/// </summary>
public static partial class InkLayout
{
    // ---------- 子页面 ----------

    /// <summary>
    /// 全屏子页的通用骨架。木框外框 48..1032，内容区一律 78..1842 / 142..1002——
    /// 与技能页同一套外沿（星盘 860 高铺到底），底部不留空带。
    /// </summary>
    public const float FullPageTop = 142f;
    public const float FullPageBottom = 1002f;
    public const float FullPageHeight = FullPageBottom - FullPageTop;

    public static readonly Rect2 FullPagePanel = new(48, 48, 1824, 984);

    public static readonly Rect2 FullPageClose = new(1722, 74, 120, 42);
    public static readonly Rect2 FullPageContent = FullPagePanel.Grow(-Pad);
    public static readonly Rect2 PageTitleRect = new(78, 72, 650, 48);
    public static readonly Rect2 PageSubtitleRect = new(1000, 75, 690, 40);
    public static readonly Rect2 SubpageBody = new(78, FullPageTop, 1764, FullPageHeight);
    public static readonly Rect2 PageFooter = new(78, 966, 1764, 36);
    public static readonly Rect2 FullListPanel = new(78, FullPageTop, 740, FullPageHeight);
    public static readonly Rect2 FullListArea = FullListPanel.Grow(-Pad);
    public static readonly Rect2 FullDetailArea = new(842, FullPageTop, 1000, FullPageHeight);
    public static readonly Rect2 PagePanel = new(48, 552, 1824, 450);
    public static readonly Rect2 PageContent = PagePanel.Grow(-Pad);
    public static readonly Rect2 PageClose = FullPageClose;
    public static readonly Rect2 WideListArea = SubpageBody;

    public static Rect2 PageTab(int index, int count) => new(
        FullPageContent.Position.X + index * 150f, 74f, 138f, 42f);

    public static Rect2 WideListColumn(int col) => new(
        SubpageBody.Position.X + col * 894f, SubpageBody.Position.Y, 870f, SubpageBody.Size.Y);

}
