using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Housing;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>“此处”一行：设施本身加上座位占用情况与占用中的角色。</summary>
public readonly record struct FixtureRow(FixtureView Fixture, int Used, int Capacity, IReadOnlyList<CharacterCard>? Occupants = null);

/// <summary>战斗状态角标：增益/减益一字为记，角标数字是剩余轮数。</summary>
public readonly record struct StatusMark(string Name, int Rounds, bool Buff);

/// <summary>角色卡：角色本身、是否选中，体力值，以及战斗中的状态角标。</summary>
public readonly record struct CardView(
    CharacterCard Card, bool Selected, int Stamina = 0, int MaxStamina = 0,
    StatusMark[]? Statuses = null);

/// <summary>
/// 设施交互页的一行：某物品在设施里/背包里各有多少，以及是否被过滤器排除。
/// 界面据此画“放入 / 取出 / 过滤”三个小按钮。
/// </summary>
public readonly record struct InkStorageRow(string ItemId, int InStorage, int InBag, bool Excluded, string CategoryLabel);

/// <summary>存储配置页的品类开关行。</summary>
public readonly record struct InkStorageCategoryRow(string DefName, string Label, bool Accepted);

/// <summary>遮盖层的一份只读快照，避免绘制期反复读 Hub 状态。</summary>
public sealed class OverlayView
{
    public OverlayKind Kind { get; init; }
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public string IllustrationId { get; init; } = "";
    public bool Waiting { get; init; }
    public IReadOnlyList<OverlayChoice> Choices { get; init; } = Array.Empty<OverlayChoice>();

    /// <summary>说话人 Id。-1 表示旁白，没有对应角色。</summary>
    public int SpeakerId { get; init; } = -1;

    /// <summary>说话人立绘的资源路径。</summary>
    public string PortraitPath { get; init; } = "";

    /// <summary>说话人对玩家的好感度与分档。旁白时为 null。</summary>
    public int? Favor { get; init; }
    public string BondLabel { get; init; } = "";

    /// <summary>说话人当前心情（0-100）。旁白时为 null。</summary>
    public int? Mood { get; init; }

    /// <summary>
    /// 打字机已显示的字符数。等于 Text 长度即整句显示完。
    /// 由界面按帧推进，模型只承载当前进度。
    /// </summary>
    public int Revealed { get; init; }

    /// <summary>当前段落累积的台词句（同一遮罩实例内逐句追加，末句跟随打字机）。</summary>
    public IReadOnlyList<string> OverlayLines { get; init; } = Array.Empty<string>();

    /// <summary>整句是否已显示完。</summary>
    public bool FullyRevealed => Revealed >= Text.Length;
}

/// <summary>
/// 主界面的一次完整呈现内容：面板数据 + 可交互元素表。
///
/// 关键约束：Widgets 同时决定“画成什么样”与“能不能点”，
/// 绘制与命中判定都只读这一份，因此两者不可能不一致。
/// </summary>
public sealed class InkHubModel
{
    public string PlaceTitle { get; init; } = "";
    /// <summary>顶栏右侧三个状态项（天气/时刻/金钱），只画数值、无标签，位置固定。</summary>
    public System.Collections.Generic.IReadOnlyList<InkViewModel.InkHeaderItem> HeaderItems { get; init; }
        = System.Array.Empty<InkViewModel.InkHeaderItem>();

    /// <summary>玩家当前所在房间 Id，-1 表示尚未定位。</summary>
    public int PlayerRoomId { get; init; } = -1;

    /// <summary>战斗形态：渲染器据此撤下面板标题、头像按威胁等级上移。</summary>
    public bool InCombat { get; init; }

    /// <summary>战斗转场/下沉动画进度（0=据点原位，1=战斗展开完成）。</summary>
    public float CombatT { get; init; } = 1f;

    /// <summary>自动战斗是否开启（中间按钮「自动」的选中态）。</summary>
    public bool AutoBattle { get; init; }

    /// <summary>战斗行动面板的选中类别：0=攻击, 1=技能, 2=道具, 3=防御。</summary>
    public int CombatCategory { get; init; } = -1;

    /// <summary>当前装配的战斗技能 Id（已点行动按钮/技能，正等待选择目标）。</summary>
    public string ArmedSkillId { get; init; } = "";

    /// <summary>战斗形态顶部按钮文案（战斗开始 / 觉醒 / 觉醒中 / 离开，严禁百分比文字）。</summary>
    public string CombatTopLabel { get; init; } = "战斗开始";
    public bool CombatTopEnabled { get; init; } = true;
    public bool CombatTopSelected { get; init; } = false;

    /// <summary>觉醒累计进度 0..1，由界面以墨线量规纯视觉绘制，禁止百分比数值。</summary>
    public float AwakeningRatio { get; init; }

    /// <summary>当前轮到谁行动的单位 Id，头上画大钝角呼吸指示三角形。</summary>
    public int PendingActorId { get; init; } = -1;

    /// <summary>当前区域的房间，按格子坐标索引；空格为 null。</summary>
    private Room?[] Grid { get; init; } = new Room?[InkLayout.GridCols * InkLayout.GridRows];

    /// <summary>按格子坐标取房间。越界返回 null。</summary>
    public Room? MapAt(int col, int row) =>
        InkLayout.InGrid(col, row) ? Grid[InkLayout.CellIndex(col, row)] : null;

    /// <summary>
    /// 房间 Id → 该房间的在场角色（含玩家）。地图格据此画角色标识。
    /// 位置未知（RoomId &lt; 0）的角色不入表，不会凭空出现在某间房里。
    /// </summary>
    private IReadOnlyDictionary<int, IReadOnlyList<CharacterCard>> Occupants { get; init; } =
        new Dictionary<int, IReadOnlyList<CharacterCard>>();

    /// <summary>取某间房的在场角色；没有则返回空表。</summary>
    public IReadOnlyList<CharacterCard> OccupantsIn(int roomId) =>
        Occupants.TryGetValue(roomId, out var list) ? list : Array.Empty<CharacterCard>();

    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
    public string HereName { get; init; } = "";
    public IReadOnlyList<FixtureRow> Fixtures { get; init; } = Array.Empty<FixtureRow>();

    /// <summary>右栏设施段是否已收起（日志铺满）。</summary>
    public bool LogExpanded { get; init; }

    /// <summary>交流面板点开的类别下标；-1 为起始态。</summary>
    public int SocialCategory { get; init; } = -1;

    /// <summary>接触阶梯当前应显示的子项数（解锁跟随存档旗标）。</summary>
    public int SocialTouchVisible { get; init; } = 1;

    /// <summary>接触阶梯当前应显示的子项数：连续解锁数，起步恒含首步摸头。</summary>
    public static int SocialTouchVisibleCount(InkViewModel vm)
    {
        var children = InkViewModel.SocialCategories[1].Children;
        var count = 1;
        while (count < children!.Length && vm.Hub.TouchStepUnlocked(count))
            count++;
        return count;
    }

    public IReadOnlyList<CardView> Cards { get; init; } = Array.Empty<CardView>();
    public int CardPage { get; init; }
    public int CardPageCount { get; init; }

    public string SocialTitle { get; init; } = "";

    /// <summary>玩家当前所坐设施的名字；没坐则空串。行动面板在设施态显示它。</summary>
    public string FixtureTitle { get; init; } = "";

    /// <summary>右下角是否显示设施行动（坐在某件设施上）。</summary>
    public bool ShowFixtureActions { get; init; }

    /// <summary>右下角是否显示“交流”一组。false 时显示“行动”。两者互斥。</summary>
    public bool ShowSocial { get; init; }

    /// <summary>左上角标题：领地 / 世界 / 任务地点名 / POI 名。</summary>
    public string MapTitle { get; init; } = "";

    /// <summary>当前位置标准名，形如“中央·庭院”。</summary>
    public string PlaceName { get; init; } = "";

    /// <summary>当前打开的页面；None 表示没开。</summary>
    public InkPage OpenPage { get; init; }

    /// <summary>地图网格当前是否停在世界层（决定入口文案与格子点击语义）。</summary>
    public bool WorldLayer { get; init; }

    /// <summary>左下角入口当前展示的页面清单（有对话对象时含状态页）。</summary>
    public IReadOnlyList<InkPage> PageEntries { get; init; } = Array.Empty<InkPage>();

    /// <summary>设施交互页是否开着（点设施行动“打开货架”后铺在左上角）。</summary>
    public bool StorageOpen { get; init; }

    /// <summary>设施交互页存储行的窗口首行（滑条用，构建时夹好）。</summary>
    public int StorageFirst { get; init; }

    /// <summary>设施交互页的标题（设施名 + 容量）。</summary>
    public string StorageTitle { get; init; } = "";

    /// <summary>设施交互页的行：物品名、设施内数量、背包内数量、是否被过滤排除。</summary>
    public IReadOnlyList<InkStorageRow> StorageRows { get; init; } = Array.Empty<InkStorageRow>();
    public IReadOnlyList<InkStorageCategoryRow> StorageCategories { get; init; } = Array.Empty<InkStorageCategoryRow>();

    /// <summary>开发模式：四面板容器不变，内容切成领地编辑。</summary>
    public bool DevMode { get; init; }

    /// <summary>场景演出进行中：插画铺满地图框，聊天层半透明叠在上面。</summary>
    public bool SceneMode { get; init; }

    /// <summary>当前演出的场景标题（如"大人的房间"）。</summary>
    public string SceneTitle { get; init; } = "";

