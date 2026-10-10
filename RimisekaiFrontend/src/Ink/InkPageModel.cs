using System;
using System.Collections.Generic;
using Godot;
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

    /// <summary>
    /// 角色状态：状态 / 攻击 / 关系，以及能力（生活 / 武器 / 流派）。
    /// 针对聊天对象。
    /// </summary>
    Status,

    /// <summary>角色技能：战斗技能目录，按流派分组，解锁门槛即流派熟练等级。</summary>
    Skills,

    /// <summary>
    /// 角色日程：上排四段安排（空闲 / 工作），
    /// 下半房间网格选房间、设施列表选设施——排班就是「某时段到某件设施去」。
    /// </summary>
    Schedule,

    /// <summary>任务页（公会板式）。</summary>
    Quest,

    /// <summary>战斗日志页：展开查看整场战斗的完整战况流水。</summary>
    CombatLog,

    /// <summary>
    /// 设施交互页：点设施行动（如“打开箱子”）后，在左上角铺开这件设施的操作界面。
    /// 由 <see cref="InkHubModel"/> 按“当前打开的设施”填内容，不走通用列表页。
    /// </summary>
    Fixture,
}

/// <summary>一个页面按钮的显示信息。</summary>
public readonly record struct InkPageInfo(InkPage Page, string Label);

public static class InkPageTabs
{
    /// <summary>领地专属页面的页签（库存 / 交易）。制作界面已彻底删除。</summary>
    public static readonly InkPage[] Territory =
    {
        InkPage.Stock,
        InkPage.Trade,
    };

    /// <summary>角色专属页面的页签（状态 / 技能 / 日程）。</summary>
    public static readonly InkPage[] Character =
    {
        InkPage.Status,
        InkPage.Skills,
        InkPage.Schedule,
    };

    /// <summary>战斗日志专属页签（仅单页，无跨页切换）。</summary>
    public static readonly InkPage[] Combat =
    {
        InkPage.CombatLog,
    };

    /// <summary>全屏子页的页签全表（兼容旧引用）。</summary>
    public static readonly InkPage[] All = Territory;

    /// <summary>获取指定页面所属的独立页签组（领地页、角色页与战斗日志彻底隔离）。</summary>
    public static InkPage[] For(InkPage page)
    {
        if (page == InkPage.CombatLog)
            return Combat;
        return InkChatPages.Contains(page) ? Character : Territory;
    }
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
    int PageFirst = 0,
    int SelectedHeld = -1,
    int SelectedMarket = -1,
    int HeldFirst = 0,
    int MarketFirst = 0,
    int ConfirmCell = -1,
    int ActionFirst = 0,
    string Notice = "");

/// <summary>
/// 交易页一栏里的一行：名称、数量标注与单价（单位 G）。
/// 左栏是玩家能卖的（卖一份收多少），右栏是市场能卖的（买一份花多少）。
/// 选中态按行下标比对（<see cref="InkTradeModel.SelectedHeld"/> /
/// <see cref="InkTradeModel.SelectedMarket"/>），不逐行存旗标。
/// </summary>
public sealed record InkTradeRow(string ItemId, string Name, string Count, int Price);

/// <summary>
/// 交易页三面板：左栏领地库存、中栏交易钮、右栏市场库存。
/// 两栏各自独立选中与滚动；同一时刻只有一栏有选中项——
/// 买以右栏选中行为对象，卖以左栏选中行为对象。
/// </summary>
public sealed class InkTradeModel
{
    /// <summary>持有的金钱（单位 G）。</summary>
    public long Money { get; init; }

    /// <summary>左栏：领地物品清单。</summary>
    public IReadOnlyList<InkTradeRow> Held { get; init; } = Array.Empty<InkTradeRow>();

    /// <summary>右栏：市场在售清单。</summary>
    public IReadOnlyList<InkTradeRow> Market { get; init; } = Array.Empty<InkTradeRow>();

