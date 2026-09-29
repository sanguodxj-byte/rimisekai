using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 界面版式：所有矩形与坐标的唯一来源。
/// 绘制与命中判定都从这里取，避免两处各算一套而错位。
/// 坐标一律是 1920×1080 画布坐标。
/// </summary>
public static class InkLayout
{
    /// <summary>画布尺寸。与 project.godot 的 stretch 基准一致，改这里要同步改那边。</summary>
    public const float CanvasWidth = 1920f;
    public const float CanvasHeight = 1080f;

    /// <summary>框线到内容区的留白，与 InkFrame 的边框厚度配合。</summary>
    public const float Pad = 30f;

    /// <summary>标题带高度：标题文字与下划线占掉的纵向空间。</summary>
    public const float TitleBand = 58f;

    public const int GridCols = 5;
    public const int GridRows = 5;
    public const int CardsPerPage = 4;

    // ---------- 四块面板 ----------
    // 标准化比例版式：画布 1920x1080（16:9），外边距 x48 / y78，面板间距 24。
    // 上排高 450，下排三块高 400（比上排矮一截，底部留出呼吸空间）。
    // 下排左右对称：角色与行动等宽 760，互为镜像；工作入口竖条 256 居中（正中 x=960）。
    // 上排日志与行动等宽右对齐，地图吃剩余宽度 1040（比日志窄，网格单格 196x78）。

    public static readonly Rect2 MapPanel = new(48, 78, 1040, 450);
    public static readonly Rect2 LogPanel = new(1112, 78, 760, 450);
    public static readonly Rect2 CharPanel = new(48, 552, 760, 400);

    /// <summary>行动面板：与右上日志面板等宽、右对齐，和角色面板互为镜像。</summary>
    public static readonly Rect2 ActPanel = new(1112, 552, 760, 400);

    /// <summary>工作入口面板：夹在角色面板与行动面板之间，居中，与两邻等高。</summary>
    public static readonly Rect2 WorkPanel = new(832, 552, 256, 400);

    /// <summary>工作入口按钮（面板内容区里的一块，点开工作页）。</summary>
    public static readonly Rect2 WorkEntry = new(856, 610, 208, 76);

    /// <summary>地图面板没有标题，内容直接铺满内缩区。</summary>
    public static readonly Rect2 MapContent = MapPanel.Grow(-Pad);

    public static readonly Rect2 LogContent = BelowTitle(LogPanel);
    public static readonly Rect2 CharContent = BelowTitle(CharPanel);
    public static readonly Rect2 ActContent = BelowTitle(ActPanel);

    /// <summary>遮盖层内容区：与地图面板同尺寸，完全盖住地图。</summary>
    public static readonly Rect2 OverlayContent = MapPanel.Grow(-Pad);

    // ---------- 设施交互页（左上角铺开，盖住地图）----------

    /// <summary>
    /// 设施交互页铺在地图面板那块上——点设施行动（如“打开货架”）后，
    /// 左上角从地图网格换成这件设施的操作界面。
    /// </summary>
    public static readonly Rect2 FixturePanel = MapPanel;

    public static readonly Rect2 FixtureContent = BelowTitle(MapPanel);

    /// <summary>设施交互页的行高与行距，与子页面列表同档。</summary>
    public const float FixtureRowHeight = 38f;
    public const float FixtureRowStep = 44f;

    /// <summary>一屏能放下的存储行数。</summary>
    public static int FixtureVisibleRows =>
        Mathf.Max(1, (int)(FixtureContent.Size.Y / FixtureRowStep));

    /// <summary>存储页第 i 行：左侧名称+数量，右侧“放入/取出/过滤”三个小按钮。</summary>
    public static Rect2 StorageRow(int i) => new(
        FixtureContent.Position.X,
        FixtureContent.Position.Y + i * FixtureRowStep,
        FixtureContent.Size.X, FixtureRowHeight);

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

    /// <summary>面板内容区：左右下按 Pad 内缩，上边再让出标题带。</summary>
    private static Rect2 BelowTitle(Rect2 panel) => new(
        panel.Position.X + Pad,
        panel.Position.Y + Pad + TitleBand,
        panel.Size.X - Pad * 2f,
        panel.Size.Y - Pad * 2f - TitleBand);

    /// <summary>面板标题带的文字基线（与 InkFrame.Title 对齐）。</summary>
    public static float TitleTop(Rect2 panel) => panel.Position.Y + 22f;