    /// <summary>场景插画闸门链（Core 链状判断）的结论：插画当前该不该盖住网格。</summary>
    public bool SceneIllustrationCover { get; init; }

    /// <summary>观察四周最短链：玩家点了观察四周且未换房，地图换本房间插画。</summary>
    public bool ObservingRoom { get; init; }

    /// <summary>所在房间声明的插画路径；空 = 无专属插画，退回占位。</summary>
    public string ObservingRoomIllustrationPath { get; init; } = "";

    /// <summary>当前所在/观察的房间名称（插画智能匹配兜底用）。</summary>
    public string ObservedRoomName { get; init; } = "";

    /// <summary>当前世界时间小时数（0..23），供昼夜插画差分使用。</summary>
    public int Hour { get; init; }

    /// <summary>开发模式的内容（房间网格/设施列表/详情）。</summary>
    public InkDevModel? Dev { get; init; }

    /// <summary>打开页面的内容。OpenPage 为 None 时是空模型。</summary>
    public InkPageModel? Page { get; init; }

    /// <summary>改名弹窗是否打开。</summary>
    public bool Renaming { get; init; }

    /// <summary>改名输入框里当前的文本。</summary>
    public string RenameText { get; init; } = "";

    /// <summary>改名入口是否可用（领地等级达标）。</summary>
    public bool CanRename { get; init; }

    /// <summary>通用居中弹窗当前展示的单页，null 表示无弹窗。</summary>
    public InkModalPage? ModalPage { get; init; }

    public OverlayView? Overlay { get; init; }

    /// <summary>可交互元素，顺序即命中优先级（靠前者优先）。</summary>
    public IReadOnlyList<InkWidget> Widgets { get; init; } = Array.Empty<InkWidget>();

    /// <summary>鼠标当前悬停的元素下标，-1 表示无。</summary>
    public int Hovered { get; init; } = -1;

    /// <summary>按类别与序号取元素；没有则返回 null。渲染器据此取“可点/置灰”状态。</summary>
    public InkWidget? Find(InkAction action, int index)
    {
        foreach (var widget in Widgets)
        {
            if (widget.Action == action && widget.Index == index)
                return widget;
        }
        return null;
    }

    /// <summary>
    /// 命中判定：纯几何 + 注册逆序。后注册的控件画在上面，因此先命中；
    /// 被遮盖层真正盖住的控件点不到，是因为它的矩形被上层的遮挡矩形先接走——
    /// 而不是靠“按层禁用”。禁止任何跨容器/按层的输入拦截。
    /// </summary>
    public InkWidget? Hit(Vector2 point)
    {
        for (var i = Widgets.Count - 1; i >= 0; i--)
        {
            var widget = Widgets[i];
            if (widget.Enabled && widget.Contains(point))
                return widget;
        }
        return null;
    }

    /// <summary>按界面状态构建本帧内容。</summary>
    public static InkHubModel Build(InkViewModel vm, InkUiState ui)
    {
        var hub = vm.Hub;
        var widgets = new List<InkWidget>(64);

        if (vm.Combat != null)
        {
            // 战斗形态纯净分支：只注册战斗所需的部件，绝无任何 BlockClick 遮罩！
            BuildCards(vm, ui.CardPage, widgets);
            BuildPageEntries(vm, widgets);

            var battle = vm.Combat.Battle;

            string topLabel;
            bool topEnabled = true;
            bool topSelected = false;

            if (!battle.BattleBegan)
            {
                topLabel = "战斗开始";
            }
            else if (battle.AwakeningActive)
            {
                topLabel = "觉醒中";
                topSelected = true;
            }
            else if (battle.IsAwakeningReady && battle.Outcome == CombatOutcome.Ongoing)
            {
                topLabel = "觉醒";
                topSelected = true;
                topEnabled = true;
            }
            else
            {
                topLabel = "觉醒";
                topEnabled = false; // 满值后才可点击；战斗分出胜负后置灰，移除离开按钮（击杀后自动结算）
            }

            var awakeningRatio = (float)battle.AwakeningGauge / Rimisekai.Combat.Battle.MaxAwakening;

            var cweRect = new Rect2(InkLayout.WorkEntry.Position + InkLayout.CombatPanelSink, InkLayout.WorkEntry.Size);
            widgets.Add(new InkWidget(cweRect, InkAction.HubWork, 0, topEnabled, topLabel));

            BuildCombatActions(vm, ui, widgets);

            // 操作逻辑：点行动按钮/技能之后才激活目标选择。未装配时不挂载目标点击热区。
            var armed = !string.IsNullOrEmpty(ui.ArmedSkillId)
                ? battle.Lookup(ui.ArmedSkillId)
                : null;
            InkCombatRenderer.BuildCombatWidgets(battle, new Rect2(48f, 150f, 1824f, 552f), armed, widgets);

            // 战后结算弹窗：在战斗场景末尾弹出，置于战斗界面最上层接收推进点击
            if (ui.ModalSession?.IsActive == true)
            {
                var modal = ui.ModalSession.Current!;
                var bgAction = modal.HasInteractiveControls ? InkAction.BlockClick : InkAction.ModalAdvance;
                widgets.Add(new InkWidget(
                    new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
                    bgAction, 0, true, ""));
                BuildModal(modal, widgets);
            }

            return new InkHubModel
            {
                InCombat = true,
                CombatT = vm.CombatT,
                AutoBattle = battle.AutoBattle,
                PlaceTitle = $"「{battle.PlaceName}」",
                CombatCategory = ui.CombatCategory,
                ArmedSkillId = ui.ArmedSkillId,
                CombatTopLabel = topLabel,
                CombatTopSelected = topSelected,
                CombatTopEnabled = topEnabled,
                AwakeningRatio = awakeningRatio,
                PendingActorId = battle.CurrentActor?.Id ?? battle.PendingActor?.Id ?? -1,
                ModalPage = ui.ModalSession?.Current,
                Widgets = widgets,
                Cards = BuildCardViews(vm, 0),
            };
        }

        var grid = BuildGrid(vm);

        // 场景演出：插画铺满地图框，聊天层半透明叠在上面。
        // 锁定由图层完成——演出中命中判定只在 Overlay 层里找。
        var sceneMode = hub.ScenePlaying;
        if (!vm.StorageOpen)
        {
            BuildMapTitle(vm, widgets);
            BuildMap(vm, ui, widgets);
        }
        BuildFixtures(vm, widgets);
        BuildCards(vm, ui.CardPage, widgets);
        widgets.Add(new InkWidget(
            InkLayout.LogToggleArrow(ui.LogExpanded), InkAction.LogToggle, 0, true, "日志开关"));
        BuildPageEntries(vm, widgets);
        var weRect = vm.Combat != null
            ? new Rect2(InkLayout.WorkEntry.Position + InkLayout.CombatPanelSink, InkLayout.WorkEntry.Size)
            : InkLayout.WorkEntry;
        var weLabel = vm.Combat != null ? "战斗开始" : "工作安排";
        widgets.Add(new InkWidget(weRect, InkAction.HubWork, 0, true, weLabel));
        BuildActions(vm, ui, widgets);

        if (vm.Combat != null)
        {
            var battle = vm.Combat.Battle;
            var armed = battle.Menu().Count > 0 ? battle.Menu()[0] : null;
            InkCombatRenderer.BuildCombatWidgets(battle, new Rect2(48f, 150f, 1824f, 552f), armed, widgets);
        }

        // 页面模型只建一次：渲染器与控件表共用同一份，
        // 避免两条路径对同一页面各算一套而对不上（角色三页曾因此取不到排序字段）。
        var pageModel = ui.DevMode || ui.OpenPage == InkPage.None
            ? null
            : BuildPageModel(vm, ui);

        // 全屏页/开发模式：整幅画布都被它占着。命中纯几何——这块遮挡矩形接走
        // 落在其上的点击，因此下层基础控件点不到；页面自身的控件注册在它之后，
        // 叠在上面照常可点。这不是按层禁用，而是“被盖住就点不到”。
        if (ui.DevMode || ui.OpenPage != InkPage.None)
        {
            widgets.Add(new InkWidget(
                new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
                InkAction.BlockClick, 0, true, ""));
        }

        // 开发页：构建五块内容（网格/设施/待安装房间/操作面板/详情），并装入模型供渲染器使用。
        InkDevModel? dev = null;
        if (ui.DevMode)
        {
            dev = InkPageBuilder.Build(vm, InkPage.Develop,
                new InkPageQuery(ui.DevSelectedCell, ui.DevSelectedRoom, ui.DevSelectedFacility,
                    ui.PageSearch, ui.SearchFocus, ui.PageFilter, ui.PageSort, ui.PageSortDesc,
                    ui.DevPlacingFacility, ui.DevPlacingRoom,
                    ConfirmCell: ui.DevConfirmCell, ActionFirst: ui.DevActionScrollRows)).Dev;
            BuildDevPage(dev, ui, widgets);
        }
        else if (pageModel != null)
            BuildPage(pageModel, widgets);

        // 设施交互页：玩家点了设施行动（如“打开货架”）后，在左上角铺开操作界面。
        // 它盖住地图网格，所以建好它的控件后就不再建地图那块的。
        var storageRows = (IReadOnlyList<InkStorageRow>)Array.Empty<InkStorageRow>();
        var storageFirst = 0;
        var storageCategories = (IReadOnlyList<InkStorageCategoryRow>)Array.Empty<InkStorageCategoryRow>();
        var storageTitle = "";
        if (vm.StorageOpen)
        {
            storageTitle = $"{vm.StorageName()}　{vm.StorageCapacityText()}";
            var rows = new List<InkStorageRow>();
            foreach (var row in vm.StorageRows())
                rows.Add(new InkStorageRow(row.ItemId, row.InStorage, row.InBag,
                    !vm.StorageAccepts(row.ItemId), vm.ItemCategoryLabel(row.ItemId)));
            storageRows = rows;
            storageFirst = Mathf.Clamp(ui.StorageScrollRows, 0, Mathf.Max(0, rows.Count - 1));
            var cats = new List<InkStorageCategoryRow>();
            foreach (var cat in vm.StorageCategoryRows())
                cats.Add(new InkStorageCategoryRow(cat.DefName, cat.Label, cat.Accepted));
            storageCategories = cats;
            // 设施页铺在地图面板上：整块面板先垫遮挡矩形，页内控件叠在其上。
            widgets.Add(new InkWidget(InkLayout.MapPanel, InkAction.BlockClick, 0, true, ""));
            BuildStoragePage(vm, rows, cats, ui.StorageScrollRows, widgets);
        }

        // 全屏子页面盖住整个屏幕：打开期间聊天层连同它的入口一起让位，
        // 关掉页面再回到对话（Hub 里的对话状态不动）。
        var paged = ui.OpenPage != InkPage.None;
        var overlay = paged
            ? null
            : sceneMode ? SceneOverlay(vm, hub, ui)
            : hub.Overlay == null ? null : Snapshot(vm, hub.Overlay, ui);
        if (overlay != null)
        {
            // 聊天层/场景演出盖住地图区与行动面板：两块先垫遮挡矩形，
            // 层内控件（推进热区、入口、分支）叠在其上。
            widgets.Add(new InkWidget(InkLayout.MapPanel, InkAction.BlockClick, 0, true, ""));
            widgets.Add(new InkWidget(InkLayout.ActPanel, InkAction.BlockClick, 0, true, ""));
            BuildOverlayWidgets(overlay, widgets);
        }

        if (ui.Renaming)
        {
            // 弹窗遮住整个画面：遮挡矩形先注册，弹窗按钮后注册（叠在上面）。
            widgets.Add(new InkWidget(
                new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
                InkAction.BlockClick, 0, true, ""));
            BuildRename(ui, widgets);
        }

        if (ui.ModalSession?.IsActive == true)
        {
            var modal = ui.ModalSession.Current!;
            // 通用弹窗遮住整个画面：
            // 若有交互控件（输入框或选项），点击空白处阻断误触（BlockClick）；
            // 若为纯展示/叙事/结算，点击画面任意部分直接推进（ModalAdvance）。
            var bgAction = modal.HasInteractiveControls ? InkAction.BlockClick : InkAction.ModalAdvance;
            widgets.Add(new InkWidget(
                new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
                bgAction, 0, true, ""));
            BuildModal(modal, widgets);
        }

        var fixtures = BuildFixtureRows(vm);
        var pageInfo = PageOf(vm.CardsHere().Count, ui.CardPage);

        return new InkHubModel
        {
            InCombat = vm.Combat != null,
            CombatT = vm.CombatT,
            PlaceTitle = vm.PlaceTitle,
            HeaderItems = vm.HeaderItems(),
            PlayerRoomId = hub.PlayerRoomId,
            Grid = grid,
            Occupants = BuildOccupants(vm),
            LogLines = vm.LogLines(),
            HereName = vm.HereName(),
            Fixtures = fixtures.Rows,
            Cards = BuildCardViews(vm, pageInfo.Page),
            CardPage = pageInfo.Page,
            CardPageCount = pageInfo.PageCount,
            LogExpanded = ui.LogExpanded,
            SocialCategory = ui.SocialCategory,
            SocialTouchVisible = SocialTouchVisibleCount(vm),
            SocialTitle = vm.SocialTitle(),
            ShowSocial = vm.ShowSocial && !sceneMode && overlay == null,
            FixtureTitle = vm.CurrentFixtureName(),
            ShowFixtureActions = !vm.ShowSocial && !sceneMode && overlay == null && vm.FixtureActions().Count > 0,
            MapTitle = vm.MapTitle(),
            PlaceName = vm.PlaceName(),
            OpenPage = ui.OpenPage,
            WorldLayer = hub.Layer == MapLayer.World,
            PageEntries = InkViewModel.PageEntries,
            StorageOpen = vm.StorageOpen,
            StorageFirst = storageFirst,
            StorageTitle = storageTitle,
            StorageRows = storageRows,
            StorageCategories = storageCategories,
            DevMode = ui.DevMode,
            SceneMode = sceneMode,
            SceneTitle = hub.SceneTitle,
            SceneIllustrationCover = sceneMode && vm.Hub.SceneIllustrationCovers(),
            ObservingRoom = ui.Observing && ui.ObservedRoomId == vm.Hub.PlayerRoomId,
            ObservingRoomIllustrationPath = vm.Hub.ObservedRoomIllustration(),
            ObservedRoomName = vm.Hub.State.Territory.Rooms.Find(r => r.Id == vm.Hub.PlayerRoomId)?.Name ?? "",
            Hour = vm.Hub.State.Clock.Hour,
            Dev = dev,
            Page = pageModel,
            Renaming = ui.Renaming,
            RenameText = ui.RenameText,
            CanRename = vm.CanRenameTerritory,
            ModalPage = ui.ModalSession?.Current,
            Overlay = overlay,
            Widgets = widgets,
            Hovered = ui.Hovered,
        };
    }

