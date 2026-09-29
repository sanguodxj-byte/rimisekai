using Godot;

namespace Rimisekai.Ink;

/// <summary>可交互元素的类别，命中后按它分派到对应的 HubSession 调用。</summary>
public enum InkAction
{
    /// <summary>地图格。Index 由 InkLayout.CellIndex 打包行列。</summary>
    MapCell,

    /// <summary>“此处”的设施。Index 是设施 Id。</summary>
    Fixture,

    /// <summary>角色卡。Index 是角色 Id。</summary>
    Card,

    /// <summary>交流选项。Index 是 InkViewModel.SocialActions 的下标。</summary>
    Social,

    /// <summary>行动选项。Index 是 InkViewModel.PlaceActions 的下标。</summary>
    Place,

    /// <summary>设施行动。Index 是 InkViewModel.FixtureActions() 的下标。</summary>
    FixtureAction,

    /// <summary>存储页：把一份物品放进设施。Index 是存储行下标。</summary>
    StorageStore,

    /// <summary>存储页：从设施取一份物品。Index 是存储行下标。</summary>
    StorageTake,

    /// <summary>存储页：切换该物品是否允许收进设施。Index 是存储行下标。</summary>
    StorageFilterToggle,

    /// <summary>存储页：关掉设施交互页。</summary>
    StorageClose,

    /// <summary>左下角的页面入口。Index 是 InkViewModel.PageEntries 的下标。</summary>
    PageEntry,

    /// <summary>角色栏翻页。</summary>
    PageNext,

    /// <summary>遮盖层的分支选项。Index 是选项下标。</summary>
    OverlayChoice,

    /// <summary>点遮盖层空白处推进一句。</summary>
    OverlayAdvance,

    /// <summary>聊天层右上角的入口（状态/技能/日程）。Index 是入口下标。</summary>
    ChatEntry,

    /// <summary>子页面列表：选中一行。Index 是行下标。</summary>
    PageSelect,

    /// <summary>子页面搜索框。点击聚焦，键盘输入过滤列表。</summary>
    PageSearchBox,

    /// <summary>子页面排序按钮。Index 是当前排序字段；点击切换字段或方向。</summary>
    PageSortCycle,

    /// <summary>子页面筛选按钮。Index 是筛选档位。</summary>
    PageFilterPick,

    /// <summary>子页面右栏详情里的动作按钮。Index 是详情动作下标。</summary>
    PageRow,

    /// <summary>子页面页签：切到另一个全屏页。Index 是 InkPageTabs.All 的下标。</summary>
    PageTab,

    /// <summary>子页面列表翻页。Index 0 上一页、1 下一页。</summary>
    PageScroll,

    /// <summary>工作页：点矩阵里的一个格子。Index 打包行列（行*1000+列）。</summary>
    WorkCell,

    /// <summary>主界面底部的世界/领地层切换。</summary>
    HubWorld,

    /// <summary>主界面底部的任务页入口。</summary>
    HubQuest,

    /// <summary>主界面底部的系统入口（设置/存读档）。</summary>
    HubSystem,

    /// <summary>主界面的工作入口（角色面板与行动面板之间的空位）。</summary>
    HubWork,

    /// <summary>关闭子页面。</summary>
    PageClose,

    /// <summary>开发页：点房间网格里的房间。Index 是房间格下标。</summary>
    DevPickRoom,

    /// <summary>开发页：点设施行选中。Index 是设施行下标。</summary>
    DevPickFacility,
    /// <summary>开发页：把选中的未放置设施放进选中的房间。Index 是PlaceFacility相关下标。</summary>
    DevPlaceFacility,

    /// <summary>开发页：选中未放置房间，进入网格选位放置。Index 是ArmPlaceRoom相关下标。</summary>
    DevArmPlaceRoom,

    /// <summary>开发页：把待放置房间放到该网格空格。Index 是 CellIndex。Index 是PlaceRoomAt相关下标。</summary>
    DevPlaceRoomAt,


    /// <summary>开发页：详情框里的动作按钮（拆除/放置等）。Index 是动作下标。</summary>
    DevDetailAction,

    /// <summary>开发页：拆除非空房间（房间格右上角的 X）。Index 是房间格下标。</summary>
    DevDemolishRoom,

    /// <summary>开发模式：退出，回到普通主界面。</summary>
    DevExit,

    /// <summary>开发页：建造一行可建造设施（花材料放进选中房间）。Index 是建造行下标。</summary>
    DevBuildFacility,

    /// <summary>开发页：建造一行房间目录（花材料，建成后未放置）。Index 是目录行下标。</summary>
    DevBuildRoom,

    /// <summary>开发页：拆掉一个已有设施（行尾 X）。Index 是已有设施行下标。</summary>
    DevRemoveFacility,

    /// <summary>点左上角标题，打开改名弹窗。</summary>
    RenameOpen,

    /// <summary>改名弹窗：确定。</summary>
    RenameConfirm,

    /// <summary>改名弹窗：取消。</summary>
    RenameCancel,
}

/// <summary>
/// 一个可交互元素。绘制与命中判定共用同一份列表，
/// 因此“画成灰色却还能点”这类不一致在结构上不可能出现：
/// Enabled 同时决定外观与是否可点。
/// </summary>
public readonly record struct InkWidget(
    Rect2 Rect,
    InkAction Action,
    int Index,
    bool Enabled,
    string Label,
    InkLayer Layer = InkLayer.Base);

/// <summary>
/// 界面分层。元素表始终包含各层的全部元素（渲染器靠它取标签），
/// 但命中判定只认当前最上层，因此下层不会被误点。
/// </summary>
public enum InkLayer
{
    /// <summary>主界面：地图、日志、角色、行动。</summary>
    Base = 0,

    /// <summary>子页面（库存/交易/制作/开发）。</summary>
    Page = 1,

    /// <summary>地图遮盖层（对话/剧情/插画）。</summary>
    Overlay = 2,

    /// <summary>改名弹窗，最高层。</summary>
    Modal = 3,
}