    /// <summary>左上角标题的可点区域，覆盖标题文字一带。</summary>
    public static readonly Rect2 MapTitleRect = new(56f, 26f, 460f, 46f);

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

    public const float LogLineStep = 34f;
    public const float HereRowHeight = 44f;

    /// <summary>“此处”地名相对内容区顶部的偏移。</summary>
    public const float HereTitleOffset = 152f;

    /// <summary>设施列表区顶（相对内容区）；一列到底放不下就从中间分第二列。</summary>
    public const float FixtureListTop = 176f;
    public const float FixtureColumnGap = 20f;
    public const float FixtureStepMin = 30f;
    public const float FixtureStepMax = 54f;

    public static float FixtureColumnWidth =>
        (LogContent.Size.X - FixtureColumnGap) / 2f;

    /// <summary>
    /// 设施行格子：塞得下就单列（行距在上下限间自动压缩，字体随之缩），
    /// 塞不下才从中间分割出第二列；两列也一样全量摆下，永无“未显示”。
    /// </summary>
    public static Rect2 FixtureCell(int index, int total)
    {
        var avail = LogContent.End.Y - 6f - (LogContent.Position.Y + FixtureListTop);
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

    // ---------- 角色栏 ----------

    public const float CardHeight = 200f;
    public const float CharGap = 12f;

    /// <summary>右侧留给翻页三角的竖条宽度。卡片与按钮都避开它，保证两者同栅格。</summary>
    public const float CharNavWidth = 52f;

    /// <summary>
    /// 角色栏的列宽。卡片与下方页面按钮共用同一套列宽与起点，
    /// 因此按钮一定对齐在头像正下方。
    /// 向下取整：列宽永远是整数，余量留在右侧预留条一侧。
    /// </summary>
    public static float CharColumnWidth(int count)
    {
        var usable = CharContent.Size.X - CharNavWidth;
        return Mathf.FloorToInt((usable - CharGap * (count - 1)) / count);
    }

    private static Rect2 CharColumn(int i, int count, float y, float h) => new(
        CharContent.Position.X + i * (CharColumnWidth(count) + CharGap),
        y, CharColumnWidth(count), h);

    public static Rect2 Card(int slot) =>
        CharColumn(slot, CardsPerPage, CharContent.Position.Y, CardHeight);

    public static Rect2 PageEntry(int i, int count) =>
        CharColumn(i, count, CharContent.End.Y - 46f, 46f);

    /// <summary>翻页三角所在的竖条，位于卡片右侧的预留区内。</summary>
    public static Rect2 PageNext => new(
        CharContent.End.X - CharNavWidth,
        CharContent.Position.Y, CharNavWidth, CardHeight);

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

    public static Rect2 SocialButton(int i, int count = 1) =>
        ActionButton(i, count, ActContent.Position.X + 10f, ActContent.Position.Y + 2f);

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

    /// <summary>聊天层右上角的三个入口（状态/技能/日程）。</summary>
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

    // ---------- 子页面 ----------

    public static readonly Rect2 FullPagePanel = new(48, 48, 1824, 984);
    public static readonly Rect2 FullPageContent = FullPagePanel.Grow(-Pad);
    public static readonly Rect2 PageTitleRect = new(78, 72, 650, 48);
    public static readonly Rect2 PageSubtitleRect = new(1000, 76, 690, 40);
    public static readonly Rect2 SubpageBody = new(78, 174, 1764, 768);
    public static readonly Rect2 PageFooter = new(78, 966, 1764, 36);
    public static readonly Rect2 FullPageClose = new(1722, 76, 120, 44);
    public static readonly Rect2 FullListPanel = new(78, 174, 740, 768);
    public static readonly Rect2 FullListArea = FullListPanel.Grow(-Pad);
    public static readonly Rect2 FullDetailArea = new(842, 174, 1000, 768);
    public static readonly Rect2 PagePanel = new(48, 552, 1824, 450);
    public static readonly Rect2 PageContent = PagePanel.Grow(-Pad);
    public static readonly Rect2 PageClose = FullPageClose;
    public static readonly Rect2 DevListArea = CharContent;
    public static readonly Rect2 DevDetailArea = ActContent;
    public static readonly Rect2 WideListArea = SubpageBody;

    public static Rect2 PageTab(int index, int count) => new(
        FullPageContent.Position.X + index * 138f, 122f, 126f, 36f);

    public static Rect2 WideListColumn(int col) => new(
        SubpageBody.Position.X + col * 894f, SubpageBody.Position.Y, 870f, SubpageBody.Size.Y);

    // ---------- 工作页（全员矩阵）----------

    /// <summary>
    /// 矩阵区域：左侧留出工作类型的名字栏，右侧按角色分列。
    /// 高度要装得下 12 个工作行动（行高 × 12 + 表头），不留大片空白。
    /// </summary>
    public static readonly Rect2 WorkMatrix = new(78, 196, 1764, 606);

    /// <summary>工作类型名栏的宽度（行头）。</summary>
    public const float WorkRowHeaderWidth = 200f;

    /// <summary>矩阵行高与列宽。行高压得紧，12 行才放得下。</summary>
    public const float WorkRowHeight = 44f;
    public const float WorkColumnGap = 8f;

    /// <summary>表头（角色名）那一行的高度。</summary>
    public const float WorkHeaderHeight = 40f;

    /// <summary>单列的最大宽度。角色少时不再把格子拉成巨条。</summary>
    public const float WorkColumnMaxWidth = 168f;

    /// <summary>一列（一个角色）的宽度。角色多时自动收窄，角色少时不超过上限。</summary>
    public static float WorkColumnWidth(int columns)
    {
        var usable = WorkMatrix.Size.X - WorkRowHeaderWidth - WorkColumnGap * (columns - 1);
        return Mathf.Clamp(usable / Mathf.Max(1, columns), 56f, WorkColumnMaxWidth);
    }

    /// <summary>矩阵实际占用的宽度（行头 + 各列 + 列间空隙）。</summary>
    public static float WorkMatrixWidth(int columns) =>
        WorkRowHeaderWidth + columns * WorkColumnWidth(columns) + WorkColumnGap * (columns - 1);

    /// <summary>
    /// 矩阵左边缘。角色少时整块（行头 + 格阵）居中，免得挤在一边。
    /// </summary>
    public static float WorkMatrixLeft(int columns) =>
        WorkMatrix.Position.X + Mathf.Max(0f, (WorkMatrix.Size.X - WorkMatrixWidth(columns)) / 2f);

    /// <summary>第 index 行（工作类型）的整行矩形，含行头。</summary>
    public static Rect2 WorkRow(int index, int columns) => new(
        WorkMatrixLeft(columns),
        WorkMatrix.Position.Y + WorkHeaderHeight + index * WorkRowHeight,
        WorkMatrixWidth(columns), WorkRowHeight);

    /// <summary>第 index 行的行头（工作类型名 + 技能标注）。</summary>
    public static Rect2 WorkRowHeader(int index, int columns)
    {
        var row = WorkRow(index, columns);
        return new Rect2(row.Position.X, row.Position.Y, WorkRowHeaderWidth, row.Size.Y);
    }

    /// <summary>表头里第 col 列的角色名。</summary>
    public static Rect2 WorkColumnHeader(int col, int columns)
    {
        var w = WorkColumnWidth(columns);
        return new Rect2(
            WorkMatrixLeft(columns) + WorkRowHeaderWidth + col * (w + WorkColumnGap),
            WorkMatrix.Position.Y, w, WorkHeaderHeight);
    }

    /// <summary>矩阵第 index 行、第 col 列的格子（点击热区）。</summary>
    public static Rect2 WorkCell(int index, int col, int columns)
    {
        var row = WorkRow(index, columns);
        var w = WorkColumnWidth(columns);
        return new Rect2(
            WorkMatrixLeft(columns) + WorkRowHeaderWidth + col * (w + WorkColumnGap),
            row.Position.Y + 3f, w, row.Size.Y - 6f);
    }

    /// <summary>工作页右栏（选中格的详情与改档按钮），排在矩阵下方。</summary>
    public static readonly Rect2 WorkDetail = new(78, 812, 1764, 130);

    public static Rect2 PanelInner(Rect2 panel) => panel.Grow(-Pad);
    public static Rect2 PanelHeading(Rect2 panel) => new(
        panel.Position.X + Pad, panel.Position.Y + 20f, panel.Size.X - Pad * 2f, 38f);
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
        area.Size.X, PageRowHeight);

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