    /// <summary>
    /// 选页面的构建器：状态/技能/日程针对当前对话对象，其余走通用的列表页构建器。
    /// </summary>
    private static InkPageModel? BuildPageModel(InkViewModel vm, InkUiState ui)
    {
        Rimisekai.Character.CharacterState? who = null;
        if (ui.OpenPage == InkPage.Schedule)
        {
            who = (ui.ScheduleMemberId >= 0 ? vm.Hub.State.Roster.Find(ui.ScheduleMemberId) : null)
                ?? vm.ChatPartner()
                ?? vm.Hub.State.Roster.Master;
        }
        else
        {
            who = vm.ChatPartner() ?? vm.Hub.State.Roster.Master;
        }

        var character = InkCharacterPageBuilder.Build(vm, ui.OpenPage, who,
            ui.SkillSelectedId, ui.PageSelected, ui.ScheduleRoomId, ui.ScheduleFacilityScrollRows,
            ui.SkillFocusedSector, ui.SkillDiscZoom, ui.SkillDiscPivot, ui.SkillDiscRotation,
            ui.StatusAbilityOpen, ui.StatusAbilityScrollRows, ui.ScheduleMemberScrollRows);
        if (character != null)
            return character;

        return InkPageBuilder.Build(vm, ui.OpenPage,
            new InkPageQuery(ui.PageSelected, ui.PageSelected, ui.DevSelectedFacility, ui.PageSearch,
                ui.SearchFocus, ui.PageFilter, ui.PageSort, ui.PageSortDesc,
                ui.DevPlacingFacility, ui.DevPlacingRoom, ui.PageFirst,
                ui.TradeSelectedHeld, ui.TradeSelectedMarket,
                ui.TradeHeldFirst, ui.TradeMarketFirst,
                ConfirmCell: ui.DevConfirmCell, ActionFirst: ui.DevActionScrollRows,
                Notice: ui.Notice));
    }

    /// <summary>
    /// 列表滑条：点轨道把滑块跳到该处，按住滑块拖动。内容不溢出时不建。
    /// 轨道先注册、滑块后注册（后注册者先命中），Index 是列表代号（ScrollThumb 分派用）。
    /// </summary>
    private static void BuildScrollBar(List<InkWidget> widgets, int list,
        Rect2 panel, Rect2 rowsArea, int total, int visible, int first)
    {
        if (total <= visible || visible <= 0)
            return;
        var track = InkLayout.ScrollBarRect(panel, rowsArea);
        var thumbRatio = Mathf.Clamp((float)visible / total, 0f, 1f);
        var ratio = Mathf.Clamp((float)first / Mathf.Max(1, total - visible), 0f, 1f);
        var thumbH = Mathf.Clamp(track.Size.Y * thumbRatio, 28f, track.Size.Y);
        var thumbY = track.Position.Y + ratio * (track.Size.Y - thumbH);
        widgets.Add(new InkWidget(track, InkAction.ScrollJump, list, true, ""));
        widgets.Add(new InkWidget(
            new Rect2(track.Position.X, thumbY, track.Size.X, thumbH),
            InkAction.ScrollThumb, list, true, ""));
    }

