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

    /// <summary>卧室类房间里拧门锁。Index 不用。</summary>
    RoomLock,

    /// <summary>过界去隔壁区域。Index 是目标区域 Id（3×3 拼图里的扁平编号）。</summary>
    CrossRegion,

    /// <summary>存储页：把一份物品放进设施。Index 是存储行下标。</summary>
    StorageStore,

    /// <summary>存储页：从设施取一份物品。Index 是存储行下标。</summary>
    StorageTake,

    /// <summary>存储页：切换该物品是否允许收进设施。Index 是存储行下标。</summary>
    StorageFilterToggle,

    /// <summary>存储页：切换整个品类是否允许收进设施。Index 是品类行下标。</summary>
    StorageCategoryToggle,

    /// <summary>存储页：关掉设施交互页。</summary>
    StorageClose,

    /// <summary>左下角的页面入口。Index 是 InkViewModel.PageEntries 的下标。</summary>
    PageEntry,

    /// <summary>角色栏翻页。</summary>
    PageNext,

    /// <summary>角色栏翻页（向回）。</summary>
    PagePrev,

    /// <summary>交流面板的起始类别。Index 是 SocialCategories 下标。</summary>
    SocialCategory,

    /// <summary>右栏日志开关：收起/还原设施段。</summary>
    LogToggle,

    /// <summary>遮盖层的分支选项。Index 是选项下标。</summary>
    OverlayChoice,

    /// <summary>点遮盖层空白处推进一句。</summary>
    OverlayAdvance,

    /// <summary>战斗：选定敌方目标。Index 是敌方单位 Id。</summary>
    CombatTarget,

    /// <summary>战斗：选定目标列。Index 是列号 1-4。</summary>
    CombatColumn,

    /// <summary>战斗：行动面板类别选择（0=攻击, 1=技能, 2=道具, 3=防御）。</summary>
    CombatCategory,

    /// <summary>战斗：释放技能。Index 是 battle.Menu() 中的下标。</summary>
    CombatSkill,

    /// <summary>战斗：使用道具。Index 是可用道具下标。</summary>
    CombatItem,

    /// <summary>聊天层右上角的入口（状态/技能/日程）。Index 是入口下标。</summary>
    ChatEntry,

    /// <summary>子页面列表：选中一行。Index 是行下标。</summary>
    PageSelect,

    /// <summary>技能页：点开一项技能。Index 是行下标。</summary>
    SkillPick,

    /// <summary>技能页：点击扇区标签或扇区聚焦放大。Index 是扇区序号（0..5）。</summary>
    SkillSectorPick,

    /// <summary>技能页：重置扇区聚焦，视角平滑缩小返回全盘全景。</summary>
    SkillSectorReset,

    /// <summary>技能页：星盘右上角按钮（左上实心三角），切换至左侧/上一瓦片技能。</summary>
    SkillTilePrev,

    /// <summary>技能页：星盘右上角按钮（右下实心三角），切换至右侧/下一瓦片技能。</summary>
    SkillTileNext,

    /// <summary>技能页：切换至上一扇区。</summary>
    SkillSectorPrev,

    /// <summary>技能页：切换至下一扇区。</summary>
    SkillSectorNext,

    /// <summary>日程页：选中一个时段。Index 是时段下标。</summary>
    ScheduleSlot,

    /// <summary>日程页：选中房间网格里的一间房。Index 是房间 Id。</summary>
    ScheduleRoom,

    /// <summary>日程页：把当前时段排到这件设施上。Index 是设施 Id。</summary>
    ScheduleFacility,

    /// <summary>日程页：取消该时段的工作安排（时段卡右上角的取消按钮）。Index 是时段下标。</summary>
    CancelTask,

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

    /// <summary>列表滑条：点轨道把滑块跳到该处（点击坐标由屏幕层取）。</summary>
    ScrollJump,

    /// <summary>列表滑条：按住滑块拖动。Index 是列表代号（见 HubScreen 的分派）。</summary>
    ScrollThumb,

    /// <summary>子页面列表翻页。Index 0 上一页、1 下一页。</summary>
    PageScroll,

    /// <summary>主界面底部的世界/领地层切换。</summary>
    HubWorld,

    /// <summary>主界面底部的任务页入口。</summary>
    HubQuest,

    /// <summary>主界面底部的系统入口（设置/存读档）。</summary>
    HubSystem,

    /// <summary>角色面板右上角的状态页入口按钮。</summary>
    CharStatus,

    /// <summary>角色面板右上角的技能页入口按钮。</summary>
    CharSkills,

    /// <summary>日程页左侧列表切换选中的成员。Index 是 CharacterId。</summary>
    ScheduleMember,

    /// <summary>主界面的工作入口（角色面板与行动面板之间的空位）。</summary>
    HubWork,

    /// <summary>观察四周：点击行动面板直接退出观察态。</summary>
    ObserveExit,

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

    /// <summary>开发页：中下操作面板的一行按钮。Index 是操作行下标（含 InkAction 与分派下标）。</summary>
    DevAction,

    /// <summary>开发页：点左下「待安装的房间」里的一行，选中它并进入网格选位。Index 是行下标。</summary>
    DevPickRoomRow,

    /// <summary>开发页：点邻近的未开发房间格，弹开拓确认窗。Index 是房间格下标。</summary>
    DevOpenConfirm,

    /// <summary>开发页：开拓确认窗——开拓。</summary>
    DevConfirmAccept,

    /// <summary>开发页：开拓确认窗——取消。</summary>
    DevConfirmCancel,

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

    /// <summary>交易页：选中左栏一行（领地库存，卖出的对象）。Index 是行下标。</summary>
    TradePickHeld,

    /// <summary>交易页：选中右栏一行（市场库存，买入的对象）。Index 是行下标。</summary>
    TradePickMarket,

    /// <summary>
    /// 交易页：唯一一枚交易键。选中右栏（市场库存）即买入，
    /// 选中左栏（领地库存）即卖出——方向由选中栏决定，不设两个键。
    /// </summary>
    TradeRun,

    /// <summary>
    /// 遮盖的遮挡矩形：吃掉落在其上的点击、不做任何事。
    /// 命中纯几何，被盖住的控件点不到是因为这块先接走了点击——
    /// 不是按层禁用，也不是跨容器拦截。
    /// </summary>
    BlockClick,

    /// <summary>通用居中弹窗：纯展示/无阻塞控件时点击画面任何部分推进下一个弹窗或结束。</summary>
    ModalAdvance,

    /// <summary>通用居中弹窗：选项按钮。Index 是选项下标。</summary>
    ModalChoice,

    /// <summary>通用居中弹窗：输入框点击聚焦。</summary>
    ModalInputFocus,

    /// <summary>
    /// 状态页：点能力列的标题，在「只报最高一项」与「列全」之间切换。
    /// 三列呼应（点任一列都切换整块），所以 Index 不用。
    /// </summary>
    StatusAbilityToggle,
}

