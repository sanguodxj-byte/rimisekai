using System.Collections.Generic;
using Godot;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>
/// 界面自身持有的纯视图状态（不进 Core，不进存档）。
/// 模型构建时读它，输入处理时改它。
/// </summary>
public sealed class InkUiState
{
    /// <summary>当前段落累积的台词句（同一说话人的连续对话逐句追加）。</summary>
    public List<string> OverlayLines { get; } = new();

    /// <summary>段落里最近一次见到的遮罩实例（去重用）。</summary>
    public MapOverlay? OverlayLinesFor { get; set; }

    /// <summary>该实例里最近一次记下的台词（去重用）。</summary>
    public string OverlayLinesForText { get; set; } = "";

    /// <summary>当前段落的说话人；换人即换段。</summary>
    public string OverlayParagraphSpeaker { get; set; } = "";
    /// <summary>角色栏当前页，从 0 起。</summary>
    public int CardPage { get; set; }

    /// <summary>右栏设施段是否已收起（日志铺满）。</summary>
    public bool LogExpanded { get; set; }

    /// <summary>交流面板点开的类别下标；-1 表示起始态（未展开子项）。</summary>
    public int SocialCategory { get; set; } = -1;

    /// <summary>观察四周：地图面板换成本房间插画（再点一次收回）。</summary>
    public bool Observing { get; set; }

    /// <summary>进入观察态时所在的房间；换房自动收回。</summary>
    public int ObservedRoomId { get; set; } = -1;

    /// <summary>当前打开的页面；None 表示没开。</summary>
    public InkPage OpenPage { get; set; } = InkPage.None;

    /// <summary>子页面左列表选中的行下标；-1 表示未选（构建时取第一行）。</summary>
    public int PageSelected { get; set; } = -1;

    /// <summary>技能页：当前打开的技能 Id；空表示未选。</summary>
    public string SkillSelectedId { get; set; } = "";

    /// <summary>技能页当前聚焦的扇区序号（0..5），-1 表示未聚焦全盘视图。</summary>
    public int SkillFocusedSector { get; set; } = -1;

    /// <summary>技能盘视角缩放动画进度/当前缩放。</summary>
    public float SkillDiscZoom { get; set; } = 1.0f;

    /// <summary>技能盘视角平移中心当前位置。</summary>
    public Vector2 SkillDiscPivot { get; set; } = new(584f, 558f);

    /// <summary>技能盘视角旋转对齐当前角度（弧度）。</summary>
    public float SkillDiscRotation { get; set; } = 0.0f;

    /// <summary>子页面列表的页首行下标（滚动窗口起点）。</summary>
    public int PageFirst { get; set; }

    /// <summary>日程页：房间网格里选中的房间 Id；-1 未选。</summary>
    public int ScheduleRoomId { get; set; } = -1;

    /// <summary>日程页：当前选中的成员 Id；未设为 -1（默认当前角色或玩家本人）。</summary>
    public int ScheduleMemberId { get; set; } = -1;

    /// <summary>日程页成员列表的滚动首行。</summary>
    public int ScheduleMemberScrollRows { get; set; }

    /// <summary>
    /// 状态页：能力三段（生活 / 武器 / 流派）各自是否已摊开。
    /// 收起时每段只报最高的一条；点该段的段头条把本级以下摊开，再点一次收回。
    /// </summary>
    public bool[] StatusAbilityOpen { get; } = { false, false, false };

    /// <summary>状态页：能力列展开态的滚动首行（段标题也算一行）。</summary>
    public int StatusAbilityScrollRows { get; set; }

    // ---------- 列表滑条：各列表的滚动行号（首行下标） ----------

    /// <summary>设施交互页存储行的滚动首行。</summary>
    public int StorageScrollRows { get; set; }

    /// <summary>开发页设施列表的滚动首行。</summary>
    public int DevFacilityScrollRows { get; set; }

    /// <summary>开发页房间列表的滚动首行。</summary>
    public int DevRoomScrollRows { get; set; }

    /// <summary>开发页中下操作面板的滚动首行。</summary>
    public int DevActionScrollRows { get; set; }