    /// <summary>
    /// 日程页下半的控件：房间网格每格一个热区（Index 是房间 Id），
    /// 设施列表每行一个热区（Index 是设施 Id）。
    /// 与渲染器共用 <see cref="InkLayout"/> 的同一套矩形，画得出来就点得动。
    /// </summary>
    private static void BuildScheduleGrid(InkWorkModel work, List<InkWidget> widgets)
    {
        var memberArea = InkLayout.ScheduleMemberList;
        var memberVisible = InkLayout.ScheduleMemberVisibleRows;
        var memberFirst = Mathf.Clamp(work.MemberFirst, 0, Mathf.Max(0, work.Members.Count - memberVisible));
        for (var i = 0; i < memberVisible && memberFirst + i < work.Members.Count; i++)
        {
            var m = work.Members[memberFirst + i];
            var rect = InkLayout.ScheduleMemberRow(memberArea, i);
            widgets.Add(new InkWidget(
                rect, InkAction.ScheduleMember, m.TargetNumber, true, m.Name));
        }

        var grid = InkLayout.ScheduleGrid;
        var facilities = InkLayout.ScheduleFacilities;

        foreach (var room in work.Rooms)
        {
            if (!InkLayout.InGrid(room.X, room.Y))
                continue;
            widgets.Add(new InkWidget(
                InkLayout.ScheduleCell(grid, room.X, room.Y), InkAction.ScheduleRoom,
                room.Id, true, room.Name));
        }

        var visible = InkLayout.ScheduleFacilityVisibleRows;
        var first = Mathf.Clamp(work.FacilityFirst, 0, Mathf.Max(0, work.Facilities.Count - visible));
        for (var i = 0; i < visible && first + i < work.Facilities.Count; i++)
        {
            var row = work.Facilities[first + i];
            var rect = InkLayout.ScheduleFacilityRow(facilities, i);
            widgets.Add(new InkWidget(
                rect, InkAction.ScheduleFacility, row.TargetNumber, row.Enabled, row.Name));
        }
    }

    /// <summary>
    /// 场景演出摊成聊天层快照：说话人与当前行、分支走同一份 OverlayView，
    /// 插画作底透出来。演出没有自己的画法——叠在插画上的就是聊天界面。
    /// </summary>
    private static OverlayView SceneOverlay(InkViewModel vm, HubSession hub, InkUiState ui)
    {
        var speaker = hub.SceneLines.Count > 0 ? hub.SceneLines[^1].Speaker : "";
        var text = hub.SceneLines.Count > 0 ? hub.SceneLines[^1].Text : "";
        var who = vm.FindByName(speaker);

        // Index 即选项下标，点击回传 SceneChoose(index)。
        var choices = new List<OverlayChoice>(hub.SceneChoices.Count);
        for (var i = 0; i < hub.SceneChoices.Count; i++)
            choices.Add(new OverlayChoice(i, hub.SceneChoices[i].Label));

        return new OverlayView
        {
            Kind = OverlayKind.Dialogue,
            Speaker = speaker,
            Text = text,
            Waiting = choices.Count > 0,
            Choices = choices,
            SpeakerId = who?.Id ?? -1,
            PortraitPath = who == null ? "" : vm.PortraitPath(who.Name),
            Favor = who?.Condition.Favor,
            BondLabel = who == null ? "" : InkText.Bond(who.Condition.Bond),
            Mood = who?.Affect.Mood,
            Revealed = ui.TypeRevealed,
            OverlayLines = ui.OverlayLines,
        };
    }

    /// <summary>
    /// 聊天层的控件：等待分支时只有选项；否则右上角入口加整幅“点击推进”热区。
    /// 对话与场景演出共用同一套——画得出来的就是点得着的。
    /// </summary>
    private static void BuildOverlayWidgets(OverlayView overlay, List<InkWidget> widgets)
    {
        if (overlay.Waiting)
        {
            for (var i = 0; i < overlay.Choices.Count; i++)
            {
                widgets.Add(new InkWidget(
                    InkLayout.OverlayChoice(i, overlay.Choices.Count),
                    InkAction.OverlayChoice, i, true, overlay.Choices[i].Label));
            }
            return;
        }

        // 整幅“点击推进”热区先注册，右上角入口后注册——
        // 命中判定逆序取（后注册者画在上面、优先命中），入口压在热区之上才点得着。
        widgets.Add(new InkWidget(
            InkLayout.MapPanel, InkAction.OverlayAdvance, 0, true, ""));
        widgets.Add(new InkWidget(
            InkLayout.ActPanel, InkAction.OverlayAdvance, 0, true, ""));

        for (var i = 0; i < ChatEntries.Length; i++)
        {
            widgets.Add(new InkWidget(
                InkLayout.ChatEntry(i, ChatEntries.Length),
                InkAction.ChatEntry, i, true, InkPageModel.Info(ChatEntries[i]).Label));
        }
    }

    private static OverlayView Snapshot(InkViewModel vm, MapOverlay overlay, InkUiState ui)
    {
        // 按说话人名找回角色，好把立绘、好感度、心情一起带上。
        var who = vm.FindByName(overlay.Speaker);

        return new OverlayView
        {
            Kind = overlay.Kind,
            Speaker = overlay.Speaker,
            Text = overlay.Text,
            IllustrationId = overlay.IllustrationId,
            Waiting = overlay.Waiting,
            Choices = overlay.Choices,
            SpeakerId = who?.Id ?? -1,
            PortraitPath = who == null ? "" : vm.PortraitPath(who.Name),
            Favor = who?.Condition.Favor,
            BondLabel = who == null ? "" : InkText.Bond(who.Condition.Bond),
            Mood = who?.Affect.Mood,
            // 打字机进度：只对当前这句话，换句时由界面重置。
            Revealed = ui.TypeRevealed,
            OverlayLines = ui.OverlayLines,
        };
    }

    /// <summary>
    /// 把当前区域的房间铺进 5×5 网格。
    /// 只取 RegionId 与当前区域一致的房间，避免跨区域同坐标互相覆盖。
    /// </summary>
    private static Room?[] BuildGrid(InkViewModel vm)
    {
        var grid = new Room?[InkLayout.GridCols * InkLayout.GridRows];
        foreach (var room in vm.Rooms())
        {
            if (!InkLayout.InGrid(room.X, room.Y))
                continue;
            grid[InkLayout.CellIndex(room.X, room.Y)] = room;
        }
        return grid;
    }

    /// <summary>
    /// 把名册按所在房间归组，供地图格画角色标识。
    /// 用全量名册（Party）而不是同房卡（CardsHere），
    /// 否则别的房间里的角色永远上不了地图；位置未知者（RoomId &lt; 0）跳过。
    /// 组内顺序固定为名册顺序，因此同一批人的标识不会逐帧跳位。
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<CharacterCard>> BuildOccupants(InkViewModel vm)
    {
        var map = new Dictionary<int, List<CharacterCard>>();
        foreach (var card in vm.Cards())
        {
            if (card.RoomId < 0)
                continue;
            if (!map.TryGetValue(card.RoomId, out var list))
            {
                list = new List<CharacterCard>();
                map[card.RoomId] = list;
            }
            list.Add(card);
        }

        var result = new Dictionary<int, IReadOnlyList<CharacterCard>>(map.Count);
        foreach (var pair in map)
            result[pair.Key] = pair.Value;
        return result;
    }

    /// <summary>
    /// 状态页能力三段的内容（段头条行名 + 该段全部行）。摊平序号与渲染端同一口径。
    /// </summary>
    private static List<KeyValuePair<int, (string Name, List<InkPageRow> Rows)>> AbilityGroups(InkPageModel page)
    {
        var groups = new List<List<InkPageRow>> { new(), new(), new() };
        var names = new[] { "生活", "武器", "流派" };
        var group = -1;
        foreach (var r in page.Rows)
        {
            if (r.IsHeading)
            {
                if (r.Name == "生活") group = 0;
                else if (r.Name == "武器") group = 1;
                else if (r.Name == "流派") group = 2;
                continue;
            }
            if (group >= 0) groups[group].Add(r);
        }
        var result = new List<KeyValuePair<int, (string, List<InkPageRow>)>>();
        for (var g = 0; g < 3; g++)
            if (groups[g].Count > 0)
                result.Add(new KeyValuePair<int, (string, List<InkPageRow>)>(g, (names[g], groups[g])));
        return result;
    }

    // ---------- 遮盖层 ----------

    /// <summary>聊天层右上角的三个入口，顺序即按钮顺序。</summary>
    public static readonly InkPage[] ChatEntries =
    {
        InkPage.Status,
        InkPage.Skills,
        InkPage.Schedule,
    };

    // ---------- 子页面 ----------

