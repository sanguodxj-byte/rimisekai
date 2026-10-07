using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 角色版式：据点角色栏（卡片与翻页钮）与角色全屏页
/// （状态 / 技能 / 日程 三页共用的轨道、主体、状态三列、日程时段卡与下半房间网格）。
/// </summary>
public static partial class InkLayout
{
    // ---------- 角色栏 ----------

    public const float CardHeight = 224f;
    public const float CharGap = 12f;

    /// <summary>
    /// 角色栏的列宽。卡片与下方页面按钮共用同一套列宽与起点，
    /// 因此按钮一定对齐在头像正下方。
    /// 向下取整：列宽永远是整数，余量留在右侧预留条一侧。
    /// </summary>
    public static float CharColumnWidth(int count)
    {
        var usable = CharContent.Size.X;
        return Mathf.FloorToInt((usable - CharGap * (count - 1)) / count);
    }

    /// <summary>
    /// 角色栏翻页钮：面板顶居中一排。矮钮（26px 高），里面放几何画的大箭头
    /// （箭头比原来的字符箭头大得多，且不依赖字体字形）。
    /// 走属性而非字段：依赖 <see cref="CharPanel"/>（在 InkLayout.cs），
    /// 拆成多文件后静态字段的初始化顺序不再由本文件决定，必须延迟求值。
    /// </summary>
    public static Rect2 CharPagerPrev => new(
        CharPanel.GetCenter().X - 78f, CharPanel.Position.Y + 26f, 72f, 26f);
    public static Rect2 CharPagerNext => new(
        CharPanel.GetCenter().X + 6f, CharPanel.Position.Y + 26f, 72f, 26f);

    /// <summary>角色面板右上角“状态”入口按钮（对齐标题行右侧，尺寸与翻页钮齐平）。</summary>
    public static Rect2 CharPanelStatusButton => new(
        CharPanel.End.X - Pad - 72f - 8f - 72f, CharPanel.Position.Y + 26f, 72f, 26f);

    /// <summary>角色面板右上角“技能”入口按钮（对齐标题行最右缘）。</summary>
    public static Rect2 CharPanelSkillButton => new(
        CharPanel.End.X - Pad - 72f, CharPanel.Position.Y + 26f, 72f, 26f);

    /// <summary>
    /// 翻页钮内实心三角形的半高（总高 20，收在 26 高的钮里，四周留匀称空隙）。
    /// </summary>
    public const float CharPagerArrowHalf = 10f;

    private static Rect2 CharColumn(int i, int count, float y, float h) => new(
        CharContent.Position.X + i * (CharColumnWidth(count) + CharGap),
        y, CharColumnWidth(count), h);

    public static Rect2 Card(int slot) =>
        CharColumn(slot, CardsPerPage, CharContent.Position.Y, CardHeight);

    /// <summary>
    /// 中间面板里的页面入口（库存/交易/制作/开发）。
    /// 竖排在工作按钮下方，与工作按钮同宽，视觉上归成一组。
    /// </summary>
    public static Rect2 PageEntry(int index) => new(
        WorkEntry.Position.X, WorkEntry.End.Y + 16f + index * 54f,
        WorkEntry.Size.X, 46f);

    /// <summary>角色卡里头像下方的体力条（名字带上方）。</summary>
    public static Rect2 CardMeter(Rect2 card) => new(
        card.Position.X + 16f, card.End.Y - 50f, card.Size.X - 32f, 8f);

    // ---------- 日程页：左侧全员成员列表 + 右侧日程操作 ----------

    /// <summary>日程页左侧成员列表面板：纵向贯通（142..1002），包含玩家与所有同伴。</summary>
    public static readonly Rect2 ScheduleMemberList = new(78, FullPageTop, 320, FullPageHeight);

    public const float ScheduleMemberRowStep = 64f;

    /// <summary>日程页左侧成员行矩形。</summary>
    public static Rect2 ScheduleMemberRow(Rect2 panel, int index) => new(
        panel.Position.X + Pad,
        panel.Position.Y + Pad + index * ScheduleMemberRowStep,
        panel.Size.X - Pad * 2f - ScrollBarGutter, 56f);

    /// <summary>日程页左侧成员列表一屏能放的行数（滑条用）。</summary>
    public static int ScheduleMemberVisibleRows
    {
        get
        {
            var inner = ScheduleMemberList.Size.Y - Pad * 2f;
            return Mathf.Max(1, (int)(inner / ScheduleMemberRowStep));
        }
    }