    /// <summary>日程页设施列表的滚动首行。</summary>
    public int ScheduleFacilityScrollRows { get; set; }

    /// <summary>交易页左栏（领地库存）的滚动首行。</summary>
    public int TradeHeldFirst { get; set; }

    /// <summary>交易页右栏（市场库存）的滚动首行。</summary>
    public int TradeMarketFirst { get; set; }

    /// <summary>
    /// 交易页左栏选中的行下标；-1 未选。两栏互斥——选中一栏即清掉另一栏，
    /// 因此同一时刻只有一个交易对象（买看右栏，卖看左栏）。
    /// </summary>
    public int TradeSelectedHeld { get; set; } = -1;

    /// <summary>交易页右栏选中的行下标；-1 未选。</summary>
    public int TradeSelectedMarket { get; set; } = -1;


    /// <summary>子页面列表的搜索词；空表示不过滤。</summary>
    public string PageSearch { get; set; } = "";

    /// <summary>子页面搜索框是否聚焦；聚焦时键盘输入进搜索词。</summary>
    public bool SearchFocus { get; set; }

    /// <summary>子页面筛选档位下标；0 恒为“全部”。</summary>
    public int PageFilter { get; set; }

    /// <summary>子页面排序字段下标。</summary>
    public int PageSort { get; set; }

    /// <summary>子页面排序方向；false 升序，true 降序。</summary>
    public bool PageSortDesc { get; set; }

    /// <summary>开发模式：四面板容器不变，内容切成领地编辑。</summary>
    public bool DevMode { get; set; }

    /// <summary>开发模式：左上网格里选中的格下标（-1 未选）。</summary>
    public int DevSelectedCell { get; set; } = -1;

    /// <summary>开发模式：选中的设施 Id（-1 未选）。</summary>
    public int DevSelectedFacility { get; set; } = -1;

    /// <summary>
    /// 开发模式：开拓确认弹窗要开拓的格子线性下标（<c>row*5+col</c>，-1 表示弹窗关着）。
    /// 用格子而不是房间 Id——「空地」还没有房间实体。
    /// </summary>
    public int DevConfirmCell { get; set; } = -1;

    /// <summary>战斗选中的目标 Id。</summary>
    public int SelectedTargetId { get; set; } = -1;

    /// <summary>战斗选中的目标列（1-4）。</summary>
    public int SelectedTargetColumn { get; set; } = 0;

    /// <summary>战斗行动面板展开的二级页面：-1 无二级页面（攻击/防御直接生效或选目标），1 技能，2 道具。</summary>
    public int CombatCategory { get; set; } = -1;

    /// <summary>当前选定装配的技能 Id（等待选定目标后执行）。</summary>
    public string ArmedSkillId { get; set; } = "";

    /// <summary>开发模式：左下「待安装的房间」列表里选中的行下标（-1 未选）。</summary>
    public int DevSelectedRoom { get; set; } = -1;

    /// <summary>开发模式：待放进选中房间的设施 Id（-1 无）。</summary>
    public int DevPlacingFacility { get; set; } = -1;

    /// <summary>开发模式：待放到网格上的房间 Id（-1 无）。</summary>
    public int DevPlacingRoom { get; set; } = -1;

    /// <summary>改名弹窗是否打开。</summary>
    public bool Renaming { get; set; }

    /// <summary>改名输入框里的文本。</summary>
    public string RenameText { get; set; } = "";

    /// <summary>打字机已显示的字符数。换句时由界面归零。</summary>
    public int TypeRevealed { get; set; }

    /// <summary>打字机正在播的那句话，用来判断是否需要重置进度。</summary>
    public string TypeLine { get; set; } = "";

    /// <summary>上一次操作的反馈文本。</summary>
    public string Notice { get; set; } = "";

    /// <summary>鼠标当前悬停的元素下标，-1 表示无。</summary>
    public int Hovered { get; set; } = -1;

    /// <summary>通用居中自适应弹窗会话（正在显示的弹窗队列）。</summary>
    public InkModalSession? ModalSession { get; set; }
}