    /// <summary>
    /// 页面 widget：管理三页全屏“左列表右详情”，角色三页交给角色渲染器。
    /// 页面内容由调用方建好的 <paramref name="page"/> 摊成，本方法只负责可点区域，
    /// 因此画出来的与点得到的永远一致。
    /// </summary>
    private static void BuildPage(InkPageModel page, List<InkWidget> widgets)
    {
        var tabs = InkPageTabs.For(page.Page);
        for (var t = 0; t < tabs.Length; t++)
            widgets.Add(new InkWidget(
                InkLayout.PageTab(t, tabs.Length), InkAction.PageTab,
                t, true, InkPageModel.Info(tabs[t]).Label));

        if (InkChatPages.Contains(page.Page))
        {
            // 技能页：热区与绘制变换完全同源（由 TransformDiscPolygon / TransformDiscRect 投影）
            // 1. 扇区标签热区：点击切换/聚焦至该扇区
            // 2. 技能节点热区：点击选中技能（聚焦时精准命中放大后的几何多边形）
            // 3. 返回全景热区：聚焦态下提供左上角返回按钮
            if (page.Page == InkPage.Skills && page.Disc != null)
            {
                var disc = page.Disc;
                var pivot = disc.ViewPivot;
                var zoom = disc.ViewZoom;
                var rotation = disc.ViewRotation;

                // 扇区标签
                for (var s = 0; s < disc.SectorLabels.Count; s++)
                {
                    var rect = InkLayout.TransformDiscRect(
                        InkLayout.SkillDiscSectorLabelRect(s), pivot, zoom, rotation);
                    if (!InkLayout.SkillDiscContent.Intersects(rect))
                        continue;
                    widgets.Add(new InkWidget(
                        rect, InkAction.SkillSectorPick, s, true, disc.SectorLabels[s]));
                }

                // 技能节点热区：**仅在扇区聚焦态下**才可点——全盘时只能点扇区标签放大，
                // 瓦片不响应点击（否则在全盘密集的小三角上误触）。超出星盘边框的自动剔除。
                for (var i = 0; i < disc.Tiles.Count && disc.FocusedSector >= 0; i++)
                {
                    var tile = disc.Tiles[i];
                    if (tile.Kind != InkSkillNodeKind.Skill)
                        continue;

                    // 聚焦态下仅响应聚焦扇区的节点
                    if (tile.Sector != disc.FocusedSector)
                        continue;

                    var poly = InkLayout.SkillDiscTilePolygon(tile.Sector, tile.Ring, tile.Col, tile.Upper);
                    var tPoly = InkLayout.TransformDiscPolygon(poly, pivot, zoom, rotation);
                    var clippedPoly = InkDraw.ClipPolygon(tPoly, InkLayout.SkillDiscContent);
                    if (clippedPoly.Length < 3)
                        continue;

                    var tBounds = InkLayout.PolygonBounds(clippedPoly);

                    widgets.Add(new InkWidget(
                        tBounds, InkAction.SkillPick, i, true, tile.Name, clippedPoly));
                }

                // 全盘模式（未聚焦特定扇区）：整个扇区作为大热区，点击扇区内任意位置直接放大聚焦该扇区
                if (disc.FocusedSector < 0)
                {
                    for (var s = 0; s < InkLayout.SkillDiscSectors; s++)
                    {
                        var wedgePoly = InkLayout.SkillDiscSectorWedgePolygon(s);
                        var wedgeBounds = InkLayout.PolygonBounds(wedgePoly);
                        var label = s < disc.SectorLabels.Count ? disc.SectorLabels[s] : $"扇区 {s}";
                        widgets.Add(new InkWidget(
                            wedgeBounds, InkAction.SkillSectorPick, s, true, label, wedgePoly));
                    }
                }

                // 聚焦态下的“返回全景”按钮
                if (disc.FocusedSector >= 0)
                {
                    widgets.Add(new InkWidget(
                        InkLayout.SkillDiscResetButtonRect,
                        InkAction.SkillSectorReset, 0, true, "全景"));
                }

                // 星盘右上角 SVG 矢量导航按钮（非简单长方形/正方形：左上实心三角 = 切换至左/上一瓦片，右下实心三角 = 切换至右/下一瓦片）
                var navRect = InkLayout.SkillDiscCornerNavButtonRect;
                var upperPoly = InkLayout.SkillDiscCornerNavTriangleUpper(navRect);
                var lowerPoly = InkLayout.SkillDiscCornerNavTriangleLower(navRect);

                // 底座背景垫底（点到两三角缝隙时默认步进）
                widgets.Add(new InkWidget(
                    navRect, InkAction.SkillTileNext, 0, true, "下一瓦片"));

                // 左上实心三角（在顶层，优先命中左侧瓦片）
                widgets.Add(new InkWidget(
                    InkLayout.PolygonBounds(upperPoly),
                    InkAction.SkillTilePrev, 0, true, "左侧瓦片", upperPoly));

                // 右下实心三角（在顶层，优先命中右侧瓦片）
                widgets.Add(new InkWidget(
                    InkLayout.PolygonBounds(lowerPoly),
                    InkAction.SkillTileNext, 0, true, "右侧瓦片", lowerPoly));
            }

            // 日程页：上排时段卡可选中，排了工作时右上角带取消按钮；下半房间网格与设施列表各一个热区。
	            if (page.Page == InkPage.Schedule)
	            {
	                for (var s = 0; s < page.Rows.Count && s < WorkSlot.Count; s++)
	                {
	                    var slotRect = InkLayout.ScheduleSlot(s);
	                    widgets.Add(new InkWidget(
	                        slotRect, InkAction.ScheduleSlot, s, true,
	                        page.Rows[s].Name));

	                    if (page.Rows[s].Value == "工作")
	                    {
	                        widgets.Add(new InkWidget(
	                            InkLayout.ScheduleSlotCancel(slotRect),
	                            InkAction.CancelTask, s, true, "取消"));
	                    }
	                }

	                if (page.Work != null)
	                    BuildScheduleGrid(page.Work, widgets);
	            }

            // 状态页：每条能力一个方框；段头条即开关——点它把本级以下摊开/收回。
            if (page.Page == InkPage.Status)
            {
                // 摊平序号与渲染端同源：三段顺序排，段头条在前，摊开的下级条跟在后面。
                var bar = 0;
                foreach (var grouping in AbilityGroups(page))
                {
                    if (bar >= InkLayout.AbilityVisibleBars)
                        break;
                    widgets.Add(new InkWidget(
                        InkLayout.AbilityBar(bar), InkAction.StatusAbilityToggle,
                        grouping.Key, true, grouping.Value.Name));
                    bar += 1 + (page.AbilityOpen[grouping.Key] ? grouping.Value.Rows.Count - 1 : 0);
                }

                // 摊开后超出可见条数出滑条：条区已让出 ScrollBarGutter，滑条不压条内文字。
                BuildScrollBar(widgets, (int)InkHubScreen.ScrollList.StatusAbility,
                    InkLayout.StatusAbilityArea, InkLayout.StatusAbilityArea,
                    InkLayout.StatusAbilityBarCount(page),
                    InkLayout.AbilityVisibleBars, page.StatusAbilityFirst);
            }
        }
        else if (page.Trade != null)
            BuildTradePage(page.Trade, widgets);
        else if (page.HasDetail)
        {
            // 面板定高（列表界面不做自动缩放）：左列表 + 右详情各占满整幅。
            var listPanel = InkLayout.FullListPanel;
            var listArea = listPanel.Grow(-InkLayout.Pad);
            var detailPane = InkLayout.FullDetailArea;

            // 控制行：搜索框、筛选档位、排序按钮。只读页面没有可查可排的内容。
            if (page.HasControls)
            {
                widgets.Add(new InkWidget(
                    InkLayout.SearchBox(listArea), InkAction.PageSearchBox,
                    0, true, "搜索"));
                for (var f = 0; f < page.Filters.Count; f++)
                    widgets.Add(new InkWidget(
                        InkLayout.FilterChip(listArea, f, page.Filters.Count), InkAction.PageFilterPick,
                        f, true, page.Filters[f]));
                if (page.Sorts.Count > 0)
                    widgets.Add(new InkWidget(
                        InkLayout.SortButton(listArea), InkAction.PageSortCycle,
                        page.ActiveSort, true, page.Sorts[page.ActiveSort]));
            }

            // 左列表：标准自顶向下顺序排列，点击只做选中，动作统一放右栏详情。
            var visible = InkLayout.ListVisibleRows(listArea);
            var first = page.ListFirst;
            var shown = Math.Min(page.Rows.Count - first, visible);
            for (var i = 0; i < shown; i++)
                widgets.Add(new InkWidget(
                    InkLayout.ListRow(listArea, i), InkAction.PageSelect,
                    first + i, true, page.Rows[first + i].Name));

            var rowsTop = InkLayout.ListRow(listArea, 0).Position.Y;
            var rowsArea = new Rect2(listArea.Position.X, rowsTop,
                listArea.Size.X, visible * InkLayout.PageRowStep);
            BuildScrollBar(widgets, (int)InkHubScreen.ScrollList.Main,
                listPanel, rowsArea, page.Rows.Count, visible, first);

            // 右详情：动作按钮固定在底端。
            for (var j = 0; j < page.DetailActions.Count; j++)
            {
                var rect = InkLayout.DetailButton(detailPane, j, page.DetailActions.Count);
                widgets.Add(new InkWidget(
                    rect, InkAction.PageRow, j, page.DetailActions[j].Enabled,
                    page.DetailActions[j].Name));
            }
        }

        widgets.Add(new InkWidget(
            InkLayout.FullPageClose, InkAction.PageClose, 0, true, "关闭"));
    }

    /// <summary>
    /// 交易页的控件：左栏领地库存、右栏市场库存各一行一个热区（点击即选中，
    /// 两栏互斥——同一时刻只有一个交易对象），中栏一枚交易键兼买卖。
    /// 行数超出可见区走滑条。与渲染器共用 <see cref="InkLayout"/> 的同一套矩形。
    /// </summary>
    private static void BuildTradePage(InkTradeModel trade, List<InkWidget> widgets)
    {
        BuildTradeColumn(widgets, InkLayout.TradePlayerPanel, trade.Held,
            trade.HeldFirst, InkAction.TradePickHeld, (int)InkHubScreen.ScrollList.TradeHeld);
        BuildTradeColumn(widgets, InkLayout.TradeMarketPanel, trade.Market,
            trade.MarketFirst, InkAction.TradePickMarket, (int)InkHubScreen.ScrollList.TradeMarket);

        widgets.Add(new InkWidget(
            InkLayout.TradeTradeButton, InkAction.TradeRun, 0, trade.CanRun, "交易"));
    }