    /// <summary>成员行的姓名区（左对齐）。</summary>
    public static Rect2 ScheduleMemberName(Rect2 row) => new(
        row.Position.X + 16f, row.Position.Y, row.Size.X - 118f, row.Size.Y);

    /// <summary>成员行的工种区（右对齐）。</summary>
    public static Rect2 ScheduleMemberJob(Rect2 row) => new(
        row.End.X - 98f, row.Position.Y, 82f, row.Size.Y);

    /// <summary>右侧房间网格面板的区域（紧随时段卡下方，与设施列表等高）。</summary>
    public static readonly Rect2 ScheduleGrid = new(422, 274, 694, 460);

    /// <summary>右侧设施列表面板的区域（紧随时段卡下方，与房间网格等高）。</summary>
    public static readonly Rect2 ScheduleFacilities = new(1132, 274, 710, 460);

    /// <summary>设施列表行的固定步距。</summary>
    public const float ScheduleFacilityStep = 52f;

    /// <summary>网格内一格：按 5×5 排，扣掉面板留白（无标题带）。</summary>
    public static Rect2 ScheduleCell(Rect2 grid, int col, int row)
    {
        var inner = new Rect2(
            grid.Position.X + Pad,
            grid.Position.Y + Pad,
            grid.Size.X - Pad * 2f,
            grid.Size.Y - Pad * 2f - 10f);
        var w = inner.Size.X / GridCols;
        var h = inner.Size.Y / GridRows;
        return new Rect2(inner.Position.X + col * w, inner.Position.Y + row * h, w, h).Grow(-4f);
    }

    /// <summary>设施列表第 index 行（无标题带，直接从面板内沿 Pad 开始排）。</summary>
    public static Rect2 ScheduleFacilityRow(Rect2 panel, int index) => new(
        panel.Position.X + Pad,
        panel.Position.Y + Pad + index * ScheduleFacilityStep,
        panel.Size.X - Pad * 2f - ScrollBarGutter, 46f);

    /// <summary>日程页设施列表一屏能放的行数（滑条用，无标题带）。</summary>
    public static int ScheduleFacilityVisibleRows
    {
        get
        {
            var inner = ScheduleFacilities.Size.Y - Pad * 2f;
            return Mathf.Max(1, (int)(inner / ScheduleFacilityStep));
        }
    }

    /// <summary>日程页下方详情栏（排在网格与设施列表下方，顶边 750，底边严格对齐 1002，绝无重叠）。</summary>
    public static readonly Rect2 WorkDetail = new(422, 750, 1420, 252);

    /// <summary>详情栏里第 index 条可选产出行（一排 <paramref name="perRow"/> 条，往下折行）。从内沿 Pad 开始铺。</summary>
    public static Rect2 WorkOutputRow(int index, int perRow) => new(
        WorkDetail.Position.X + Pad + index % perRow * ((WorkDetail.Size.X - Pad * 2f) / perRow),
        WorkDetail.Position.Y + Pad + index / perRow * 56f,
        (WorkDetail.Size.X - Pad * 2f) / perRow - 12f, 48f);

    /// <summary>
    /// 角色页左轨道：名牌在上，立绘在名牌以下、轨道内沿以上的空间里**取中**
    /// （上下空隙相等）。与技能页同一套外沿（142..1002）。
    /// </summary>
    public static readonly Rect2 CharacterRail = new(78, FullPageTop, 400, FullPageHeight);
    public static readonly Rect2 CharacterIdentity = new(108, 172, 340, 76);

    /// <summary>
    /// 立绘框：宽取轨道内沿宽，高按 9:16 立绘规格反推，然后在名牌以下、
    /// 轨道内沿以上的空间里**居中**（上下空隙相等）。
    /// 走属性而非字段——它由 <see cref="CharacterIdentity"/> 与 <see cref="CharacterRail"/>
    /// 算出来，写死数字的话轨道一改就对不上。
    /// </summary>
    public static Rect2 CharacterPortrait
    {
        get
        {
            var width = CharacterRail.Size.X - Pad * 2f;
            var height = width * 16f / 9f;
            var top = CharacterIdentity.End.Y;
            var bottom = CharacterRail.End.Y - Pad;
            var y = top + Mathf.Max(0f, (bottom - top - height) / 2f);
            return new Rect2(CharacterRail.Position.X + Pad, y, width, height);
        }
    }

