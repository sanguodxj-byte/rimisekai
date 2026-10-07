using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 界面版式：所有矩形与坐标的唯一来源。
/// 绘制与命中判定都从这里取，避免两处各算一套而错位。
/// 坐标一律是 1920×1080 画布坐标。
/// </summary>
public static partial class InkLayout
{
    /// <summary>画布尺寸。与 project.godot 的 stretch 基准一致，改这里要同步改那边。</summary>
    public const float CanvasWidth = 1920f;
    public const float CanvasHeight = 1080f;

    /// <summary>框线到内容区的留白，与 InkFrame 的边框厚度配合。</summary>
    public const float Pad = 30f;

    /// <summary>标题带高度：标题文字与下划线占掉的纵向空间。</summary>
    public const float TitleBand = 58f;

    /// <summary>
    /// 标题的左右内缩（相对内容区左/右缘）。
    /// **面板标题与面板内的分节标题共用这一套**，禁止在调用点现编魔数——
    /// 2026-10-01 主人指出「日志标题与地名行大小位置都不对齐」，根因就是地名行手写了 6。
    /// </summary>
    public const float TitleInset = 12f;

    /// <summary>标题下饰线相对文字顶的纵向偏移（面板标题与分节标题同用）。</summary>
    public const float TitleRuleGap = 34f;

    /// <summary>标题默认字号（面板标题与分节标题同用）。</summary>
    public const int TitleFontSize = 26;

    public const int GridCols = 5;
    public const int GridRows = 5;
    public const int CardsPerPage = 4;

    /// <summary>除玩家外，每页能排的角色数（左 1 固定给玩家本人）。</summary>
    public const int OthersPerPage = CardsPerPage - 1;

    // ---------- 四块面板 ----------
    // 标准化比例版式：画布 1920x1080（16:9），外边距 x48 / y78，面板间距 24。
    // 上排高 540（地图 960x540 = 16:9 插画位），下排三块压到 360，底缘 1002 留出按钮行。
    // 下排左右对称：角色与行动等宽 760，互为镜像；工作入口竖条 256 居中（正中 x=960）。
    // 上排日志与行动等宽右对齐，地图吃剩余宽度 1040（比日志窄，网格单格 196x78）。

    // 左上网格块按 16:9（插画位）：800x450，插画按 1920x1080 或 1600x900 绘制可无损铺满。
    public static readonly Rect2 MapPanel = new(48, 78, 960, 540);
    public static readonly Rect2 LogPanel = new(1032, 78, 840, 540);
    public static readonly Rect2 CharPanel = new(48, 642, 760, 360);

    /// <summary>行动面板：与右上日志面板等宽、右对齐，和角色面板互为镜像。</summary>
    public static readonly Rect2 ActPanel = new(1112, 642, 760, 360);

    /// <summary>工作入口面板：夹在角色面板与行动面板之间，居中，与两邻等高。</summary>
    public static readonly Rect2 WorkPanel = new(832, 642, 256, 360);


    // ---------- 合并区（2026-10-01 主人定） ----------
    // 面板不是各自独立的视觉元素：上区（地图＋日志）合成一个大框，
    // 下区（角色＋操作＋行动）合成另一个大框。厚白雕花框只画在这两个大框上，
    // 区内各面板之间用竖分隔槽划分，不再各画一圈外框。

    /// <summary>上区：地图与日志合并，外框一圈，内部以竖槽分隔。</summary>
    public static readonly Rect2 UpperZone = new(
        48, 78, LogPanel.End.X - MapPanel.Position.X, MapPanel.Size.Y);

    /// <summary>下区：角色、操作、行动三块合并，外框一圈，内部以两道竖槽分隔。</summary>
    public static readonly Rect2 LowerZone = new(
        48, 642, ActPanel.End.X - CharPanel.Position.X, CharPanel.Size.Y);

    /// <summary>上区内部竖分隔线的 x（地图与日志之间的缝隙正中）。</summary>
    public static float UpperSplitX => (MapPanel.End.X + LogPanel.Position.X) * 0.5f;