    // ---------- 角色三页 ----------

    public static readonly Rect2 CharacterRail = new(78, 174, 400, 768);
    public static readonly Rect2 CharacterIdentity = new(108, 204, 340, 76);
    public static readonly Rect2 CharacterPortrait = new(108, 304, 340, 476);
    public static readonly Rect2 CharacterCaption = new(108, 804, 340, 108);
    public static readonly Rect2 CharacterIdentityName = new(124, 214, 308, 34);
    public static readonly Rect2 CharacterIdentityKind = new(124, 252, 308, 22);
    public static readonly Rect2 CharacterBody = new(502, 174, 1340, 768);
    public static readonly Rect2 StatusVitals = new(502, 174, 580, 358);
    public static readonly Rect2 StatusCombat = new(1106, 174, 736, 358);
    public static readonly Rect2 StatusRelations = new(502, 556, 1340, 386);
    public static readonly Rect2 ScheduleDetail = new(502, 354, 1340, 588);
    public static readonly Rect2 ScheduleNote = new(532, 442, 1280, 112);

    public static Rect2 CharacterCaptionRow(int index) => new(124, 816 + index * 40f, 308, 36);
    public static Rect2 StatusRowLabel(Rect2 row) => new(row.Position.X, row.Position.Y, 76f, row.Size.Y);
    public static Rect2 StatusRowValue(Rect2 row) => new(row.End.X - 158f, row.Position.Y, 158f, row.Size.Y);
    public static Rect2 StatusRowMeter(Rect2 row) => new(row.Position.X + 88f, row.GetCenter().Y - 4f,
        Mathf.Max(12f, row.Size.X - 258f), 8f);
    public static Rect2 CombatStatCell(int index)
    {
        var w = (StatusCombat.Size.X - Pad * 2f - 20f) / 2f;
        return new Rect2(StatusCombat.Position.X + Pad + index % 2 * (w + 20f),
            StatusCombat.Position.Y + 88f + index / 2 * 78f, w, 64f);
    }
    public static Rect2 SkillColumn(int col) => new(502f + col * (1292f / 3f + 24f), 174f, 1292f / 3f, 768f);
    public static Rect2 SkillRowName(Rect2 row) => new(row.Position.X, row.Position.Y, row.Size.X * 0.58f, 32f);
    public static Rect2 SkillRowValue(Rect2 row) => new(row.Position.X + row.Size.X * 0.60f, row.Position.Y,
        row.Size.X * 0.40f, 32f);
    public static Rect2 SkillRowNote(Rect2 row) => new(row.Position.X, row.Position.Y + 34f, row.Size.X, 24f);
    public static Rect2 ScheduleSlot(int slot) => new(502f + slot * 341f, 174f, 317f, 156f);
    public static Rect2 ScheduleSlotName(Rect2 row) => new(row.Position.X + 24f, row.Position.Y + 20f, row.Size.X - 48f, 28f);
    public static Rect2 ScheduleSlotValue(Rect2 row) => new(row.Position.X + 24f, row.Position.Y + 58f, row.Size.X - 48f, 36f);
    public static Rect2 ScheduleSlotNote(Rect2 row) => new(row.Position.X + 24f, row.Position.Y + 108f, row.Size.X - 48f, 24f);
    public static Rect2 CharacterDetailButton(int slot, int count) => new(
        532f + slot % 5 * 258f, 594f + slot / 5 * 100f, 246f, 76f);