    public static readonly Rect2 CharacterIdentityName = new(124, 182, 308, 34);
    public static readonly Rect2 CharacterIdentityKind = new(124, 220, 308, 22);
    public static readonly Rect2 CharacterBody = new(502, FullPageTop, 1340, FullPageHeight);

    /// <summary>
    /// 状态页内容区：左右两列数据，中间一列立绘。
    /// 立绘严格落在内容区**正中心**（上下左右都居中）。
    /// </summary>
    public const float StatusContentLeft = 78f;
    public const float StatusContentTop = FullPageTop;
    public const float StatusContentWidth = 1764f;
    public const float StatusContentHeight = FullPageHeight;

    /// <summary>内容区正中。立绘与三列的分割都以它为基准。</summary>
    public static Vector2 StatusCenter => new(
        StatusContentLeft + StatusContentWidth / 2f,
        StatusContentTop + StatusContentHeight / 2f);

    public const float StatusColumnGap = 16f;

    /// <summary>中间一列（立绘列）的宽度；立绘宽 = 它减去两侧留白。</summary>
    public const float StatusMiddleWidth = 400f;

    /// <summary>左右两列的宽度：内容区扣掉中列与两条缝隙，对半分。</summary>
    public static float StatusSideWidth =>
        (StatusContentWidth - StatusMiddleWidth - StatusColumnGap * 2f) / 2f;

    /// <summary>左列（状态 + 攻击）。</summary>
    public static Rect2 StatusLeftColumn => new(
        StatusContentLeft, StatusContentTop, StatusSideWidth, StatusContentHeight);

    /// <summary>中列（名牌 + 立绘）。水平中心就是 StatusCenter.X。</summary>
    public static Rect2 StatusMiddleColumn => new(
        StatusContentLeft + StatusSideWidth + StatusColumnGap, StatusContentTop,
        StatusMiddleWidth, StatusContentHeight);

    /// <summary>右列（属性 + 能力）。</summary>
    public static Rect2 StatusRightColumn => new(
        StatusContentLeft + StatusSideWidth + StatusColumnGap * 2f + StatusMiddleWidth,
        StatusContentTop, StatusSideWidth, StatusContentHeight);

    /// <summary>左右列里上面那块的高度；下面那块吃剩余高度，两列都铺到 1002。</summary>
    public const float StatusPanelTopHeight = 396f;

    /// <summary>「状态」栏：左列上半。</summary>
    public static Rect2 StatusVitals => new(
        StatusLeftColumn.Position.X, StatusContentTop,
        StatusSideWidth, StatusPanelTopHeight);

    /// <summary>「攻击」栏：左列下半。</summary>
    public static Rect2 StatusCombat => new(
        StatusLeftColumn.Position.X, StatusContentTop + StatusPanelTopHeight + StatusColumnGap,
        StatusSideWidth,
        StatusContentHeight - StatusPanelTopHeight - StatusColumnGap);

    /// <summary>「属性」栏：右列上半。</summary>
    public static Rect2 StatusAttributes => new(
        StatusRightColumn.Position.X, StatusContentTop,
        StatusSideWidth, StatusPanelTopHeight);

    /// <summary>角色名：立在立绘正下方，与立绘同宽，居中对齐。</summary>
    public static Rect2 StatusName => new(
        StatusPortrait.Position.X, StatusPortrait.End.Y + 20f,
        StatusPortrait.Size.X, 36f);

    /// <summary>
    /// 立绘：宽取中列内沿宽，高按 9:16 立绘规格反推，然后以
    /// <see cref="StatusCenter"/> 为心**上下左右居中**。
    /// 走属性而非字段——它由中列与内容区算出来，写死数字的话页面一改就对不上。
    /// </summary>
    public static Rect2 StatusPortrait
    {
        get
        {
            var width = StatusMiddleWidth - Pad * 2f;
            var height = width * 16f / 9f;
            var c = StatusCenter;
            return new Rect2(c.X - width / 2f, c.Y - height / 2f, width, height);
        }
    }

    /// <summary>「属性」栏里六个方块：按 3×2 排，每个都是**正方形**。</summary>
    public const int AttributeGridCols = 3;
    public const int AttributeGridRows = 2;
    public const float AttributeCellGap = 14f;

    /// <summary>
    /// 「属性」区：右列上半。**不画外框**——每项属性自己一个正方形框就是它的容器。
    /// </summary>
    public static Rect2 StatusAttributeArea => new(
        StatusRightColumn.Position.X, StatusContentTop,
        StatusSideWidth, StatusPanelTopHeight);