    /// <summary>下区内部两道竖分隔线的 x（角色｜操作｜行动之间的缝隙正中）。</summary>
    public static float LowerSplitLeftX => (CharPanel.End.X + WorkPanel.Position.X) * 0.5f;

    public static float LowerSplitRightX => (WorkPanel.End.X + ActPanel.Position.X) * 0.5f;

    /// <summary>工作入口按钮（面板无标题带，直接从内容区上缘起）。</summary>
    public static readonly Rect2 WorkEntry = new(856, 672, 208, 76);

    /// <summary>地图面板没有标题，内容直接铺满内缩区。</summary>
    public static readonly Rect2 MapContent = MapPanel.Grow(-Pad);

    public static readonly Rect2 LogContent = BelowTitle(LogPanel);
    public static readonly Rect2 CharContent = BelowTitle(CharPanel);
    public static readonly Rect2 ActContent = BelowTitle(ActPanel);

    /// <summary>遮盖层内容区：与地图面板同尺寸，完全盖住地图。</summary>
    public static readonly Rect2 OverlayContent = MapPanel.Grow(-Pad);

    // ---------- 角色标识（地图格与设施行共用同一套尺寸） ----------

    /// <summary>角色棋子高度。地图格与设施行的标识等大，取两方较大值。</summary>
    public const float OccupantFigureHeight = 26f;

    /// <summary>
    /// 同一处多个角色标识的中心间距。设施行几个标识相互靠近、密集排
    /// （棋子底座宽为高的 0.5，23px 中心距下相邻底座留约 10px 空隙，密而不叠）。
    /// </summary>
    public const float OccupantStep = 23f;

    /// <summary>一屏最多画的角色标识个数，超出在右下角标余数。</summary>
    public const int OccupantMaxShown = 8;

    /// <summary>面板内容区：左右下按 Pad 内缩，上边再让出标题带。</summary>
    private static Rect2 BelowTitle(Rect2 panel) => new(
        panel.Position.X + Pad,
        panel.Position.Y + Pad + TitleBand,
        panel.Size.X - Pad * 2f,
        panel.Size.Y - Pad * 2f - TitleBand);

    /// <summary>面板标题带的文字基线（与 InkFrame.Title 对齐）。</summary>
    public static float TitleTop(Rect2 panel) => panel.Position.Y + 22f;

    // ---------- 头像 ----------

    /// <summary>
    /// 头像统一尺寸，标准比例 1:1。一切出现头像的位置（据点角色卡、任务队伍槽等）
    /// 都从这里取，禁止各处自行推算；面板几何放不下时改面板，不改头像。
    /// </summary>
    public static readonly Vector2 AvatarSize = new(144f, 144f);

    /// <summary>在给定区域里取一个 AvatarSize 的头像框：水平居中、底边贴区底。</summary>
    public static Rect2 AvatarBox(Rect2 area) => new(
        area.GetCenter().X - AvatarSize.X / 2f, area.End.Y - AvatarSize.Y,
        AvatarSize.X, AvatarSize.Y);

    // ---------- 滚动条（大列表滑条）----------

    /// <summary>列表右缘为滑条让出的宽度（行缩短这么多，轨道排在这条空隙里）。</summary>
    public const float ScrollBarGutter = 22f;

    /// <summary>
    /// 竖向滑条轨道：贴在行内容区右缘外那条空隙里（行已按 <see cref="ScrollBarGutter"/> 缩窄），
    /// 因此永远不压行内文字与按钮。
    /// </summary>
    public static Rect2 ScrollBarRect(Rect2 panel, Rect2 rowsArea) => new(
        rowsArea.End.X + 6f, rowsArea.Position.Y, 9f, rowsArea.Size.Y);

    /// <summary>滑块被拖拽时，按鼠标 y 反推行号。返回 0..1 的比率。</summary>
    public static float ScrollRatioAt(Rect2 track, float mouseY, float thumbRatio)
    {
        var thumbH = Mathf.Clamp(track.Size.Y * Mathf.Clamp(thumbRatio, 0f, 1f), 28f, track.Size.Y);
        var max = Mathf.Max(1f, track.Size.Y - thumbH);
        return Mathf.Clamp((mouseY - track.Position.Y - thumbH / 2f) / max, 0f, 1f);
    }