    /// <summary>交易页一栏：可见行各一个热区（Index 是绝对行下标）加一条滑条。</summary>
    private static void BuildTradeColumn(List<InkWidget> widgets, Rect2 panel,
        IReadOnlyList<InkTradeRow> rows, int first, InkAction action, int list)
    {
        var visible = InkLayout.TradeVisibleRows(panel);
        var shown = Math.Min(rows.Count - first, visible);
        for (var i = 0; i < shown; i++)
        {
            widgets.Add(new InkWidget(
                InkLayout.TradeRow(panel, i), action, first + i, true, rows[first + i].Name));
        }

        var top = InkLayout.TradeRow(panel, 0).Position.Y;
        var rowsArea = new Rect2(panel.Position.X + 15f, top, panel.Size.X - 45f,
            visible * InkLayout.TradeRowStep - (InkLayout.TradeRowStep - InkLayout.TradeRowHeight));
        BuildScrollBar(widgets, list, panel, rowsArea, rows.Count, visible, first);
    }

    /// <summary>
    /// 设施交互页的控件：每行右侧三个小按钮（放入 / 取出 / 过滤），外加右上角关闭。
    /// 与渲染器共用同一份行数据，画得出来就一定点得动。
    /// </summary>
    private static void BuildStoragePage(InkViewModel vm, List<InkStorageRow> rows,
        List<InkStorageCategoryRow> categories, int first, List<InkWidget> widgets)
    {
        for (var i = 0; i < categories.Count; i++)
        {
            var row = InkLayout.StorageCategoryRow(i);
            var cat = categories[i];
            widgets.Add(new InkWidget(row, InkAction.StorageCategoryToggle,
                i, true, cat.Accepted ? $"收{cat.Label}" : $"不收{cat.Label}"));
        }
        var visible = InkLayout.FixtureVisibleRows;
        for (var i = 0; i < visible && first + i < rows.Count; i++)
        {
            var row = InkLayout.StorageRow(i);
            var entry = rows[first + i];
            // 背包里有才放得进去；设施里有才取得出来。
            widgets.Add(new InkWidget(InkLayout.StorageRowButton(row, 0), InkAction.StorageStore,
                first + i, entry.InBag > 0, "放入"));
            widgets.Add(new InkWidget(InkLayout.StorageRowButton(row, 1), InkAction.StorageTake,
                first + i, entry.InStorage > 0, "取出"));
            widgets.Add(new InkWidget(InkLayout.StorageRowButton(row, 2), InkAction.StorageFilterToggle,
                first + i, true, entry.Excluded ? "允许" : "禁止"));
        }
        var row0 = InkLayout.StorageRow(0);
        var storageArea = new Rect2(row0.Position.X, row0.Position.Y,
            row0.Size.X, row0.Size.Y + (visible - 1) * InkLayout.FixtureRowStep);
        BuildScrollBar(widgets, (int)InkHubScreen.ScrollList.Storage,
            InkLayout.FixturePanel, storageArea, rows.Count, visible, first);
        widgets.Add(new InkWidget(InkLayout.FixtureClose, InkAction.StorageClose, 0, true, "关闭"));
    }

    /// <summary>
    /// 开发页（2026-10-01 主人定）：五块容器不变，内容换成领地编辑。
    /// 左上网格（已开发格可点选中；邻近的未开发格点了弹开拓确认窗）、
    /// 右上当前房间的设施、左下待安装的房间、中下操作面板、右下详情。
    /// </summary>
    private static void BuildDevPage(InkDevModel? dev, InkUiState ui, List<InkWidget> widgets)
    {
        if (dev == null)
            return;

        // 左上面板：房间网格。整格先注册，格内 X 后注册（逆序命中时 X 优先）。
        for (var i = 0; i < dev.Rooms.Count; i++)
        {
            var cell = dev.Rooms[i];
            if (!InkLayout.InGrid(cell.X, cell.Y))
                continue;
            var cellRect = InkLayout.Cell(cell.X, cell.Y);
            // 两种格都用 dev.Rooms 的下标 i 派发——界面侧再从 cell.X/Y 换算出格子下标。
            widgets.Add(new InkWidget(
                cellRect, cell.Open ? InkAction.DevPickRoom : InkAction.DevOpenConfirm,
                i, true, cell.Name));
            if (cell.Open && cell.NonEmpty)
            {
                widgets.Add(new InkWidget(
                    InkLayout.DevRoomX(cellRect), InkAction.DevDemolishRoom,
                    i, cell.Removable, "拆除" + cell.Name));
            }
        }

        // 待安装房间：落点只能是「空房」（开拓出来的毛坯），不是任意空格。
        // 空房只能靠开拓得到，而开拓要求相邻——飞地问题就自然没了。
        if (dev.PlacingRoom >= 0)
        {
            foreach (var cell in dev.Rooms)
            {
                if (!cell.Open || !cell.Vacant || !InkLayout.InGrid(cell.X, cell.Y))
                    continue;
                widgets.Add(new InkWidget(
                    InkLayout.Cell(cell.X, cell.Y), InkAction.DevPlaceRoomAt,
                    cell.Id, true, "装进空房"));
            }
        }

        // 右上面板：**只列当前选中房间的设施**（窗口化+滑条）。
        var facVisible = Math.Min(dev.FacilityRows.Count, InkLayout.DevFacilityVisibleRows);
        var facFirst = Math.Clamp(ui.DevFacilityScrollRows, 0,
            Math.Max(0, dev.FacilityRows.Count - facVisible));
        for (var j = 0; j < facVisible; j++)
        {
            var row = dev.FacilityRows[facFirst + j];
            widgets.Add(new InkWidget(
                InkLayout.DevFacilityRow(j), InkAction.DevPickFacility,
                row.Id, true, row.Name));
        }
        BuildScrollBar(widgets, (int)InkHubScreen.ScrollList.DevFacility,
            InkLayout.LogPanel, InkLayout.DevFacilityArea,
            dev.FacilityRows.Count, facVisible, facFirst);

        // 左下面板：**只列已建但还没安装的房间**（窗口化+滑条）。点一行即选中并进入网格选位。
        var roomVisible = Math.Min(dev.RoomRows.Count, InkLayout.DevRoomVisibleRows);
        var roomFirst = Math.Clamp(ui.DevRoomScrollRows, 0,
            Math.Max(0, dev.RoomRows.Count - roomVisible));
        for (var j = 0; j < roomVisible; j++)
        {
            var row = dev.RoomRows[roomFirst + j];
            widgets.Add(new InkWidget(
                InkLayout.DevRoomRow(j), InkAction.DevPickRoomRow,
                roomFirst + j, true, row.Name));
        }
        BuildScrollBar(widgets, (int)InkHubScreen.ScrollList.DevRoom,
            InkLayout.CharPanel, InkLayout.DevRoomArea,
            dev.RoomRows.Count, roomVisible, roomFirst);

        // 中下面板：操作面板——所有操作按钮都在这儿。每行自己带着要派发的动作与下标。
        var actVisible = Math.Min(dev.ActionRows.Count, InkLayout.DevActionVisibleRows);
        var actFirst = Math.Clamp(dev.ActionFirst, 0,
            Math.Max(0, dev.ActionRows.Count - actVisible));
        for (var j = 0; j < actVisible; j++)
        {
            var row = dev.ActionRows[actFirst + j];
            widgets.Add(new InkWidget(
                InkLayout.DevActionRow(j), InkAction.DevAction,
                actFirst + j, row.Enabled, row.Name));
        }
        BuildScrollBar(widgets, (int)InkHubScreen.ScrollList.DevAction,
            InkLayout.WorkPanel, InkLayout.DevActionArea,
            dev.ActionRows.Count, actVisible, actFirst);

        // 退出开发常驻在大按钮位（主界面那一格放的是「工作安排」）。
        widgets.Add(new InkWidget(InkLayout.DevExitButton, InkAction.DevExit, 0, true, "退出开发"));

        // 开拓确认弹窗：遮挡矩形先注册，弹窗按钮后注册（叠在上面）。
        if (dev.ConfirmCell >= 0)
        {
            widgets.Add(new InkWidget(
                new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
                InkAction.BlockClick, 0, true, ""));
            widgets.Add(new InkWidget(
                InkLayout.DevConfirmButton(0), InkAction.DevConfirmAccept, 0, true, "开拓"));
            widgets.Add(new InkWidget(
                InkLayout.DevConfirmButton(1), InkAction.DevConfirmCancel, 0, true, "取消"));
        }
    }

    // ---------- 改名弹窗 ----------

    private static void BuildRename(InkUiState ui, List<InkWidget> widgets)
    {
        widgets.Add(new InkWidget(
            InkLayout.RenameButton(0), InkAction.RenameConfirm, 0, true, "确定"));
        widgets.Add(new InkWidget(
            InkLayout.RenameButton(1), InkAction.RenameCancel, 0, true, "取消"));
    }

    // ---------- 通用居中弹窗 ----------

    private static void BuildModal(InkModalPage modal, List<InkWidget> widgets)
    {
        var layout = InkLayout.CalculateModalLayout(modal);

        if (modal.Input != null)
        {
            widgets.Add(new InkWidget(
                layout.InputRect, InkAction.ModalInputFocus, 0, true, ""));
        }

        for (var i = 0; i < modal.Choices.Count && i < layout.ChoiceRects.Count; i++)
        {
            var choice = modal.Choices[i];
            widgets.Add(new InkWidget(
                layout.ChoiceRects[i], InkAction.ModalChoice, i, choice.Enabled, choice.Label));
        }
    }

    // ---------- 地图 ----------