    public static float AttributeCellSize(Rect2 panel)
    {
        var inner = panel.Grow(-Pad);
        var byWidth = (inner.Size.X - AttributeCellGap * (AttributeGridCols - 1)) / AttributeGridCols;
        var byHeight = (inner.Size.Y - AttributeCellGap * (AttributeGridRows - 1)) / AttributeGridRows;
        return Mathf.Floor(Mathf.Min(byWidth, byHeight));
    }

    /// <summary>第 index 个属性方块（0..5，按行优先），落在「属性」区里。</summary>
    public static Rect2 AttributeCell(int index) => AttributeCellIn(StatusAttributeArea, index);

    /// <summary>在给定区域里排第 index 个属性方块。</summary>
    public static Rect2 AttributeCellIn(Rect2 panel, int index)
    {
        var size = AttributeCellSize(panel);
        var inner = panel.Grow(-Pad);
        var col = index % AttributeGridCols;
        var row = index / AttributeGridCols;
        var gridW = size * AttributeGridCols + AttributeCellGap * (AttributeGridCols - 1);
        var gridH = size * AttributeGridRows + AttributeCellGap * (AttributeGridRows - 1);
        var x = inner.Position.X + (inner.Size.X - gridW) / 2f + col * (size + AttributeCellGap);
        var y = inner.Position.Y + (inner.Size.Y - gridH) / 2f + row * (size + AttributeCellGap);
        return new Rect2(x, y, size, size);
    }

    /// <summary>方块里的属性名（顶部一行）。</summary>
    public static Rect2 AttributeCellName(Rect2 cell) => new(
        cell.Position.X + 6f, cell.Position.Y + 8f, cell.Size.X - 12f, 22f);

    /// <summary>方块里的属性值（中部一行）。</summary>
    public static Rect2 AttributeCellValue(Rect2 cell) => new(
        cell.Position.X + 6f, cell.GetCenter().Y - 14f, cell.Size.X - 12f, 28f);

    /// <summary>方块底部的经验进度条。</summary>
    public static Rect2 AttributeCellBar(Rect2 cell) => new(
        cell.Position.X + 10f, cell.End.Y - 20f, cell.Size.X - 20f, 8f);

    /// <summary>
    /// 「能力」区：右列下半。**不画外框**——每条能力自己一个方框就是它的容器。
    /// 三条段头条（生活 / 武器 / 流派各一条）常显；点段头条把本级以下摊开。
    /// </summary>
    public static Rect2 StatusAbilityArea => new(
        StatusRightColumn.Position.X,
        StatusContentTop + StatusPanelTopHeight + StatusColumnGap,
        StatusSideWidth,
        StatusContentHeight - StatusPanelTopHeight - StatusColumnGap);

    /// <summary>能力条的宽（右侧让出滑条空隙，滑条不压条内文字）。</summary>
    public static float AbilityBarWidth => StatusAbilityArea.Size.X - ScrollBarGutter;

    public const float AbilityBarHeight = 38f;
    public const float AbilityBarGap = 8f;

    /// <summary>一屏放得下的能力条数。摊开后超出这个数就出滑条，不静默裁掉。</summary>
    public static int AbilityVisibleBars
    {
        get
        {
            var area = StatusAbilityArea;
            return Mathf.Max(1, (int)((area.Size.Y + AbilityBarGap) / (AbilityBarHeight + AbilityBarGap)));
        }
    }

    /// <summary>第 index 条能力条（摊平后的序号，段标题行也算一条）。</summary>
    public static Rect2 AbilityBar(int index) => new(
        StatusAbilityArea.Position.X,
        StatusAbilityArea.Position.Y + index * (AbilityBarHeight + AbilityBarGap),
        AbilityBarWidth, AbilityBarHeight);

    /// <summary>能力条左侧的类别名（暗标签）。</summary>
    public static Rect2 AbilityBarGroup(Rect2 bar) => new(
        bar.Position.X + 14f, bar.Position.Y, 108f, bar.Size.Y);

    /// <summary>能力条中间的技能名（亮字）。</summary>
    public static Rect2 AbilityBarName(Rect2 bar) => new(
        bar.Position.X + 130f, bar.Position.Y, bar.Size.X - 260f, bar.Size.Y);

    /// <summary>能力条右侧的等级（亮字，右对齐）。</summary>
    public static Rect2 AbilityBarLevel(Rect2 bar) => new(
        bar.End.X - 122f, bar.Position.Y, 108f, bar.Size.Y);