    // ---------- 系统与行旅页面 ----------

    public static readonly Rect2 SystemSidebar = CharacterRail;
    public static readonly Rect2 SystemContent = CharacterBody;
    public static Rect2 SystemSidebarEntry(int index) => new(108f, 204f + index * 64f, 340f, 48f);
    public static Rect2 SystemRow(int index) => new(532f, 204f + index * 76f, 1280f, 60f);
    public static Rect2 SystemOption(Rect2 row, int index, int count)
    {
        var w = (row.Size.X * 0.62f - 12f * (count - 1)) / Mathf.Max(1, count);
        return new Rect2(row.End.X - row.Size.X * 0.62f + index * (w + 12f), row.Position.Y + 6f, w, 48f);
    }
    public static readonly Rect2 JourneyMap = new(78, 174, 1060, 600);
    public static readonly Rect2 JourneyList = new(1162, 174, 680, 600);
    public static readonly Rect2 JourneyDetail = new(78, 798, 1764, 144);
    public static Rect2 JourneyCell(int col, int row) => new(108f + col * 200f, 262f + row * 92f, 200f, 92f);
    public static Rect2 JourneyRow(int index) => new(1192f, 250f + index * 56f, 620f, 48f);
    public static Rect2 JourneyAction(int slot, int count) => new(1842f - Pad - count * 172f + slot * 172f, 844f, 156f, 52f);
    public static Rect2 JourneyTab(int index) => PageTab(index, 3);
    public static readonly Rect2 PageHeaderRule = new(78, 160, 1764, 0f);
    public static readonly Rect2 HubSystemEntry = new(1742, 968, 130, 42);
    public static readonly Rect2 HubQuestEntry = new(1598, 968, 130, 42);
    public static readonly Rect2 HubWorldEntry = new(1454, 968, 130, 42);