    /// <summary>
    /// 左上角标题区。领地且等级达标时可点，点开改名弹窗。
    /// </summary>
    private static void BuildMapTitle(InkViewModel vm, List<InkWidget> widgets)
    {
        if (!vm.CanRenameTerritory)
            return;
        widgets.Add(new InkWidget(
            InkLayout.MapTitleRect, InkAction.RenameOpen, 0, true, "领地"));
    }

    private static void BuildMap(InkViewModel vm, InkUiState ui, List<InkWidget> widgets)
    {
        // 闸门链（任一环成立即让位插画，网格与热区不建）：
        // 长链——场景演出进行中且 Core 链全过（领地层/开演/未收演/有演员/演员同房/有内容）；
        // 最短链——玩家在本房间点了观察四周。
        // 末环：插画纹理在盘上。
        var observing = ui.Observing && ui.ObservedRoomId == vm.Hub.PlayerRoomId;
        if (vm.Hub.Layer != MapLayer.World
            && (vm.Hub.SceneIllustrationCovers() || observing)
            && InkIllustration.Get() != null)
            return;

        var world = vm.Hub.Layer == MapLayer.World;
        for (var row = 0; row < InkLayout.GridRows; row++)
        {
            for (var col = 0; col < InkLayout.GridCols; col++)
            {
                var room = vm.RoomAt(col, row);
                if (room == null)
                    continue;

                bool enabled;
                if (world)
                {
                    // 世界层：走得过去的格都可点（走过去；聚落进场、领地格回家）。
                    enabled = room.Open;
                }
                else
                {
                    // 主地图只负责走**已开放**的房间：开拓一律进开发页，
                    // 所以未开放的房间在这里不给任何入口（点了也不会开拓）。
                    enabled = room.Open
                        && (vm.IsPlayerRoom(room.Id) || vm.IsNeighbor(room));
                }

                widgets.Add(new InkWidget(
                    InkLayout.Cell(col, row), InkAction.MapCell,
                    InkLayout.CellIndex(col, row), enabled, room.Name));
            }
        }
    }

    // ---------- 此处 ----------

    private readonly record struct FixtureBuild(IReadOnlyList<FixtureRow> Rows);

    private static FixtureBuild BuildFixtureRows(InkViewModel vm)
    {
        var all = vm.Fixtures();
        var rows = new List<FixtureRow>(all.Count);
        foreach (var fixture in all)
        {
            var (used, cap) = vm.Seats(fixture);
            var workers = vm.WorkersAtFixture(fixture.Id);
            rows.Add(new FixtureRow(fixture, used, cap, workers));
        }
        return new FixtureBuild(rows);
    }

    private static FixtureBuild BuildFixtures(InkViewModel vm, List<InkWidget> widgets)
    {
        var built = BuildFixtureRows(vm);
        for (var i = 0; i < built.Rows.Count; i++)
        {
            var row = built.Rows[i];
            // 满员的设施不可用，与“座位已满”的提示保持一致。
            var enabled = row.Used < row.Capacity;
            widgets.Add(new InkWidget(
                InkLayout.FixtureCell(i, built.Rows.Count), InkAction.Fixture,
                row.Fixture.Id, enabled, row.Fixture.Name));
        }
        return built;
    }

    // ---------- 角色栏 ----------

    private readonly record struct PageInfo(int Page, int PageCount);

    private static PageInfo PageOf(int total, int requested)
    {
        var pageCount = Math.Max(1, (total + InkLayout.OthersPerPage - 1) / InkLayout.OthersPerPage);
        return new PageInfo(Math.Clamp(requested, 0, pageCount - 1), pageCount);
    }

    /// <summary>角色当前战斗状态的角标数据；不在战斗中则空。</summary>
    private static StatusMark[] MarksOf(InkViewModel vm, int characterId)
    {
        var battle = vm.Combat?.Battle;
        if (battle == null)
            return Array.Empty<StatusMark>();
        var member = battle.Members.Find(x => x.Id == characterId);
        if (member == null || member.Statuses.Count == 0)
            return Array.Empty<StatusMark>();
        return member.Statuses
            .Select(st => new StatusMark(st.Name, st.RoundsLeft,
                st.Category == StatusCategory.Buff))
            .ToArray();
    }

    private static IReadOnlyList<CardView> BuildCardViews(InkViewModel vm, int page)
    {
        var selected = vm.Selected();
        var views = new List<CardView>(InkLayout.CardsPerPage);

        // 玩家固定在第一页第一个；第二页起全是其余角色。
        var master = vm.Hub.State.Roster.Master;
        if (page == 0 && master != null)
            views.Add(new CardView(
                new CharacterCard(master.Id, master.Name, true, vm.Hub.PlayerRoomId, master.ThreatTier),
                false, master.Condition.Stamina, master.Condition.MaxStamina,
                MarksOf(vm, master.Id)));

        // 其余在场角色从第 2 格起排，翻页只翻这些人。
        var others = vm.CardsHere();
        var start = page * InkLayout.OthersPerPage;
        for (var i = start; i < others.Count && views.Count < InkLayout.CardsPerPage; i++)
        {
            var isSelected = selected.HasValue && selected.Value.Id == others[i].Id;
            var c = vm.FindById(others[i].Id);
            views.Add(new CardView(others[i], isSelected,
                c?.Condition.Stamina ?? 0, c?.Condition.MaxStamina ?? 0,
                MarksOf(vm, others[i].Id)));
        }
        return views;
    }

    private readonly record struct CardBuild(
        IReadOnlyList<CardView> Views, int Page, int PageCount);

    /// <summary>
    /// 构建战斗行动面板的 4x3 按钮部件：布局与日常行动面板完全一致。
    /// 第 0 列为四大类别：攻击(0) / 技能(1) / 道具(2) / 防御(3)；
    /// 第 1 列与第 2 列展开当前选中类别下的子项（最大 4x2 = 8 项）。
    /// </summary>
    private static void BuildCombatActions(InkViewModel vm, InkUiState ui, List<InkWidget> widgets)
    {
        var shift = InkLayout.CombatPanelSink;
        var categories = new[] { "攻击", "技能", "道具", "防御" };
        var curCat = Math.Clamp(ui.CombatCategory, 0, 3);

        // 第 0 列：四大类别按钮 (4 行)
        for (var r = 0; r < 4; r++)
        {
            var btnRect = InkLayout.SocialGridButton(0, r);
            var shifted = new Rect2(btnRect.Position + shift, btnRect.Size);
            widgets.Add(new InkWidget(shifted, InkAction.CombatCategory, r, true, categories[r],
                Value: CombatPredict(vm, r)));
        }

        var battle = vm.Combat?.Battle;
        if (battle == null)
            return;

        var pending = battle.Members.Find(m => m.Alive && m.IsPlayer) ?? battle.CurrentActor ?? battle.PendingActor;
        if (pending == null)
            return;

        var menu = battle.Menu();

        // 只有技能(1)和道具(2)有二级页面展开在第 1 列与第 2 列；攻击与防御没有二级页面
        if (ui.CombatCategory == 1) // 技能二级页面
        {
            var specialSkills = menu.Where(s => s.Id != Rimisekai.Combat.BattleSkills.AttackId && s.Id != Rimisekai.Combat.BattleSkills.GuardId).ToList();
            if (specialSkills.Count == 0)
            {
                var btnRect = InkLayout.SocialGridButton(1, 0);
                var shifted = new Rect2(btnRect.Position + shift, btnRect.Size);
                widgets.Add(new InkWidget(shifted, InkAction.CombatSkill, -1, false, "无可用技能"));
            }
            else
            {
                for (var i = 0; i < specialSkills.Count && i < 8; i++)
                {
                    var col = 1 + i / 4;
                    var row = i % 4;
                    var btnRect = InkLayout.SocialGridButton(col, row);
                    var shifted = new Rect2(btnRect.Position + shift, btnRect.Size);
                    var menuIdx = menu.IndexOf(specialSkills[i]);
                    var skill = specialSkills[i];
                    var damage = battle.Predict(pending.Id, skill.Id);
                    string val;
                    if (damage > 0)
                    {
                        val = skill.Kind == Rimisekai.Catalog.SkillKind.Heal ? $"+{damage}" : damage.ToString();
                    }
                    else if (skill.Kind == Rimisekai.Catalog.SkillKind.Buff && skill.StatusPercent != 0)
                    {
                        val = $"{skill.StatusPercent:+0;-0}%";
                    }
                    else
                    {
                        val = "";
                    }
                    widgets.Add(new InkWidget(shifted, InkAction.CombatSkill, menuIdx, true,
                        skill.Name, Value: val));
                }
            }
        }
        else if (ui.CombatCategory == 2) // 道具二级页面
        {
            var btnRect = InkLayout.SocialGridButton(1, 0);
            var shifted = new Rect2(btnRect.Position + shift, btnRect.Size);
            widgets.Add(new InkWidget(shifted, InkAction.CombatItem, -1, false, "无可用道具"));
        }
    }

    /// <summary>
    /// 战斗第 0 列类别按钮的右侧数值：攻击显示预计伤害；防御显示架势效果。
    /// </summary>
    private static string CombatPredict(InkViewModel vm, int category)
    {
        var battle = vm.Combat?.Battle;
        if (battle == null)
            return "";
        var actor = battle.Members.Find(m => m.Alive && m.IsPlayer)
                    ?? battle.CurrentActor ?? battle.PendingActor;
        if (actor == null)
            return "";

        if (category == 0) // 攻击：预计伤害/威力
        {
            var damage = battle.Predict(actor.Id, Rimisekai.Combat.BattleSkills.AttackId);
            return damage > 0 ? damage.ToString() : "";
        }
        if (category == 3) // 防御：架势效果
        {
            return "+50%";
        }
        return "";
    }