    /// <summary>左栏选中行下标；-1 未选。</summary>
    public int SelectedHeld { get; init; } = -1;

    /// <summary>右栏选中行下标；-1 未选。</summary>
    public int SelectedMarket { get; init; } = -1;

    /// <summary>左栏滚动首行。</summary>
    public int HeldFirst { get; init; }

    /// <summary>右栏滚动首行。</summary>
    public int MarketFirst { get; init; }

    /// <summary>此刻能不能成交：人站在城镇商店里才行。</summary>
    public bool TradeAvailable { get; init; } = true;

    /// <summary>准备卖出的物品名称与单价。</summary>
    public string SellName { get; init; } = "";
    public long SellPrice { get; init; }

    /// <summary>准备购买的物品名称与单价。</summary>
    public string BuyName { get; init; } = "";
    public long BuyPrice { get; init; }

    /// <summary>选中的物品详情信息（中栏与右栏通用）。</summary>
    public string SelectedName { get; init; } = "";
    public string SelectedCategory { get; init; } = "";
    public string SelectedDesc { get; init; } = "";
    public string SelectedMeta { get; init; } = "";
    public int TotalHeldCount { get; init; }

    /// <summary>当前操作是否属于买入（选中右栏为买，选中左栏为卖）。</summary>
    public bool Buying => SelectedMarket >= 0;

    /// <summary>当前操作所需的金额数。</summary>
    public long RunPrice => Buying ? BuyPrice : SellPrice;

    /// <summary>是否选中了准备卖出项。</summary>
    public bool HasSell => SelectedHeld >= 0 && !string.IsNullOrEmpty(SellName);

    /// <summary>是否选中了准备购买项。</summary>
    public bool HasBuy => SelectedMarket >= 0 && !string.IsNullOrEmpty(BuyName);

    /// <summary>中栏成交反馈：最近一次交易的结果，空表示没有可反馈的。</summary>
    public string Notice { get; init; } = "";

    /// <summary>仅买入时是否可点。</summary>
    public bool CanBuyOnly => TradeAvailable && HasBuy && BuyPrice > 0 && Money >= BuyPrice;

    /// <summary>仅卖出时是否可点。</summary>
    public bool CanSellOnly => TradeAvailable && HasSell && SellPrice > 0;

    /// <summary>同时买卖时是否可点（以物易物：现有资金 + 卖出收入 >= 买入支出）。</summary>
    public bool CanCombined => TradeAvailable && HasSell && HasBuy && SellPrice > 0 && BuyPrice > 0 && (Money + SellPrice >= BuyPrice);

    /// <summary>
    /// 成交按钮是否可点：支持仅卖出、仅买入、或买卖一次性完成。
    /// </summary>
    public bool CanRun => (HasSell && HasBuy) ? CanCombined : HasSell ? CanSellOnly : HasBuy && CanBuyOnly;
}

/// <summary>
/// 开发页（五区域）的内容（2026-10-01 主人定）：
/// 左上领地网格（已开发房间 ＋ 邻近可开拓的未开发格，含没有房间实体的「空地」）、
/// 右上当前选中房间的设施、左下已建未安装的房间、
/// 中下操作面板（所有操作按钮）、右下详情（纯文字）。
/// 点邻近的未开发格 → <see cref="ConfirmCell"/> 弹确认窗。
/// </summary>
public sealed class InkDevModel
{
    /// <summary>左上网格的格子：已开发房间 ＋ 邻近可开拓的未开发房间。</summary>
    public IReadOnlyList<InkDevRoomCell> Rooms { get; init; } = System.Array.Empty<InkDevRoomCell>();

    /// <summary>左上网格里选中格的下标；-1 表示未选中。</summary>
    public int SelectedCell { get; init; } = -1;

    /// <summary>当前正在看的是哪块区域（3×3 拼图里的扁平编号）。</summary>
    public int RegionId { get; init; }

    /// <summary>3×3 区域拼图的解锁位掩码。画四向箭头时判亮暗。</summary>
    public int UnlockedRegionMask { get; init; } = 1;

