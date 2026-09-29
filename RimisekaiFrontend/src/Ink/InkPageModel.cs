using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>左下角入口能打开的页面。</summary>
public enum InkPage
{
    None = 0,
    Stock,
    Trade,
    Craft,
    Develop,

    /// <summary>角色状态：体力/气力/疲劳/好感/心情/关系。针对聊天对象。</summary>
    Status,

    /// <summary>角色技能：生活 / 武器 / 流派。</summary>
    Skills,

    /// <summary>角色日程：四段开关（空闲 / 工作 / 不干活）。</summary>
    Schedule,

    /// <summary>
    /// 工作页：全员矩阵。行 = 工作类型，列 = 角色，格子里是优先级（空白 / 1-4）。
    /// 由 <see cref="InkWorkPageBuilder"/> 填内容，走专用渲染路径。
    /// </summary>
    Work,

    /// <summary>
    /// 设施交互页：点设施行动（如“打开货架”）后，在左上角铺开这件设施的操作界面。
    /// 由 <see cref="InkHubModel"/> 按“当前打开的设施”填内容，不走通用列表页。
    /// </summary>
    Fixture,
}

/// <summary>一个页面按钮的显示信息。</summary>
public readonly record struct InkPageInfo(InkPage Page, string Label);

public static class InkPageTabs
{
    /// <summary>全屏子页的页签顺序：管理三页与角色三页共用一条页签带。</summary>
    public static readonly InkPage[] All =
    {
        InkPage.Stock,
        InkPage.Trade,
        InkPage.Craft,
        InkPage.Work,
        InkPage.Status,
        InkPage.Skills,
        InkPage.Schedule,
    };
}


/// <summary>
/// 角色三页（状态/技能/日程）由聊天层右上角进入，针对当前对话对象。
/// 它们盖住整个屏幕，因此打开期间聊天层要让位。
/// </summary>
public static class InkChatPages
{
    public static bool Contains(InkPage page) =>
        page is InkPage.Status or InkPage.Skills or InkPage.Schedule;
}

/// <summary>
/// 列表查询参数：由界面视图状态摊平而来，Builder 据此搜索、筛选、排序。
/// </summary>
public readonly record struct InkPageQuery(
    int Selected,
    int SelectedRoom,
    int SelectedFacility,
    string Search,
    bool SearchFocused,
    int Filter,
    int Sort,
    bool SortDesc,
    int PlacingFacility,
    int PlacingRoom,
    int PageFirst = 0);

/// <summary>开发页（四区域）的内容：左上房间网格、右上房间详情、左下设施列表、右下设施详情。</summary>
public sealed class InkDevModel
{
    /// <summary>左上网格的房间格（已开拓的房间，按坐标落位）。</summary>
    public IReadOnlyList<InkDevRoomCell> Rooms { get; init; } = System.Array.Empty<InkDevRoomCell>();

    /// <summary>选中的房间下标；-1 表示未选中。</summary>
    public int SelectedRoom { get; init; } = -1;

    /// <summary>选中的设施 Id；-1 表示未选中。</summary>
    public int SelectedFacility { get; init; } = -1;

    /// <summary>选中房间的 Id；-1 表示未选中。</summary>
    public int RoomId { get; init; } = -1;

    /// <summary>右上：选中房间的名称标题。</summary>
    public string RoomDetailTitle { get; init; } = "";

    /// <summary>右上：设施列表（全部设施，含未放置）。</summary>
    public IReadOnlyList<InkDevRow> FacilityRows { get; init; } = System.Array.Empty<InkDevRow>();