    public static Rect2 PanelInner(Rect2 panel) => panel.Grow(-Pad);
    public static Rect2 PanelText(Rect2 panel) => BelowTitle(panel);
    public static Rect2 SectionRow(Rect2 panel, int index, float step = 54f, float height = 44f) => new(
        panel.Position.X + Pad, panel.Position.Y + Pad + TitleBand + index * step,
        panel.Size.X - Pad * 2f, height);

    public const float PageRowHeight = 58f;
    public const float PageRowStep = 64f;
    public const float PageControlsHeight = 108f;

    public static int ListVisibleRows(Rect2 area) =>
        Mathf.Max(1, (int)((area.Size.Y - PageControlsHeight - 56f) / PageRowStep));

    public static Rect2 ListRow(Rect2 area, int i) => new(
        area.Position.X, area.Position.Y + PageControlsHeight + i * PageRowStep,
        area.Size.X - ScrollBarGutter, PageRowHeight);

    public static Rect2 SearchBox(Rect2 listArea) => new(
        listArea.Position.X, listArea.Position.Y, listArea.Size.X - 140f, 44f);
    public static Rect2 SortButton(Rect2 listArea) => new(
        listArea.End.X - 128f, listArea.Position.Y, 128f, 44f);
    public static Rect2 FilterChip(Rect2 listArea, int i, int count) => new(
        listArea.Position.X + i * 112f, listArea.Position.Y + 56f, 100f, 36f);
    public static Rect2 ListRowName(Rect2 row, bool note) => new(
        row.Position.X + 16f, row.Position.Y + (note ? 4f : 0f),
        row.Size.X * 0.68f - 24f, note ? 28f : row.Size.Y);
    public static Rect2 ListRowValue(Rect2 row) => new(
        row.Position.X + row.Size.X * 0.68f, row.Position.Y,
        row.Size.X * 0.32f - 16f, row.Size.Y);
    public static Rect2 ListRowNote(Rect2 row) => new(
        row.Position.X + 16f, row.Position.Y + 32f, row.Size.X * 0.68f - 24f, 22f);

    public static Rect2 PagePager(Rect2 area, int direction) => new(
        direction < 0 ? area.Position.X : area.End.X - 52f, area.End.Y - 44f, 52f, 44f);
    public static Rect2 PageCounter(Rect2 area) => new(
        area.Position.X + 64f, area.End.Y - 44f, area.Size.X - 128f, 44f);
    public static Rect2 DetailTitle(Rect2 pane) => new(
        pane.Position.X + Pad, pane.Position.Y + Pad, pane.Size.X - Pad * 2f, 52f);
    public static Rect2 DetailNote(Rect2 pane) => new(
        pane.Position.X + Pad, pane.Position.Y + 114f, pane.Size.X - Pad * 2f,
        Mathf.Max(0f, pane.Size.Y - 244f));
    public static Rect2 DetailNoteRow(Rect2 pane, int index) => new(
        pane.Position.X + Pad, pane.Position.Y + 126f + index * 58f,
        pane.Size.X - Pad * 2f, 48f);
    public static Rect2 DetailButton(Rect2 pane, int slot, int count)
    {
        const float gap = 16f;
        var w = Mathf.Min(240f, (pane.Size.X - Pad * 2f - gap * (count - 1)) / Mathf.Max(1, count));
        var total = count * w + (count - 1) * gap;
        return new Rect2(pane.GetCenter().X - total / 2f + slot * (w + gap),
            pane.End.Y - Pad - 52f, w, 52f);
    }

    public static Rect2 DetailButtonGrid(Rect2 pane, int slot, int count)
    {
        const float gap = 16f;
        var cols = Mathf.Max(1, (int)((pane.Size.X - Pad * 2f + gap) / 216f));
        var rows = Mathf.CeilToInt(count / (float)cols);
        var w = (pane.Size.X - Pad * 2f - (cols - 1) * gap) / cols;
        return new Rect2(pane.Position.X + Pad + slot % cols * (w + gap),
            pane.End.Y - Pad - rows * 64f + slot / cols * 64f, w, 48f);
    }
}