    private static CardBuild BuildCards(InkViewModel vm, int cardPage, List<InkWidget> widgets)
    {
        var info = PageOf(vm.CardsHere().Count, cardPage);
        var views = BuildCardViews(vm, info.Page);

        for (var i = 0; i < views.Count; i++)
        {
            var rect = InkLayout.Card(i);
            if (vm.Combat != null)
            {
                var lift = (views[i].Card.ThreatTier - 3) * 14f;
                rect = new Rect2(rect.Position + InkLayout.CombatPanelSink - new Vector2(0f, lift), rect.Size);
            }
            widgets.Add(new InkWidget(
                rect, InkAction.Card,
                views[i].Card.Id, true, views[i].Card.Name));
        }

        // 多页时给一对紧凑翻页钮（‹ ›），单页不画、也不再有页码。
        if (info.PageCount > 1)
        {
            widgets.Add(new InkWidget(
                InkLayout.CharPagerPrev, InkAction.PagePrev, 0, info.Page > 0, "上一页"));
            widgets.Add(new InkWidget(
                InkLayout.CharPagerNext, InkAction.PageNext, 0,
                info.Page < info.PageCount - 1, "下一页"));
        }

        if (vm.Combat == null)
        {
            widgets.Add(new InkWidget(
                InkLayout.CharPanelStatusButton, InkAction.CharStatus, 0, true, "状态"));
            widgets.Add(new InkWidget(
                InkLayout.CharPanelSkillButton, InkAction.CharSkills, 0, true, "技能"));
        }

        return new CardBuild(views, info.Page, info.PageCount);
    }

    private static void BuildPageEntries(InkViewModel vm, List<InkWidget> widgets)
    {
        var entries = InkViewModel.PageEntries;
        var inCombat = vm.Combat != null;
        var shift = inCombat ? InkLayout.CombatPanelSink : Vector2.Zero;

        for (var i = 0; i < entries.Length; i++)
        {
            var label = inCombat ? (i switch
            {
                0 => "自动",
                1 => "状态",
                2 => "日志",
                3 => "逃跑",
                _ => InkPageModel.Info(entries[i]).Label
            }) : InkPageModel.Info(entries[i]).Label;

            var rect = InkLayout.PageEntry(i);
            var shiftedRect = new Rect2(rect.Position + shift, rect.Size);

            widgets.Add(new InkWidget(
                shiftedRect, InkAction.PageEntry,
                i, true, label));
        }
    }

    /// <summary>中下操作面板入口：固定为领地四页（库存/交易/任务/开发），制作已删除并由任务替代。</summary>
    public static InkPage[] Entries(bool withCharacter = false) =>
        new[] { InkPage.Stock, InkPage.Trade, InkPage.Quest, InkPage.Develop };

    // ---------- 交流 / 行动 ----------

    /// <summary>
    /// 右下角三态，互斥：
    /// 选中角色时是“交流”；坐在设施上时是这件设施的日常行动；
    /// 都没占时是房间级行动（观察 + 导航）。
    /// 未显示的那几组不进元素表，因此不可能被点到。
    /// </summary>
    private static void BuildActions(InkViewModel vm, InkUiState ui, List<InkWidget> widgets)
    {
        // 演出中（场景演出或对话）行动面板转为演出态，不构建交互按钮
        if (vm.Hub.ScenePlaying || vm.Hub.Overlay != null)
            return;

        // 行动面板右上角设置按钮（字号与行动等大=26）
        widgets.Add(new InkWidget(InkLayout.ActPanelSettingsButton, InkAction.HubSystem, 0, true, "设置"));

        if (vm.ShowSocial)
        {
            var present = vm.SelectedIsPresent();

            // 左列起始钮：交谈/接触/观察/离开，占第 0 列。
            for (var i = 0; i < InkViewModel.SocialCategories.Length; i++)
            {
                widgets.Add(new InkWidget(
                    InkLayout.SocialGridButton(0, i), InkAction.SocialCategory,
                    i, present, InkViewModel.SocialCategories[i].Label));
            }

            // 点开带子表的类别后，子项从第 1 列起竖排（满四行才换列）；
            // Index 指回 SocialActions 主表，显示名用子项自己的。
            // 接触是启发式：解锁跟随存档旗标（成功做过上一步才显示下一步），
            // 其余类别全量显示。
            var category = ui.SocialCategory;
            if (category >= 0 && category < InkViewModel.SocialCategories.Length)
            {
                var children = InkViewModel.SocialCategories[category].Children;
                if (children != null)
                {
                    var visible = category == 1
                        ? SocialTouchVisibleCount(vm)
                        : children.Length;
                    for (var i = 0; i < visible; i++)
                    {
                        var child = children[i];
                        // “邀请”位跟随中换成“分开”，其余子项用静态表里的名字。
                        var label = child.Index == InkViewModel.InviteActionIndex
                            ? vm.InviteEntry().Label
                            : child.Label;
                        var minutes = vm.Hub.ActionMinutes((Hub.SocialAction)child.Index);
                        widgets.Add(new InkWidget(
                            InkLayout.SocialGridButton(1 + i / 4, i % 4), InkAction.Social,
                            child.Index, present, label,
                            Value: minutes > 0 ? $"{minutes}分" : ""));
                    }
                }
            }
            return;
        }

        // 坐在设施上：列出这件设施支持的日常行动（睡觉/吃饭/洗澡…）。
        // 行动清单直接来自 Core，画得出来就一定点得动。
        var fixture = vm.FixtureActions();
        if (fixture.Count > 0)
        {
            var currentName = vm.CurrentFixtureName();
            for (var i = 0; i < fixture.Count; i++)
            {
                var action = fixture[i];
                var label = action == Housing.ActionKind.Store && !string.IsNullOrEmpty(currentName)
                    ? $"打开{currentName}"
                    : InkText.ActionKind(action);
                var minutes = vm.Hub.FacilityActionMinutes(action);
                widgets.Add(new InkWidget(
                    InkLayout.PlaceButton(i, fixture.Count), InkAction.FixtureAction,
                    i, true, label,
                    Value: minutes > 0 ? $"{minutes}分" : ""));
            }
            AddRoomLockButton(widgets, vm, fixture.Count);
            AddCrossButton(widgets, vm, fixture.Count);
            return;
        }

        // 房间级行动：第 1 项固定为观察四周，第 2 项固定为前往世界（若在世界层显示返回领地）
        var placeMinutes = vm.Hub.ActionMinutes(Hub.SocialAction.Observe);
        var hasLock = vm.Hub.CurrentRoomLockable();
        var target = vm.Hub.CrossTargetRegion(vm.Hub.PlayerRoomId);
        var totalCount = 2 + (hasLock ? 1 : 0) + (target >= 0 ? 1 : 0);

        // 1. 观察四周
        widgets.Add(new InkWidget(
            InkLayout.PlaceButton(0, totalCount), InkAction.Place,
            0, true, "观察四周",
            Value: placeMinutes > 0 ? $"{placeMinutes}分" : ""));

        // 2. 前往世界（固定第二项）
        var worldLabel = vm.Hub.Layer == MapLayer.World ? "返回领地" : "前往世界";
        widgets.Add(new InkWidget(
            InkLayout.PlaceButton(1, totalCount), InkAction.HubWorld,
            0, true, worldLabel));

        // 3. 门锁（如有）
        if (hasLock)
        {
            widgets.Add(new InkWidget(
                InkLayout.PlaceButton(2, totalCount), InkAction.RoomLock,
                0, true, vm.Hub.CurrentRoomLocked() ? "解锁房门" : "锁上房门"));
        }

        // 4. 过界（如有）
        if (target >= 0)
        {
            var crossSlot = 2 + (hasLock ? 1 : 0);
            widgets.Add(new InkWidget(
                InkLayout.PlaceButton(crossSlot, totalCount), InkAction.CrossRegion,
                target, true, $"去往{Territory.RegionName(target)}"));
        }
    }

    /// <summary>
    /// 过界按钮。Index 直接是目标区域 Id（3×3 拼图里的扁平编号），
    /// 命中后由屏幕层转交给 CrossTo，不需要再查表。
    /// </summary>
    private static void AddCrossButton(List<InkWidget> widgets, InkViewModel vm, int count)
    {
        var target = vm.Hub.CrossTargetRegion(vm.Hub.PlayerRoomId);
        if (target < 0)
            return;
        var slot = count + (vm.Hub.CurrentRoomLockable() ? 1 : 0);
        widgets.Add(new InkWidget(
            InkLayout.PlaceButton(slot, slot + 1), InkAction.CrossRegion,
            target, true, $"去往{Territory.RegionName(target)}"));
    }

    /// <summary>
    /// 卧室类房间里多一个门锁钮：人在屋里才能拧，按钮上写当前状态的相反动作。
    /// </summary>
    private static void AddRoomLockButton(List<InkWidget> widgets, InkViewModel vm, int count)
    {
        if (!vm.Hub.CurrentRoomLockable())
            return;
        widgets.Add(new InkWidget(
            InkLayout.PlaceButton(count, count + 1), InkAction.RoomLock,
            0, true, vm.Hub.CurrentRoomLocked() ? "解锁房门" : "锁上房门"));
    }
}