/// <summary>
/// 一个可交互元素。绘制与命中判定共用同一份列表，
/// 因此“画成灰色却还能点”这类不一致在结构上不可能出现：
/// Enabled 同时决定外观与是否可点。
///
/// Rect 是包围盒（悬停高亮与整体落位用）；
/// Polygon 非空时命中判定改用它——三角瓦片这类非矩形热区靠它精确命中，
/// 否则相邻瓦片的外接矩形会互相抢点击。
/// </summary>
/// <summary>
/// 一个可交互元素。绘制与命中判定共用同一份列表，
/// 因此“画成灰色却还能点”这类不一致在结构上不可能出现：
/// Enabled 同时决定外观与是否可点。
/// <see cref="Value"/> 是按钮右侧要显示的数值（消耗时间 / 预计伤害等），
/// 渲染器据此把按钮分成左右两区；空串即不分区、按老样子画。
/// </summary>
public readonly record struct InkWidget(
    Rect2 Rect,
    InkAction Action,
    int Index,
    bool Enabled,
    string Label,
    Vector2[]? Polygon = null,
    string Value = "")
{
    /// <summary>点是否落在本元素上。有 Polygon 走多边形内判定，否则走矩形。</summary>
    public bool Contains(Vector2 point)
    {
        if (Polygon == null || Polygon.Length < 3)
            return Rect.HasPoint(point);
        return InPolygon(point, Polygon);
    }

    /// <summary>多边形内判定：射线穿越计数（奇内偶外），凹多边形也适用。</summary>
    private static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if (a.Y > p.Y != b.Y > p.Y
                && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>
/// 界面分层。
/// </summary>
public enum InkLayer
{
    Base = 0,
    Page = 1,
    Overlay = 2,
    Modal = 3,
}
