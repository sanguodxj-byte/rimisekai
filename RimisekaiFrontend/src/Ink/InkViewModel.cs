using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Session;

namespace Rimisekai.Ink;

/// <summary>
/// 界面数据源：把 Core 的 HubSession 摊平成界面好读的形状。
/// 只读，不含任何逻辑；所有状态变更仍然走 HubSession。
/// </summary>
public sealed class InkViewModel
{
    private readonly HubSession _hub;

    /// <summary>内容包里显式配了立绘的角色，按名字索引。内容包可以不传。</summary>
    private readonly Dictionary<string, string> _portraits;

    public HubSession Hub => _hub;

    /// <summary>战斗形态：非空时据点切入战斗版式（地图与日志退出，下三面板下沉）。</summary>
    public BattleSession? Combat { get; set; }

    /// <summary>战斗形态淡入进度 0..1，驱动面板下沉与边框消融。</summary>
    public float CombatT { get; set; }

    public InkViewModel(HubSession hub, ContentPack? pack = null)
    {
        _hub = hub;
        _portraits = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pack != null)
        {
            foreach (var entry in pack.Seed.Characters)
            {
                if (entry.Portrait.Length > 0)
                    _portraits[entry.Name] = entry.Portrait;
            }
        }
    }

    public string PlaceTitle => $"「{Hub.MapTitle()}」";

    /// <summary>地区名称：世界地图下的地区、地城、POI、任务地点与领地同级。</summary>
    public string MapTitle() => Hub.MapTitle();

    /// <summary>当前房间名，不带任何子一级地区概念。</summary>
    public string PlaceName() => Hub.PlaceName();

    /// <summary>领地是否已可改名（仅限领地层级且等级达标）。</summary>
    public bool CanRenameTerritory => Hub.Layer == Rimisekai.Hub.MapLayer.Territory && Hub.State.Territory.CanName;

    /// <summary>领地当前名（玩家改过的）。空表示尚未取名。</summary>
    public string TerritoryName => Hub.State.Territory.Name;

    /// <summary>按名字找角色。遮盖层只知道说话人名，需要据此取资料。</summary>
    public CharacterState? FindByName(string name)
    {
        if (name.Length == 0)
            return null;
        foreach (var character in Hub.State.Roster.Members.Concat(Hub.State.Roster.Visitors))
        {
            if (character.Name == name)
                return character;
        }
        return null;
    }

    /// <summary>按 Id 找角色。关系表存的是 Id，界面要显示名字。</summary>
    public CharacterState? FindById(int id) => Hub.State.Roster.Person(id);

    /// <summary>角色的显示名；名册里没有（已被移除）时退回 Id，避免显示空白。</summary>
    public string NameOf(int id) => FindById(id)?.Name ?? $"#{id}";

    /// <summary>
    /// 角色立绘资源路径。内容包里显式配了就用它；
    /// 没配则按约定取 res://content/portraits/&lt;名字&gt;.png。
    /// </summary>
    public string PortraitPath(string name)
    {
        if (name.Length == 0)
            return "";
        if (_portraits.TryGetValue(name, out var configured))
            return configured;
        return $"res://content/portraits/{name}.png";
    }

    /// <summary>
    /// 当前对话对象：优先取遮盖层的说话人，其次取场景演出的说话人，
    /// 都没有再取角色栏选中的角色，最后退回名册里第一名同伴
    /// （据点里的工作安排入口没有对话对象，落在这条上）。
    /// 状态/技能/日程三页都针对它。
    /// </summary>
    public CharacterState? ChatPartner()
    {
        var overlay = Hub.Overlay;
        if (overlay != null)
        {
            var speaker = FindByName(overlay.Speaker);
            if (speaker != null)
                return speaker;
        }
        if (Hub.ScenePlaying && Hub.SceneLines.Count > 0)
        {
            var actor = FindByName(Hub.SceneLines[^1].Speaker);
            if (actor != null)
                return actor;
        }
        var selected = Hub.State.Roster.Person(Hub.SelectedCharacterId);
        if (selected != null)
            return selected;
        foreach (var character in Hub.State.Roster.Members)
        {
            if (!character.IsMaster)
                return character;
        }
        return null;
    }

    /// <summary>
    /// 顶栏右侧的一个状态项：标签（暗）与数值（亮）分开画，项与项之间位置固定。
    /// 文案一律不用冒号（AGENTS.md 铁律）。
    /// </summary>
    public readonly record struct InkHeaderItem(string Label, string Value);

    /// <summary>
    /// 顶栏右侧的状态项。主人 2026-10-01 定：**只删「季节」二字，保留它的值**
    /// （天气/时刻同样只留数值、不带标签）。顺序即绘制顺序。
    /// </summary>
    public IReadOnlyList<InkHeaderItem> HeaderItems()
    {
        var h = Hub.Header();
        return new[]
        {
            // 季节：删「季节」二字，保留「春季/夏季/…」这个值。
            new InkHeaderItem("", HubSession.SeasonName(h.Season)),
            // 天气：同样只留数值。
            new InkHeaderItem("", HubSession.WeatherName(h.Weather)),
            // 时刻不用冒号，写成「0时00分」。
            new InkHeaderItem("", $"{h.Hour}时{h.Minute:00}分"),
            // 金钱单位 G，空格千分位分隔（如 10 000G）。
            new InkHeaderItem("", InkText.Money(h.Money)),
        };
    }

    /// <summary>近期日志（最旧在前），每条一句「a，b」。</summary>
    public IReadOnlyList<string> LogLines()
    {
        var list = new List<string>();
        foreach (var line in Hub.History)
            list.Add(line.Text);
        return list;
    }

    /// <summary>当前所在地的设施，含容量与占用。</summary>
    public IReadOnlyList<FixtureView> Fixtures() => Hub.Here();

    public IReadOnlyList<CharacterCard> Cards() => Hub.Present();

    /// <summary>左下角头像：只有同房的其他角色，主角不在内。</summary>
    public IReadOnlyList<CharacterCard> CardsHere() => Hub.CardsHere();

    /// <summary>左下角色栏：主角常显，其余只列与主角同房的角色。</summary>
    public IReadOnlyList<CharacterCard> Avatars()
    {
        var list = new List<CharacterCard>();
        foreach (var card in Hub.Present())
        {
            if (card.IsPlayer || card.RoomId == Hub.PlayerRoomId)
                list.Add(card);
        }
        return list;
    }

    /// <summary>当前所在地的标准名，形如“中央·庭院”。POI 头与日志都用它。</summary>
    public string HereName() => Hub.PlaceName();

    /// <summary>
    /// 按格子坐标取当前区域的房间；没有则为 null。
    /// 只在当前区域的房间集合里找，避免其它区域的同坐标房间被误取。
    /// </summary>
    public Room? RoomAt(int x, int y)
    {
        foreach (var room in Rooms())
        {
            if (room.X == x && room.Y == y)
                return room;
        }
        return null;
    }

    public IReadOnlyList<Room> Rooms() => Hub.Map();

    /// <summary>主角所在格：横版世界层是队伍脚下那间视口房，其余图层是主角所在的房间。</summary>
    public bool IsPlayerRoom(int roomId) =>
        Hub.Layer == MapLayer.World ? Hub.WorldPartyViewRoomId == roomId : Hub.PlayerRoomId == roomId;

    public bool IsOpen(Room room) => room.Open;

    public bool IsNeighbor(Room room)
    {
        var here = Hub.State.Territory.Rooms.Find(r => r.Id == Hub.PlayerRoomId);
        return here != null && here.Links.Contains(room.Id);
    }

    /// <summary>“此处”条目右侧的槽位：返回 (已占用, 容量)。</summary>
    public (int Used, int Capacity) Seats(FixtureView fixture) =>
        (fixture.Occupants, fixture.Occupants + fixture.SeatsLeft);

    public IReadOnlyList<CharacterCard> WorkersAtFixture(int fixtureId) => Hub.WorkersAtFixture(fixtureId);

    public CharacterCard? Selected()
    {
        foreach (var card in Hub.Present())
        {
            if (card.Id == Hub.SelectedCharacterId)
                return card;
        }
        return null;
    }

    public bool SelectedIsPresent()
    {
        var card = Selected();
        return card.HasValue && card.Value.RoomId == Hub.PlayerRoomId;
    }

    /// <summary>
    /// 右下角显示“交流”还是“行动”：选中角色时为交流，未选中（或取消选中）为行动。
    /// 两者互斥，同一时刻只显示一组。
    /// </summary>
    public bool ShowSocial => Hub.HasSelection;

    public string SocialTitle()
    {
        var card = Selected();
        if (card == null)
            return "交流";
        if (card.Value.IsPlayer)
            return "交流 · 你";
        // 选中即同房：对方走开时 Core 会取消选中，所以这里不会出现“不在场”。
        return $"交流 · {card.Value.Name}";
    }

    /// <summary>
    /// 房间级行动：站在屋里没坐上设施时能做的，只有“观察”。
    /// 日常行动一律要坐到对应设施上（见 <see cref="FixtureActions"/>）。
    /// </summary>
    public static readonly (PlaceAction Action, string Label, bool Enabled)[] PlaceActions =
    {
        (PlaceAction.Observe, "观察四周", true),
    };

    /// <summary>
    /// 玩家当前所坐设施支持的行动。没坐设施时为空表。
    /// 直接取 Core 的判定，界面画出来的按钮与点得动的判定同源，
    /// 不会出现“画得出来却点不动”。
    /// </summary>
    public IReadOnlyList<ActionKind> FixtureActions() => Hub.ActionsAtCurrentFixture();

    /// <summary>玩家当前所坐设施的名字；没坐则空串。行动面板的标题用它。</summary>
    public string CurrentFixtureName()
    {
        foreach (var fixture in Hub.Here())
        {
            if (fixture.PlayerHere)
                return fixture.Name;
        }
        return "";
    }

    /// <summary>设施交互页是否开着（玩家点了“打开货架”这类行动）。</summary>
    public bool StorageOpen => Hub.OpenStorageFacility != null;

    /// <summary>当前打开操作的设施名；没开则空串。</summary>
    public string StorageName() => Hub.OpenStorageFacility?.Name ?? "";

    /// <summary>存储页的一览（设施内 + 背包里的物品）。</summary>
    public IReadOnlyList<StorageRow> StorageRows() => Hub.StorageRows();

    /// <summary>存储配置页的品类行（按大类一键收放）。</summary>
    public IReadOnlyList<StorageCategoryRow> StorageCategoryRows() => Hub.StorageCategoryRows();

    /// <summary>当前设施的容量说明，如“12 / 20”或“12 / 不限”。</summary>
    public string StorageCapacityText()
    {
        var facility = Hub.OpenStorageFacility;
        if (facility == null)
            return "";
        return facility.StorageCapacity <= 0
            ? $"{facility.StoredCount()} / 不限"
            : $"{facility.StoredCount()} / {facility.StorageCapacity}";
    }

    /// <summary>该物品是否被当前设施的过滤器允许（含品类命中）。</summary>
    public bool StorageAccepts(string itemId)
    {
        var facility = Hub.OpenStorageFacility;
        return facility != null && Hub.State.Territory.Allows(facility, itemId);
    }

    /// <summary>该物品的品类名（用于在存储行上标注归属）。没有定义则空串。</summary>
    public string ItemCategoryLabel(string itemId)
    {
        var info = Rimisekai.Defs.Items.Info(Hub.State.Territory, itemId);
        if (info == null || string.IsNullOrEmpty(info.Value.Category))
            return "";
        var cat = Rimisekai.Defs.DefDatabase<Rimisekai.Defs.ThingCategoryDef>.Get(info.Value.Category);
        return cat?.Label ?? "";
    }

    public static readonly (SocialAction Action, string Label)[] SocialActions =
    {
        (SocialAction.Talk, "交谈"),
        (SocialAction.Observe, "观察"),
        (SocialAction.Gift, "赠物"),
        (SocialAction.Touch, "接触"),
        (SocialAction.PatHead, "摸头"),
        (SocialAction.BodyContact, "身体接触"),
        (SocialAction.Hug, "拥抱"),
        (SocialAction.Kiss, "亲吻"),
        (SocialAction.Invite, "邀请"),
    };

    /// <summary>“邀请／分开”在 SocialActions 主表里的下标。</summary>
    public const int InviteActionIndex = 8;

    /// <summary>选中角色是否正跟着你。</summary>
    public bool SelectedFollowing => Hub.IsFollowing(Hub.SelectedCharacterId);

    /// <summary>交谈子项里“邀请”位：跟随中显示为“分开”，否则是“邀请”。</summary>
    public (SocialAction Action, string Label) InviteEntry() =>
        SelectedFollowing ? (SocialAction.Part, "分开") : (SocialAction.Invite, "邀请");

    /// <summary>
    /// 交流面板的嵌套结构：左列四个起始钮；带子表的类别点开后在右侧列子项。
    /// 子项存 SocialActions 主表下标与自己的显示名（交谈的子项叫“聊天”），
    /// 分发复用既有通路；null 子表表示点了直接执行（观察执行 Observe，离开取消选中）。
    /// </summary>
    public static readonly (string Label, (int Index, string Label)[]? Children)[] SocialCategories =
    {
        ("交谈", new[] { (0, "聊天"), (8, "邀请"), (2, "赠礼") }),
        ("接触", new[] { (4, "摸头"), (5, "身体接触"), (6, "拥抱"), (7, "亲吻") }),
        ("观察", null),
        ("离开", null),
    };

    /// <summary>中下操作面板的页面入口：库存、交易、任务（原制作位置）、开发。</summary>
    public static readonly InkPage[] PageEntries =
    {
        InkPage.Stock,
        InkPage.Trade,
        InkPage.Quest,
        InkPage.Develop,
    };
}