    /// <summary>开发模式：非空房间格右上角的拆除 X（点击热区，复用地图格）。</summary>
    public static Rect2 DevRoomX(Rect2 cell) => new(
        cell.End.X - 24f, cell.Position.Y + 4f, 20f, 20f);

    /// <summary>开发模式：右上面板的设施行区（日志面板内容区）。</summary>
    public static Rect2 DevFacilityArea => LogContent;

    /// <summary>开发模式：左下面板的房间行区（角色面板内容区）。</summary>
    public static Rect2 DevRoomArea => CharContent;

    /// <summary>开发模式：右上面板的设施行（日志面板内容区）。</summary>
    public static Rect2 DevFacilityRow(int j) => new(
        LogContent.Position.X, LogContent.Position.Y + 44f + j * PageRowStep,
        LogContent.Size.X, PageRowHeight);

    /// <summary>开发模式：左下面板能放下的库存行数。</summary>
    public static int DevFacilityVisibleRows =>
        Mathf.Max(1, (int)((CharContent.Size.Y - 44f) / PageRowStep));

    /// <summary>开发模式：右上面板已有设施行的拆除 X（行右端，点击热区）。</summary>
    public static Rect2 DevOwnedX(Rect2 row) => new(
        row.End.X - 24f, row.Position.Y + 10f, 20f, 20f);

    /// <summary>开发模式：右下面板的建造行。</summary>
    public static Rect2 DevBuildRow(int j) => new(
        ActContent.Position.X, ActContent.Position.Y + 44f + j * PageRowStep,
        ActContent.Size.X - 160f, PageRowHeight);

    /// <summary>开发模式：右下面板能放下的建造行数（给退出按钮留出右上角）。</summary>
    public static int DevBuildVisibleRows =>
        Mathf.Max(1, (int)((ActContent.Size.Y - 44f) / PageRowStep));

    /// <summary>开发模式：左下面板的房间行。</summary>
    public static Rect2 DevRoomRow(int j) => new(
        CharContent.Position.X, CharContent.Position.Y + 44f + j * PageRowStep,
        CharContent.Size.X, PageRowHeight);

    /// <summary>开发模式：左下面板能放下的房间行数。</summary>
    public static int DevRoomVisibleRows =>
        Mathf.Max(1, (int)((CharContent.Size.Y - 44f) / PageRowStep));

    /// <summary>开发模式：右下面板右上角的退出开发按钮。</summary>
    public static Rect2 DevExitButton(Rect2 pane) => new(
        pane.End.X - 150f, pane.Position.Y + 4f, 140f, 40f);

    // ---------- 改名弹窗 ----------

    /// <summary>改名弹窗：居中一块小面板。高度要容下标题、输入框、提示与按钮。</summary>
    public static readonly Rect2 RenamePanel = new(550, 360, 820, 340);
    public static readonly Rect2 RenameTitle = new(580, 388, 760, 40);
    public static readonly Rect2 RenameHint = new(580, 438, 760, 28);
    public static readonly Rect2 RenameField = new(580, 486, 760, 52);

    /// <summary>改名输入框允许的最大字符数。</summary>
    public const int RenameMaxChars = 12;

    public static Rect2 RenameButton(int slot)
    {
        const float w = 160f;
        const float gap = 20f;
        var total = w * 2f + gap;
        var x = RenamePanel.GetCenter().X - total / 2f + slot * (w + gap);
        return new Rect2(x, RenamePanel.End.Y - Pad - 52f, w, 46f);
    }
}