    /// <summary>能力条摊平后的总条数（三条段头条 + 各段摊开的下级条）。</summary>
    public static int StatusAbilityBarCount(InkPageModel? page)
    {
        if (page == null) return 0;
        var n = 0;
        var group = -1;
        foreach (var r in page.Rows)
        {
            if (r.IsHeading)
            {
                if (r.Name is "生活" or "武器" or "流派") { n++; group = n - 1; }
                continue;
            }
            if (group < 0) continue;
            if (page.AbilityOpen[group]) n++;
        }
        return n;
    }

    /// <summary>
    /// 能力列标题的热区：覆盖标题带整行（含列名右侧那点余量）。
    /// 点它在收敛（每段只报最高一项）与展开（列全）之间切换。
    /// </summary>
    public static Rect2 AbilityToggleRect(Rect2 panel) => new(
        panel.Position.X + Pad, panel.Position.Y + 12f,
        panel.Size.X - Pad * 2f, TitleBand - 12f);

    // ---------- 日程页：上排四段开关 + 下排全员工作矩阵 ----------

    /// <summary>上排四个时段卡（排在右侧主体内，顶边对齐 FullPageTop 142，宽 1420，高 116）。</summary>
    public static Rect2 ScheduleSlot(int slot) => new(
        422f + slot * (343f + 16f), FullPageTop, 343f, 116f);

    /// <summary>时段卡第一行：时间（例如「0时00分」）。留出右上角取消按钮的位置。</summary>
    public static Rect2 ScheduleSlotName(Rect2 card) => new(
        card.Position.X + 24f, card.Position.Y + 16f, card.Size.X - 48f - 70f, 26f);

    /// <summary>时段卡右上角的「取消」按钮（仅排定工作时显示）。</summary>
    public static Rect2 ScheduleSlotCancel(Rect2 card) => new(
        card.End.X - 16f - 64f, card.Position.Y + 12f, 64f, 26f);

    /// <summary>时段卡第二行左侧：工作/空闲。</summary>
    public static Rect2 ScheduleSlotWork(Rect2 card) => new(
        card.Position.X + 24f, card.Position.Y + 58f, 60f, 34f);

    /// <summary>时段卡第二行右侧：房间-设施-产出（右对齐）。</summary>
    public static Rect2 ScheduleSlotChain(Rect2 card) => new(
        card.Position.X + 88f, card.Position.Y + 58f, card.Size.X - 48f - 68f, 34f);

    public static Rect2 StatusRowLabel(Rect2 row) => new(row.Position.X, row.Position.Y, 76f, row.Size.Y);
    public static Rect2 StatusRowValue(Rect2 row) => new(row.End.X - 158f, row.Position.Y, 158f, row.Size.Y);
    public static Rect2 StatusRowMeter(Rect2 row) => new(row.Position.X + 88f, row.GetCenter().Y - 4f,
        Mathf.Max(12f, row.Size.X - 258f), 8f);

    /// <summary>
    /// 状态页「状态」「属性」两栏的第 index 行：按 <paramref name="count"/> 行
    /// 在标题带以下、栏底以上的空间里**均分**，所以两栏行数不同（5 行 vs 6 行）
    /// 也各排得下、不会溢到栏外。行距由可用高度算出来，不写死。
    /// </summary>
    public static Rect2 StatusColumnRow(Rect2 panel, int index, int count)
    {
        var top = panel.Position.Y + Pad;
        var bottom = panel.End.Y - 12f;
        var step = (bottom - top) / Mathf.Max(1, count);
        return new Rect2(
            panel.Position.X + Pad, top + index * step,
            panel.Size.X - Pad * 2f, step - 4f);
    }

    public static Rect2 CombatStatCell(int index)
    {
        var panel = StatusCombat;
        var inner = new Rect2(
            panel.Position.X + Pad, panel.Position.Y + Pad,
            panel.Size.X - Pad * 2f, panel.Size.Y - Pad * 2f - 12f);
        // 3 行 2 列均分：栏高变了格子跟着长，不会在栏底留空带。
        const int rows = 3;
        const int cols = 2;
        const float gap = 16f;
        var w = (inner.Size.X - gap * (cols - 1)) / cols;
        var h = (inner.Size.Y - gap * (rows - 1)) / rows;
        var col = index % cols;
        var row = index / cols;
        return new Rect2(
            inner.Position.X + col * (w + gap),
            inner.Position.Y + row * (h + gap), w, h);
    }

}