    /// <summary>右上：选中房间的 Id；-1 表示未选中。</summary>
    public int RoomId { get; init; } = -1;

    /// <summary>右上：面板标题（随选中房间变，如「堂屋 · 设施」）。</summary>
    public string FacilityTitle { get; init; } = "设施";

    /// <summary>右上：**只列当前选中房间的设施**；没选房间时是空的。</summary>
    public IReadOnlyList<InkDevRow> FacilityRows { get; init; } = System.Array.Empty<InkDevRow>();

    /// <summary>右上：选中的设施 Id；-1 表示未选中。</summary>
    public int SelectedFacility { get; init; } = -1;

    /// <summary>左下：面板标题。</summary>
    public string RoomListTitle { get; init; } = "待安装的房间";

    /// <summary>左下：**只列已建但还没安装的房间**（建成后尚未落到网格上）。</summary>
    public IReadOnlyList<InkDevRow> RoomRows { get; init; } = System.Array.Empty<InkDevRow>();

    /// <summary>左下：选中的待安装房间行下标；-1 表示未选中。</summary>
    public int SelectedRoomRow { get; init; } = -1;

    /// <summary>中下操作面板：所有操作按钮（建造设施／建造房间／拆除…）。</summary>
    public IReadOnlyList<InkDevActionRow> ActionRows { get; init; } = System.Array.Empty<InkDevActionRow>();

    /// <summary>中下操作面板滚动首行。</summary>
    public int ActionFirst { get; init; }

    /// <summary>右下详情：标题。</summary>
    public string DetailTitle { get; init; } = "";

    /// <summary>右下详情：正文（按 \n 分行）。</summary>
    public string DetailNote { get; init; } = "";

    /// <summary>
    /// 开拓确认弹窗：待开拓的格子线性下标（<c>row*5+col</c>）；-1 表示弹窗关着。
    /// 用格子而不是房间 Id，因为「空地」还没有房间实体。
    /// </summary>
    public int ConfirmCell { get; init; } = -1;

    /// <summary>开拓确认弹窗：标题（房间名）。</summary>
    public string ConfirmTitle { get; init; } = "";

    /// <summary>开拓确认弹窗：正文（问句 ＋ 花费明细）。</summary>
    public string ConfirmBody { get; init; } = "";

    /// <summary>待放到网格上的房间 Id（-1 无）。</summary>
    public int PlacingRoom { get; init; } = -1;

    /// <summary>右上设施列表滚动首行。</summary>
    public int FacilityFirst { get; init; }

    /// <summary>左下房间列表滚动首行。</summary>
    public int RoomFirst { get; init; }
}

/// <summary>开发页列表的一行。Kind 决定点击行为与右侧说明。</summary>
public sealed record InkDevRow
{
    public InkDevRowKind Kind { get; init; }
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Note { get; init; } = "";
    public string Value { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool Selected { get; init; }
}

/// <summary>开发页列表行的类别。</summary>
public enum InkDevRowKind
{
    /// <summary>已开发、已落到网格上的房间。</summary>
    Room,

    /// <summary>已建但还没安装（没落到网格上）的房间。</summary>
    RoomUnplaced,

    /// <summary>已放置到房间的设施。</summary>
    Facility,

    /// <summary>已建但还没安装的设施。</summary>
    FacilityUnplaced,
}

/// <summary>
/// 开发页房间网格里的一个格子：已开发的房间、邻近可开拓的未开发房间，
/// 或邻近可开拓的「空地」（还没有房间实体，<see cref="Id"/> 为 -1）。
/// </summary>
public sealed record InkDevRoomCell
{
    /// <summary>房间 Id；-1 表示这是还没有房间实体的「空地」。</summary>
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string Name { get; init; } = "";

    /// <summary>已开发（Open）。false 表示邻近的未开发格，点击弹开拓确认窗。</summary>
    public bool Open { get; init; } = true;