    /// <summary>可建造的设施目录行（整行点击即建造，建成后未放置）。</summary>
    public IReadOnlyList<InkPageRow> FacilityCatalog { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>左下：房间行（含未放置的房间）。</summary>
    public IReadOnlyList<InkDevRow> RoomRows { get; init; } = System.Array.Empty<InkDevRow>();

    /// <summary>可建造的房间目录行（整行点击即建造，建成后未放置）。</summary>
    public IReadOnlyList<InkPageRow> RoomCatalog { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>右下：共用详情——当前选中项（设施或房间）。</summary>
    public string DetailTitle { get; init; } = "";
    public string DetailNote { get; init; } = "";
    public IReadOnlyList<InkPageRow> DetailActions { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>待放进选中房间的设施 Id（-1 无）。</summary>
    public int PlacingFacility { get; init; } = -1;

    /// <summary>待放到网格上的房间 Id（-1 无）。</summary>
    public int PlacingRoom { get; init; } = -1;
}

/// <summary>开发页列表的一行。Kind 决定点击行为与右侧说明。</summary>
public sealed record InkDevRow
{
    public InkDevRowKind Kind { get; init; }
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Note { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool Selected { get; init; }
}

/// <summary>开发页列表行的类别。</summary>
public enum InkDevRowKind
{
    /// <summary>已放置的房间。</summary>
    Room,

    /// <summary>建成而未放置的房间。</summary>
    RoomUnplaced,

    /// <summary>已放置到房间的设施。</summary>
    Facility,

    /// <summary>建成而未放置的设施。</summary>
    FacilityUnplaced,
}

/// <summary>开发页房间网格里的一个房间格。</summary>
public sealed class InkDevRoomCell
{
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string Name { get; init; } = "";

    /// <summary>非空房间（里面有设施）：右上角画白色 X，点击拆除。</summary>
    public bool NonEmpty { get; init; }

    /// <summary>拆除是否可用（玩家当前所在的房间拆不得）。</summary>
    public bool Removable { get; init; }

    public bool Selected { get; init; }
}

/// <summary>
/// 工作页（全员矩阵）的内容：行 = 工作类型，列 = 角色。
/// 每格是优先级（0 空白 / 1-4）与角色在该项上的技能值。
/// </summary>
public sealed class InkWorkModel
{
    /// <summary>矩阵列：参与排班的角色（不含主角）。</summary>
    public IReadOnlyList<InkWorkColumn> Columns { get; init; } = System.Array.Empty<InkWorkColumn>();

    /// <summary>矩阵行：工作类型，顺序即行序。</summary>
    public IReadOnlyList<InkWorkRow> Rows { get; init; } = System.Array.Empty<InkWorkRow>();

    /// <summary>选中格的行下标（工作类型）；-1 未选。</summary>
    public int SelectedRow { get; init; } = -1;

    /// <summary>选中格的列下标（角色）；-1 未选。</summary>
    public int SelectedColumn { get; init; } = -1;

    /// <summary>右栏说明标题。</summary>
    public string DetailTitle { get; init; } = "";

    /// <summary>右栏说明正文，按 \n 分行。</summary>
    public string DetailNote { get; init; } = "";

    /// <summary>右栏动作按钮（改优先级）。</summary>
    public IReadOnlyList<InkPageRow> DetailActions { get; init; } = System.Array.Empty<InkPageRow>();
}

/// <summary>工作矩阵的一列：一个角色。</summary>
public readonly record struct InkWorkColumn(int CharacterId, string Name);

/// <summary>工作矩阵的一行：一种工作类型，以及每个角色的格子。</summary>
public sealed class InkWorkRow
{
    public ActionKind Task { get; init; }

    /// <summary>行名（工作类型的中文名）。</summary>
    public string Name { get; init; } = "";

    /// <summary>这项活对应的生活技能名（表头标注用）。</summary>
    public string SkillName { get; init; } = "";

    /// <summary>每个角色的格子，下标与 <see cref="InkWorkModel.Columns"/> 对齐。</summary>
    public IReadOnlyList<InkWorkCell> Cells { get; init; } = System.Array.Empty<InkWorkCell>();
}

/// <summary>工作矩阵的一格。</summary>
public readonly record struct InkWorkCell(int Priority, int Skill)
{
    /// <summary>格子里显示的字：0 空白，1-4 显示档位。</summary>
    public string Label => Priority <= 0 ? "" : Priority.ToString();

    /// <summary>是否已派（用于画高亮）。</summary>
    public bool Assigned => Priority > 0;
}

/// <summary>
/// 页面的内容快照。各页面共用同一种形状：
/// 若干条目 + 可执行的操作为，渲染器按 Kind 决定画法。
/// </summary>
public sealed class InkPageModel
{
    public InkPage Page { get; init; }
    public string Title { get; init; } = "";

    /// <summary>列表行：左边名称、右边数值，以及该行可执行的动作。</summary>
    public IReadOnlyList<InkPageRow> Rows { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>左列表当前选中的行下标。-1 表示无。</summary>
    public int SelectedRow { get; init; } = -1;

    /// <summary>右栏详情标题（选中项的名字）。空表示右栏不画。</summary>
    public string DetailTitle { get; init; } = "";

    /// <summary>右栏详情说明，按 \n 分行。</summary>
    public string DetailNote { get; init; } = "";

    /// <summary>右栏详情的动作按钮行。Name 是按钮字，Action 决定执行什么。</summary>
    public IReadOnlyList<InkPageRow> DetailActions { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>列表为空时的提示语。没有任何一行匹配搜索/筛选时也会显示。</summary>
    public string EmptyHint { get; init; } = "";

    /// <summary>列表滚动窗口的页首行下标。</summary>
    public int ListFirst { get; init; }

    /// <summary>角色三页：角色名。</summary>
    public string CharacterName { get; init; } = "";

    /// <summary>角色三页：立绘路径。</summary>
    public string PortraitPath { get; init; } = "";

    /// <summary>开发页的四区域内容；其他页面为 null。</summary>
    public InkDevModel? Dev { get; init; }

    /// <summary>工作页的全员矩阵内容；其他页面为 null。</summary>
    public InkWorkModel? Work { get; init; }

    // ---- 列表控制行：搜索 / 筛选 / 排序 ----

    /// <summary>当前搜索词（回显在输入框里）。</summary>
    public string Search { get; init; } = "";

    /// <summary>搜索框是否聚焦（聚焦时画光标）。</summary>
    public bool SearchFocused { get; init; }

    /// <summary>筛选档位标签，下标即档位；第一个恒为“全部”。</summary>
    public IReadOnlyList<string> Filters { get; init; } = System.Array.Empty<string>();

    /// <summary>当前筛选档位。</summary>
    public int ActiveFilter { get; init; }

    /// <summary>排序字段标签，下标即字段。</summary>
    public IReadOnlyList<string> Sorts { get; init; } = System.Array.Empty<string>();

    /// <summary>当前排序字段。</summary>
    public int ActiveSort { get; init; }

    /// <summary>当前排序是否降序。</summary>
    public bool ActiveSortDesc { get; init; }

    /// <summary>
    /// 右栏动作按钮是否按网格排（一行放不下时折行）。
    /// 只给选项多的页面用（日程的派活按钮），列表页保持原来的单排横列。
    /// </summary>
    public bool DetailActionGrid { get; init; }

    /// <summary>
    /// 是否显示列表控制行（搜索/筛选/排序）。
    /// 库存/交易/制作有；角色三页是只读展示，没有可查可排的内容，不给。
    /// </summary>
    public bool HasControls { get; init; }

    /// <summary>
    /// 右栏是否有内容。没有则列表铺满整行并按两栏排（角色三页走这条）。
    /// </summary>
    public bool HasDetail =>
        DetailTitle.Length > 0 || DetailNote.Length > 0 || DetailActions.Count > 0;

    public static InkPageInfo Info(InkPage page) => page switch
    {
        InkPage.Stock => new InkPageInfo(page, "库存"),
        InkPage.Trade => new InkPageInfo(page, "交易"),
        InkPage.Craft => new InkPageInfo(page, "制作"),
        InkPage.Develop => new InkPageInfo(page, "开发"),
        InkPage.Work => new InkPageInfo(page, "工作"),
        InkPage.Status => new InkPageInfo(page, "状态"),
        InkPage.Skills => new InkPageInfo(page, "技能"),
        InkPage.Schedule => new InkPageInfo(page, "日程"),
        _ => new InkPageInfo(page, "?"),
    };
}

/// <summary>
/// 列表的一行。Action 决定点这一行做什么；None 表示只是展示。
/// </summary>
public sealed class InkPageRow
{
    public string Name { get; init; } = "";

    /// <summary>右侧数值文本，如数量、价格。</summary>
    public string Value { get; init; } = "";

    /// <summary>补充说明，如配方材料。</summary>
    public string Note { get; init; } = "";

    /// <summary>点击该行执行的动作。</summary>
    public InkPageAction Action { get; init; } = InkPageAction.None;

    /// <summary>动作携带的 Id（物品 Id / 设施 Id / 房间 Id）。</summary>
    public string TargetId { get; init; } = "";

    public int TargetNumber { get; init; }

    /// <summary>是否可点。false 时画成暗色且点不动。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>是否处于选中态（如日程里当前那段的工作）。只影响画法。</summary>
    public bool Selected { get; init; }

    /// <summary>角色三页：量表当前值（体力/气力/疲劳/好感/心情）。</summary>
    public float? MeterValue { get; init; }

    /// <summary>角色三页：量表最大值。</summary>
    public float MeterMax { get; init; }

    /// <summary>角色三页：是否是分组标题。</summary>
    public bool IsHeading { get; init; }
}

/// <summary>页面行能执行的动作。</summary>
public enum InkPageAction
{
    None = 0,

    /// <summary>卖出一份。</summary>
    Sell,

    /// <summary>买入一份。</summary>
    Buy,

    /// <summary>制作一份。</summary>
    Craft,

    /// <summary>开拓该房间。</summary>
    Develop,

    /// <summary>拆除该设施（材料按比例返还）。</summary>
    RemoveFacility,

    /// <summary>按设施目录建新设施（花材料，放进指定房间）。</summary>
    BuildDef,

    /// <summary>按房间目录建新房间（花材料，建成后未放置）。</summary>
    BuildRoom,

    /// <summary>把未放置的设施放进指定房间。</summary>
    PlaceFacility,

    /// <summary>建造该设施。</summary>
    Build,

    /// <summary>日程：给当前角色的某一段下委派。TargetNumber 是时段，TargetId 是任务名。</summary>
    AssignTask,

    /// <summary>
    /// 工作页：改某角色对某工作类型的优先级。
    /// TargetNumber 是工作类型下标，TargetId 是角色 Id。
    /// </summary>
    SetWorkPriority,
}
