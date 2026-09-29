using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>“此处”一行：设施本身加上座位占用情况。</summary>
public readonly record struct FixtureRow(FixtureView Fixture, int Used, int Capacity);

/// <summary>角色卡：角色本身加上是否被选中。</summary>
public readonly record struct CardView(CharacterCard Card, bool Selected);

/// <summary>
/// 设施交互页的一行：某物品在设施里/背包里各有多少，以及是否被过滤器排除。
/// 界面据此画“放入 / 取出 / 过滤”三个小按钮。
/// </summary>
public readonly record struct InkStorageRow(string ItemId, int InStorage, int InBag, bool Excluded);

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

    /// <summary>说话人立绘的资源路径；空则退回线稿人形。</summary>
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
    public string HeaderRight { get; init; } = "";

    /// <summary>玩家当前所在房间 Id，-1 表示尚未定位。</summary>
    public int PlayerRoomId { get; init; } = -1;

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

    /// <summary>设施交互页的标题（设施名 + 容量）。</summary>
    public string StorageTitle { get; init; } = "";

    /// <summary>设施交互页的行：物品名、设施内数量、背包内数量、是否被过滤排除。</summary>
    public IReadOnlyList<InkStorageRow> StorageRows { get; init; } = Array.Empty<InkStorageRow>();

    /// <summary>开发模式：四面板容器不变，内容切成领地编辑。</summary>
    public bool DevMode { get; init; }

    /// <summary>是否显示工作入口面板（有两个以上可派角色时才有意义）。</summary>
    public bool ShowWorkPanel { get; init; }

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

    /// <summary>本帧操作产生的反馈文本，画在行动区底部。</summary>
    public string Notice { get; init; } = "";

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

    /// <summary>命中判定：只在最上层里找，返回第一个包含该点且可用的元素。</summary>
    public InkWidget? Hit(Vector2 point)
    {
        var top = TopLayer();
        foreach (var widget in Widgets)
        {
            if (widget.Layer == top && widget.Enabled && widget.Rect.HasPoint(point))
                return widget;
        }
        return null;
    }

    /// <summary>当前最上层的图层。命中判定只看它。</summary>
    public InkLayer TopLayer()
    {
        if (Renaming)
            return InkLayer.Modal;
        if (Overlay != null)
            return InkLayer.Overlay;
        if (OpenPage != InkPage.None || DevMode)
            return InkLayer.Page;
        return InkLayer.Base;
    }

    /// <summary>按界面状态构建本帧内容。</summary>
    public static InkHubModel Build(InkViewModel vm, InkUiState ui)
    {
        var hub = vm.Hub;
        var widgets = new List<InkWidget>(64);
        var grid = BuildGrid(vm);

        // 元素表始终包含全部图层：渲染器要靠它取标签与可用状态。
        // 命中判定只看最上层（见 Hit/TopLayer），因此下层不会被误点。
        if (!vm.StorageOpen)
        {
            BuildMapTitle(vm, widgets);
            BuildMap(vm, widgets);
        }
        BuildFixtures(vm, widgets);
        BuildCards(vm, ui.CardPage, widgets);
        BuildPageEntries(vm, widgets);
        widgets.Add(new InkWidget(InkLayout.HubWorldEntry, InkAction.HubWorld, 0, true,
            vm.Hub.Layer == MapLayer.World ? "领地" : "世界"));
        widgets.Add(new InkWidget(InkLayout.HubQuestEntry, InkAction.HubQuest, 0, true, "任务"));
        widgets.Add(new InkWidget(InkLayout.HubSystemEntry, InkAction.HubSystem, 0, true, "设置"));
        // 工作入口常显：面板空着比单列工作页更难看，单角色也能进工作页派唯一的活。
        widgets.Add(new InkWidget(InkLayout.WorkEntry, InkAction.HubWork, 0, true, "工作安排"));
        BuildActions(vm, widgets);

        // 页面模型只建一次：渲染器与控件表共用同一份，
        // 避免两条路径对同一页面各算一套而对不上（角色三页曾因此取不到排序字段）。
        var pageModel = ui.DevMode || ui.OpenPage == InkPage.None
            ? null
            : BuildPageModel(vm, ui);

        // 开发模式：构建四面板内容（房间网格/设施/详情），并装入模型供渲染器使用。
        InkDevModel? dev = null;
        if (ui.DevMode)
        {
            dev = InkPageBuilder.Build(vm, InkPage.Develop,
                new InkPageQuery(ui.PageSelected, ui.PageSelected, ui.DevSelectedFacility, ui.PageSearch,
                    ui.SearchFocus, ui.PageFilter, ui.PageSort, ui.PageSortDesc,
                    ui.DevPlacingFacility, ui.DevPlacingRoom)).Dev;
            BuildDevPage(dev, ui, widgets);
        }
        else if (pageModel != null)
            BuildPage(pageModel, widgets);

        // 设施交互页：玩家点了设施行动（如“打开货架”）后，在左上角铺开操作界面。
        // 它盖住地图网格，所以建好它的控件后就不再建地图那块的。
        var storageRows = (IReadOnlyList<InkStorageRow>)Array.Empty<InkStorageRow>();
        var storageTitle = "";
        if (vm.StorageOpen)
        {
            storageTitle = $"{vm.StorageName()}　{vm.StorageCapacityText()}";
            var rows = new List<InkStorageRow>();
            foreach (var row in vm.StorageRows())
            {
                rows.Add(new InkStorageRow(row.ItemId, row.InStorage, row.InBag, !vm.StorageAccepts(row.ItemId)));
                if (rows.Count >= InkLayout.FixtureVisibleRows)
                    break;
            }
            storageRows = rows;
            BuildStoragePage(vm, rows, widgets);
        }

        // 角色三页盖住整个屏幕：打开期间聊天层连同它的入口一起让位，
        // 关掉页面再回到对话（Hub 里的对话状态不动）。
        var chatPaged = InkChatPages.Contains(ui.OpenPage);
        if (hub.Overlay != null && !chatPaged)
            BuildOverlay(hub, widgets);

        if (ui.Renaming)
            BuildRename(ui, widgets);

        var fixtures = BuildFixtureRows(vm);
        var pageInfo = PageOf(vm.CardsHere().Count, ui.CardPage);

        return new InkHubModel
        {
            PlaceTitle = vm.PlaceTitle,
            HeaderRight = vm.HeaderRight(),
            PlayerRoomId = hub.PlayerRoomId,
            Grid = grid,
            Occupants = BuildOccupants(vm),
            LogLines = vm.LogLines(),
            HereName = vm.HereName(),
            Fixtures = fixtures.Rows,
            Cards = BuildCardViews(vm, pageInfo.Page),
            CardPage = pageInfo.Page,
            CardPageCount = pageInfo.PageCount,
            SocialTitle = vm.SocialTitle(),
            ShowSocial = vm.ShowSocial,
            FixtureTitle = vm.CurrentFixtureName(),
            ShowFixtureActions = !vm.ShowSocial && vm.FixtureActions().Count > 0,
            MapTitle = vm.MapTitle(),
            PlaceName = vm.PlaceName(),
            OpenPage = ui.OpenPage,
            WorldLayer = hub.Layer == MapLayer.World,
            PageEntries = Entries(vm.ChatPartner() != null),
            StorageOpen = vm.StorageOpen,
            StorageTitle = storageTitle,
            StorageRows = storageRows,
            DevMode = ui.DevMode,
            ShowWorkPanel = true,
            Dev = dev,
            Page = pageModel,
            Renaming = ui.Renaming,
            RenameText = ui.RenameText,
            CanRename = vm.CanRenameTerritory,
            Notice = ui.Notice,
            Overlay = hub.Overlay == null || chatPaged ? null : Snapshot(vm, hub.Overlay, ui),
            Widgets = widgets,
            Hovered = ui.Hovered,
        };
    }

    /// <summary>
    /// 选页面的构建器：状态/技能/日程针对当前对话对象，其余走通用的列表页构建器。
    /// </summary>
    private static InkPageModel? BuildPageModel(InkViewModel vm, InkUiState ui)
    {
        if (ui.OpenPage == InkPage.Work)
            return InkWorkPageBuilder.Build(vm, ui.WorkRow, ui.WorkColumn);

        var character = InkCharacterPageBuilder.Build(vm, ui.OpenPage, vm.ChatPartner(), ui.PageSelected);
        if (character != null)
            return character;

        return InkPageBuilder.Build(vm, ui.OpenPage,
            new InkPageQuery(ui.PageSelected, ui.PageSelected, ui.DevSelectedFacility, ui.PageSearch,
                ui.SearchFocus, ui.PageFilter, ui.PageSort, ui.PageSortDesc,
                ui.DevPlacingFacility, ui.DevPlacingRoom, ui.PageFirst));
    }

    /// <summary>
    /// 工作页的控件：每格一个热区（Index 打包行列），外加底部改档按钮。
    /// 与渲染器共用 <see cref="InkLayout"/> 的同一套矩形，画得出来就点得动。
    /// </summary>
    private static void BuildWorkPage(InkWorkModel work, List<InkWidget> widgets)
    {
        var columns = work.Columns.Count;
        for (var r = 0; r < work.Rows.Count; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                widgets.Add(new InkWidget(
                    InkLayout.WorkCell(r, c, columns), InkAction.WorkCell,
                    r * 1000 + c, true, work.Rows[r].Name, InkLayer.Page));
            }
        }
        for (var a = 0; a < work.DetailActions.Count; a++)
        {
            widgets.Add(new InkWidget(
                InkLayout.DetailButton(InkLayout.WorkDetail, a, work.DetailActions.Count),
                InkAction.PageRow, a, work.DetailActions[a].Enabled,
                work.DetailActions[a].Name, InkLayer.Page));
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

    // ---------- 遮盖层 ----------

    private static OverlayView? BuildOverlay(HubSession hub, List<InkWidget> widgets)
    {
        var overlay = hub.Overlay;
        if (overlay == null)
            return null;

        if (overlay.Waiting)
        {
            for (var i = 0; i < overlay.Choices.Count; i++)
            {
                widgets.Add(new InkWidget(
                    InkLayout.OverlayChoice(i, overlay.Choices.Count),
                    InkAction.OverlayChoice, i, true, overlay.Choices[i].Label,
                    InkLayer.Overlay));
            }
            return null;
        }

        // 右上角三个入口必须先注册：命中判定按注册顺序取第一个，
        // 后注册的整幅“点击推进”热区会把它们盖住，入口就点不着了。
        for (var i = 0; i < ChatEntries.Length; i++)
        {
            widgets.Add(new InkWidget(
                InkLayout.ChatEntry(i, ChatEntries.Length),
                InkAction.ChatEntry, i, true, InkPageModel.Info(ChatEntries[i]).Label,
                InkLayer.Overlay));
        }

        // 空白处点一下推进一句（或跳过打字机）。
        widgets.Add(new InkWidget(
            InkLayout.MapPanel, InkAction.OverlayAdvance, 0, true, "",
            InkLayer.Overlay));

        return null;
    }

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
        var tabs = InkPageTabs.All;
        for (var t = 0; t < tabs.Length; t++)
            widgets.Add(new InkWidget(
                InkLayout.PageTab(t, tabs.Length), InkAction.PageTab,
                t, true, InkPageModel.Info(tabs[t]).Label, InkLayer.Page));

        if (InkChatPages.Contains(page.Page))
        {
            // 角色三页的动作（日程派活）排在右下网格，与角色渲染器同一套坐标。
            for (var j = 0; j < page.DetailActions.Count; j++)
                widgets.Add(new InkWidget(
                    InkLayout.CharacterDetailButton(j, page.DetailActions.Count),
                    InkAction.PageRow, j, page.DetailActions[j].Enabled,
                    page.DetailActions[j].Name, InkLayer.Page));
        }
        else if (page.Work != null)
        {
            BuildWorkPage(page.Work, widgets);
        }
        else if (page.HasDetail)
        {
            var listArea = InkLayout.FullListArea;

            // 控制行：搜索框、筛选档位、排序按钮。只读页面没有可查可排的内容。
            if (page.HasControls)
            {
                widgets.Add(new InkWidget(
                    InkLayout.SearchBox(listArea), InkAction.PageSearchBox,
                    0, true, "搜索", InkLayer.Page));
                for (var f = 0; f < page.Filters.Count; f++)
                    widgets.Add(new InkWidget(
                        InkLayout.FilterChip(listArea, f, page.Filters.Count), InkAction.PageFilterPick,
                        f, true, page.Filters[f], InkLayer.Page));
                if (page.Sorts.Count > 0)
                    widgets.Add(new InkWidget(
                        InkLayout.SortButton(listArea), InkAction.PageSortCycle,
                        page.ActiveSort, true, page.Sorts[page.ActiveSort], InkLayer.Page));
            }

            // 左列表：点击只做选中，动作统一放右栏详情。超出部分翻页。
            var visible = InkLayout.ListVisibleRows(listArea);
            var first = page.ListFirst;
            var shown = Math.Min(page.Rows.Count - first, visible);
            for (var i = 0; i < shown; i++)
                widgets.Add(new InkWidget(
                    InkLayout.ListRow(listArea, i), InkAction.PageSelect,
                    first + i, true, page.Rows[first + i].Name, InkLayer.Page));

            widgets.Add(new InkWidget(
                InkLayout.PagePager(listArea, -1), InkAction.PageScroll,
                0, first > 0, "上一页", InkLayer.Page));
            widgets.Add(new InkWidget(
                InkLayout.PagePager(listArea, 1), InkAction.PageScroll,
                1, first + visible < page.Rows.Count, "下一页", InkLayer.Page));

            // 右详情：动作按钮来自选中项。
            for (var j = 0; j < page.DetailActions.Count; j++)
                widgets.Add(new InkWidget(
                    InkLayout.DetailButton(InkLayout.FullDetailArea, j, page.DetailActions.Count),
                    InkAction.PageRow, j, page.DetailActions[j].Enabled,
                    page.DetailActions[j].Name, InkLayer.Page));
        }

        widgets.Add(new InkWidget(
            InkLayout.FullPageClose, InkAction.PageClose, 0, true, "关闭",
            InkLayer.Page));
    }

    /// <summary>
    /// 设施交互页的控件：每行右侧三个小按钮（放入 / 取出 / 过滤），外加右上角关闭。
    /// 与渲染器共用同一份行数据，画得出来就一定点得动。
    /// </summary>
    private static void BuildStoragePage(InkViewModel vm, List<InkStorageRow> rows, List<InkWidget> widgets)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = InkLayout.StorageRow(i);
            var entry = rows[i];
            // 背包里有才放得进去；设施里有才取得出来。
            widgets.Add(new InkWidget(InkLayout.StorageRowButton(row, 0), InkAction.StorageStore,
                i, entry.InBag > 0, "放入"));
            widgets.Add(new InkWidget(InkLayout.StorageRowButton(row, 1), InkAction.StorageTake,
                i, entry.InStorage > 0, "取出"));
            widgets.Add(new InkWidget(InkLayout.StorageRowButton(row, 2), InkAction.StorageFilterToggle,
                i, true, entry.Excluded ? "允许" : "禁止"));
        }
        widgets.Add(new InkWidget(InkLayout.FixtureClose, InkAction.StorageClose, 0, true, "关闭"));
    }

    /// <summary>开发模式：四面板容器不变，内容换成领地编辑。</summary>
    private static void BuildDevPage(InkDevModel? dev, InkUiState ui, List<InkWidget> widgets)
    {
        if (dev == null)
            return;

        // 左上面板：房间网格。X 热区在格内，必须先入表才能优先命中。
        for (var i = 0; i < dev.Rooms.Count; i++)
        {
            var cell = dev.Rooms[i];
            var cellRect = InkLayout.Cell(cell.X, cell.Y);
            if (cell.NonEmpty)
            {
                widgets.Add(new InkWidget(
                    InkLayout.DevRoomX(cellRect), InkAction.DevDemolishRoom,
                    i, cell.Removable, "拆除" + cell.Name, InkLayer.Page));
            }
            widgets.Add(new InkWidget(
                cellRect, InkAction.DevPickRoom, i, true, cell.Name, InkLayer.Page));
        }

        // 待放置房间：网格空格成为放置热区。
        if (dev.PlacingRoom >= 0)
        {
            var occupied = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var cell in dev.Rooms)
                occupied.Add((cell.X, cell.Y));
            for (var x = 0; x < InkLayout.GridCols; x++)
            {
                for (var y = 0; y < InkLayout.GridRows; y++)
                {
                    if (occupied.Contains((x, y)))
                        continue;
                    widgets.Add(new InkWidget(
                        InkLayout.Cell(x, y), InkAction.DevPlaceRoomAt,
                        InkLayout.CellIndex(x, y), true, "放置房间", InkLayer.Page));
                }
            }
        }

        // 右上面板：设施列表（点击行选中）+ 底部可建造设施目录（点击行即建造）。
        var facRows = Math.Min(dev.FacilityRows.Count, InkLayout.DevFacilityVisibleRows);
        for (var j = 0; j < facRows; j++)
        {
            widgets.Add(new InkWidget(
                InkLayout.DevFacilityRow(j), InkAction.DevPickFacility,
                j, true, dev.FacilityRows[j].Name, InkLayer.Page));
        }
        var facCatalog = Math.Min(dev.FacilityCatalog.Count,
            InkLayout.DevFacilityVisibleRows - facRows);
        for (var c = 0; c < facCatalog; c++)
        {
            widgets.Add(new InkWidget(
                InkLayout.DevFacilityRow(facRows + c), InkAction.DevBuildFacility,
                c, dev.FacilityCatalog[c].Enabled, dev.FacilityCatalog[c].Name, InkLayer.Page));
        }

        // 左下面板：房间行（点击选中）+ 底部可建造房间目录（点击行即建造）。
        var roomRows = Math.Min(dev.RoomRows.Count, InkLayout.DevRoomVisibleRows);
        for (var j = 0; j < roomRows; j++)
        {
            widgets.Add(new InkWidget(
                InkLayout.DevRoomRow(j), InkAction.DevPickRoom,
                j, true, dev.RoomRows[j].Name, InkLayer.Page));
        }
        var roomCatalog = Math.Min(dev.RoomCatalog.Count,
            InkLayout.DevRoomVisibleRows - roomRows);
        for (var c = 0; c < roomCatalog; c++)
        {
            widgets.Add(new InkWidget(
                InkLayout.DevRoomRow(roomRows + c), InkAction.DevBuildRoom,
                c, dev.RoomCatalog[c].Enabled, dev.RoomCatalog[c].Name, InkLayer.Page));
        }

        // 右下面板：详情动作按钮（拆除/放置）与退出开发。
        for (var a = 0; a < dev.DetailActions.Count; a++)
        {
            widgets.Add(new InkWidget(
                InkLayout.DetailButton(InkLayout.ActContent, a, dev.DetailActions.Count),
                InkAction.DevDetailAction, a, dev.DetailActions[a].Enabled,
                dev.DetailActions[a].Name, InkLayer.Page));
        }
        widgets.Add(new InkWidget(
            InkLayout.DevExitButton(InkLayout.ActContent), InkAction.DevExit,
            0, true, "退出开发", InkLayer.Page));
    }

    // ---------- 改名弹窗 ----------

    private static void BuildRename(InkUiState ui, List<InkWidget> widgets)
    {
        widgets.Add(new InkWidget(
            InkLayout.RenameButton(0), InkAction.RenameConfirm, 0, true, "确定",
            InkLayer.Modal));
        widgets.Add(new InkWidget(
            InkLayout.RenameButton(1), InkAction.RenameCancel, 0, true, "取消",
            InkLayer.Modal));
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

    private static void BuildMap(InkViewModel vm, List<InkWidget> widgets)
    {
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
                    // 世界层：只有落在兴趣点上的格子可点（点击进入该地点）。
                    var name = room.Name;
                    enabled = vm.Hub.State.World.Pois.Exists(
                        p => p.NameZh == name || p.NameEn == name);
                }
                else
                {
                    // 未开拓的房间只有在买得起时才可点，避免“点了没反应”。
                    enabled = room.Open
                        ? vm.IsPlayerRoom(room.Id) || vm.IsNeighbor(room)
                        : vm.CanDevelop(room);
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
            rows.Add(new FixtureRow(fixture, used, cap));
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
        var pageCount = Math.Max(1, (total + InkLayout.CardsPerPage - 1) / InkLayout.CardsPerPage);
        return new PageInfo(Math.Clamp(requested, 0, pageCount - 1), pageCount);
    }

    private static IReadOnlyList<CardView> BuildCardViews(InkViewModel vm, int page)
    {
        var all = vm.CardsHere();
        var selected = vm.Selected();
        var views = new List<CardView>(InkLayout.CardsPerPage);

        var start = page * InkLayout.CardsPerPage;
        for (var i = start; i < all.Count && views.Count < InkLayout.CardsPerPage; i++)
        {
            var isSelected = selected.HasValue && selected.Value.Id == all[i].Id;
            views.Add(new CardView(all[i], isSelected));
        }
        return views;
    }

    private readonly record struct CardBuild(
        IReadOnlyList<CardView> Views, int Page, int PageCount);

    private static CardBuild BuildCards(InkViewModel vm, int cardPage, List<InkWidget> widgets)
    {
        var info = PageOf(vm.CardsHere().Count, cardPage);
        var views = BuildCardViews(vm, info.Page);

        for (var i = 0; i < views.Count; i++)
        {
            widgets.Add(new InkWidget(
                InkLayout.Card(i), InkAction.Card,
                views[i].Card.Id, true, views[i].Card.Name));
        }

        // 只有确实还有下一页时才给翻页三角，避免“点了没反应”。
        if (info.PageCount > 1)
        {
            widgets.Add(new InkWidget(
                InkLayout.PageNext, InkAction.PageNext, 0, true, "下一页"));
        }

        return new CardBuild(views, info.Page, info.PageCount);
    }

    private static void BuildPageEntries(InkViewModel vm, List<InkWidget> widgets)
    {
        var entries = Entries(vm.ChatPartner() != null);
        for (var i = 0; i < entries.Length; i++)
        {
            widgets.Add(new InkWidget(
                InkLayout.PageEntry(i, entries.Length), InkAction.PageEntry,
                i, true, InkPageModel.Info(entries[i]).Label));
        }
    }

    /// <summary>左下角入口：管理四页；存在对话对象时追加上角色状态页。</summary>
    public static InkPage[] Entries(bool withCharacter) => withCharacter
        ? new[] { InkPage.Stock, InkPage.Trade, InkPage.Craft, InkPage.Develop, InkPage.Status }
        : new[] { InkPage.Stock, InkPage.Trade, InkPage.Craft, InkPage.Develop };

    // ---------- 交流 / 行动 ----------

    /// <summary>
    /// 右下角三态，互斥：
    /// 选中角色时是“交流”；坐在设施上时是这件设施的日常行动；
    /// 都没占时是房间级行动（观察 + 导航）。
    /// 未显示的那几组不进元素表，因此不可能被点到。
    /// </summary>
    private static void BuildActions(InkViewModel vm, List<InkWidget> widgets)
    {
        if (vm.ShowSocial)
        {
            var present = vm.SelectedIsPresent();
            var social = InkViewModel.SocialActions;
            for (var i = 0; i < social.Length; i++)
            {
                widgets.Add(new InkWidget(
                    InkLayout.SocialButton(i, social.Length), InkAction.Social,
                    i, present, social[i].Label));
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
                widgets.Add(new InkWidget(
                    InkLayout.PlaceButton(i, fixture.Count), InkAction.FixtureAction,
                    i, true, label));
            }
            return;
        }

        var places = InkViewModel.PlaceActions;
        for (var i = 0; i < places.Length; i++)
        {
            widgets.Add(new InkWidget(
                InkLayout.PlaceButton(i, places.Length), InkAction.Place,
                i, places[i].Enabled, places[i].Label));
        }
    }
}