    /// <summary>未开发的格子是否够得着（邻近已开发房间），可以点开确认窗。</summary>
    public bool CanDevelop { get; init; }

    /// <summary>未开发格右下角的花费角标，如「材料 木材×5　钱 100」。</summary>
    public string CostText { get; init; } = "";

    /// <summary>非空房间（里面有设施）：右上角画白色 X，点击拆除。</summary>
    public bool NonEmpty { get; init; }

    /// <summary>已开发的「空房」：还没有装任何房间类型，是待安装房间的落点。</summary>
    public bool Vacant { get; init; }

    /// <summary>拆除是否可用（玩家当前所在的房间拆不得）。</summary>
    public bool Removable { get; init; }

    public bool Selected { get; init; }
}

/// <summary>
/// 开发页中下操作面板里的一行按钮。行自己带着要派发的 <see cref="InkAction"/> 与下标，
/// 界面侧照抄注册即可——建造设施／建造房间／拆除／放置／退出都挤在同一列里。
/// </summary>
public sealed record InkDevActionRow
{
    public string Name { get; init; } = "";

    /// <summary>按钮左侧的短前缀，如「设」「房」「拆」。空则只画名称。</summary>
    public string Prefix { get; init; } = "";

    /// <summary>点击派发的动作。</summary>
    public InkAction Action { get; init; } = (InkAction)0;

    /// <summary>派发时带的 Index（目录行下标 / 房间格下标）。</summary>
    public int Index { get; init; } = -1;

    public bool Enabled { get; init; } = true;

    /// <summary>点不动时的缘由（如「只能摆在室内」），画在行尾。空 = 不写。</summary>
    public string Note { get; init; } = "";

    /// <summary>是否处于选中态（如当前选中的设施）。只影响画法。</summary>
    public bool Selected { get; init; }
}

/// <summary>
/// 日程页内容：左侧全员成员列表 + 右侧房间网格（选房间）＋ 设施列表（选设施）。
/// 选中时段后，点设施即把这段排到那件设施上。
/// </summary>
public sealed class InkWorkModel
{
    /// <summary>日程页左侧全员成员列表（含玩家本人）。</summary>
    public IReadOnlyList<InkPageRow> Members { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>当前选中的成员 Id（用于左侧列表高亮与日程绑定）。</summary>
    public int SelectedMemberId { get; init; } = -1;

    /// <summary>成员列表的滚动首行。</summary>
    public int MemberFirst { get; init; }

    /// <summary>网格里的房间格（已开拓的房间，按坐标落位）。</summary>
    public IReadOnlyList<InkDevRoomCell> Rooms { get; init; } = System.Array.Empty<InkDevRoomCell>();

    /// <summary>选中的房间 Id；-1 表示未选。</summary>
    public int RoomId { get; init; } = -1;

    /// <summary>选中房间的名称（标题用）。</summary>
    public string RoomName { get; init; } = "";

    /// <summary>选中房间里的设施列表。点一行即把当前时段排到它上面。</summary>
    public IReadOnlyList<InkPageRow> Facilities { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>当前时段已点名的设施 Id；-1 表示没点名。</summary>
    public int AssignedFacilityId { get; init; } = -1;

    /// <summary>
    /// 选中设施的可选产出：直接采集物 + 该设施支持制作的配方成品。
    /// Name 是成品名，Value 是材料清单（采集项为空），Note 标「采集」或「制作」。
    /// </summary>
    public IReadOnlyList<InkPageRow> Outputs { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>设施列表的窗口首行（滑条用）。</summary>
    public int FacilityFirst { get; init; }

    /// <summary>下方详情标题。</summary>
    public string DetailTitle { get; init; } = "";

    /// <summary>下方详情正文，按 \n 分行。</summary>
    public string DetailNote { get; init; } = "";

    /// <summary>下方详情动作按钮。</summary>
    public IReadOnlyList<InkPageRow> DetailActions { get; init; } = System.Array.Empty<InkPageRow>();
}

/// <summary>
/// 技能盘上的一个技能节点：占网格里的一格三角。
/// Sector 是扇区（流派）序号，Slot 是扇区内的格号；
/// 两者一起决定节点在盘上的顶点（见 <see cref="InkLayout.SkillDiscTilePolygon"/>）。
/// </summary>
public sealed class InkSkillTile
{
    /// <summary>技能 Id；过路/空位节点为空串。</summary>
    public string Id { get; init; } = "";

    /// <summary>节点名（只在右栏详情显示，格内不写字）。</summary>
    public string Name { get; init; } = "";

    /// <summary>扇区序号：0=通用，1..7=七个流派。</summary>
    public int Sector { get; init; }

    /// <summary>环号（由内向外，0 起）。</summary>
    public int Ring { get; init; }

    /// <summary>环内列号。</summary>
    public int Col { get; init; }

    /// <summary>true = 该列的上三角（贴外弧），false = 下三角（贴内弧）。</summary>
    public bool Upper { get; init; }

    /// <summary>节点类别：真技能 / 过路 / 空位。</summary>
    public InkSkillNodeKind Kind { get; init; } = InkSkillNodeKind.Skill;

    /// <summary>是否已解锁。未解锁画成暗色，门槛内容在右栏详情里逐条列出。</summary>
    public bool Unlocked { get; init; }
}

/// <summary>
/// 星盘上一格的类别。技能盘是一整片三角格，真正有内容的只是其中一部分，
/// 其余格按 Blade&Hex 的做法填成过路/空位，盘面因此是满的、技能也连得起来。
/// </summary>
public enum InkSkillNodeKind
{
    /// <summary>真技能：有 Id、有名字，可点开看详情。</summary>
    Skill,

    /// <summary>过路节点：无内容，只把技能连成一张网、填满盘面（对应 Blade&Hex 的 pip/small）。</summary>
    Filler,

    /// <summary>空位：未铺设的格，只画最淡的刻面。</summary>
    Empty,
}

/// <summary>
/// 技能页的星盘内容：盘心的角色名与熟练总览、八个扇区的名与等级、盘上的瓦片。
/// 技能不再是列表——它是一具铺满三角瓦片的大星盘。
/// </summary>
public sealed class InkSkillDiscModel
{
    /// <summary>盘心的角色名。</summary>
    public string CharacterName { get; init; } = "";

    /// <summary>盘心的当前流派名与熟练（第二行）。</summary>
    public string StyleLine { get; init; } = "";

    /// <summary>扇区标签：下标即扇区序号（0=通用，1..7=流派），内容是「流派名 熟练 LvN」。</summary>
    public IReadOnlyList<string> SectorLabels { get; init; } = System.Array.Empty<string>();

    /// <summary>盘上的瓦片（只含该扇区实际有的技能，空格子不出现）。</summary>
    public IReadOnlyList<InkSkillTile> Tiles { get; init; } = System.Array.Empty<InkSkillTile>();

    /// <summary>当前选中的技能 Id；空表示未选。</summary>
    public string SelectedId { get; init; } = "";

    /// <summary>当前聚焦的扇区序号（0..5），-1 表示未聚焦全盘视图。</summary>
    public int FocusedSector { get; init; } = -1;

    /// <summary>当前视图缩放比例（1.0..1.85）。</summary>
    public float ViewZoom { get; init; } = 1.0f;

    /// <summary>当前视图中心在星盘原始几何中的锚点位置。</summary>
    public Vector2 ViewPivot { get; init; } = new(584f, 558f);

    /// <summary>当前视图旋转对齐角度（弧度）。</summary>
    public float ViewRotation { get; init; } = 0.0f;
}

/// <summary>
/// 页面的内容快照。各页面共用同一种形状：
/// 若干条目 + 可执行的操作为，渲染器按 Kind 决定画法。
/// </summary>
public sealed class InkPageModel
{
    public InkPage Page { get; init; }
    public string Title { get; init; } = "";

    /// <summary>页眉副标题（右侧）：交易页用它显示持有的金钱。</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>列表行：左边名称、右边数值，以及该行可执行的动作。</summary>
    public IReadOnlyList<InkPageRow> Rows { get; init; } = System.Array.Empty<InkPageRow>();

    /// <summary>左列表当前选中的行下标。-1 表示无。</summary>
    public int SelectedRow { get; init; } = -1;

    /// <summary>右栏详情标题（选中项的名字）。空表示右栏不画。</summary>
    public string DetailTitle { get; init; } = "";

    /// <summary>右栏详情说明，按 \n 分行。</summary>
    public string DetailNote { get; init; } = "";

    /// <summary>右栏详情底部的效果描述（技能盘用：这一招打出去实际做什么）。空表示不画。</summary>
    public string DetailEffect { get; init; } = "";

    /// <summary>
    /// 右栏详情下卡的需求条件清单（技能盘用）：每项是「条件 + 是否未达成」。
    /// 渲染端只画一次「需求」标签，条件逐条列在其下，不重复字样。
    /// </summary>
    public IReadOnlyList<InkDetailRequirement> DetailRequirements { get; init; }
        = System.Array.Empty<InkDetailRequirement>();

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

    /// <summary>技能页的星盘内容；其他页面为 null。</summary>
    public InkSkillDiscModel? Disc { get; init; }

    /// <summary>工作页的全员矩阵内容；其他页面为 null。</summary>
    public InkWorkModel? Work { get; init; }

    /// <summary>交易页三面板的内容（领地库存/市场库存/金钱）；其他页面为 null。</summary>
    public InkTradeModel? Trade { get; init; }

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
    /// 状态页：能力三段（生活 / 武器 / 流派）各自是否已摊开。
    /// 收起时每段只报最高的一条（见 <see cref="InkPageRow.IsTop"/>）。
    /// </summary>
    public IReadOnlyList<bool> AbilityOpen { get; init; }
        = new[] { false, false, false };

    /// <summary>状态页：能力列展开态的滚动首行（段标题也算一行）。</summary>
    public int StatusAbilityFirst { get; init; }

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
        InkPage.Status => new InkPageInfo(page, "状态"),
        InkPage.Skills => new InkPageInfo(page, "技能"),
        InkPage.Schedule => new InkPageInfo(page, "日程"),
        InkPage.Quest => new InkPageInfo(page, "任务"),
        InkPage.CombatLog => new InkPageInfo(page, "战斗日志"),
        _ => new InkPageInfo(page, "?"),
    };
}

/// <summary>
/// 详情下卡的一条需求：条件文本 + 是否未达成。
/// 渲染端在清单前只画一次「需求」标签，各条条件列在其下。
/// </summary>
public sealed class InkDetailRequirement
{
    public string Text { get; init; } = "";

    /// <summary>未达成：条件行用次级色，并在其后标「（未达成）」。</summary>
    public bool Unmet { get; init; }
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

    /// <summary>角色三页：量表当前值（体力/气力/好感/心情）。</summary>
    public float? MeterValue { get; init; }

    /// <summary>角色三页：量表最大值。</summary>
    public float MeterMax { get; init; }

    /// <summary>角色三页：是否是分组标题。</summary>
    public bool IsHeading { get; init; }

    /// <summary>
    /// 角色三页：本分组里数值最高的一项。能力列（生活 / 武器 / 流派）收敛态下
    /// 每列只画这一项，展开后才画全列。
    /// </summary>
    public bool IsTop { get; init; }
}

/// <summary>页面行能执行的动作。</summary>
public enum InkPageAction
{
    None = 0,

    /// <summary>制作一份。</summary>
    Craft,

    /// <summary>日程：给当前角色的某一段下委派。TargetNumber 是时段，TargetId 是模式名。</summary>
    AssignTask,

    /// <summary>日程：取消这一段已排好的工作（清空该时段）。TargetNumber 是时段。</summary>
    CancelTask,
}
