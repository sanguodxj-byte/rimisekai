using System;
using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;

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

    /// <summary>左上角标题：领地内写“领地”，世界写“世界”，任务/POI 写地点名。</summary>
    public string MapTitle() => Hub.MapTitle();

    /// <summary>当前位置标准名，形如“中央·庭院”。</summary>
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
        foreach (var character in Hub.State.Roster.Members)
        {
            if (character.Name == name)
                return character;
        }
        return null;
    }

    /// <summary>按 Id 找角色。关系表存的是 Id，界面要显示名字。</summary>
    public CharacterState? FindById(int id) => Hub.State.Roster.Find(id);

    /// <summary>
    /// 工作页矩阵的列：参与排班的角色（不含主角），顺序即名册顺序。
    /// 构建器与点击处理共用它，保证列下标两边一致。
    /// </summary>
    public IReadOnlyList<InkWorkColumn> WorkColumns()
    {
        var list = new List<InkWorkColumn>();
        foreach (var character in Hub.State.Roster.Members)
        {
            if (character.IsMaster)
                continue;
            list.Add(new InkWorkColumn(character.Id, character.Name));
        }
        return list;
    }

    /// <summary>角色的显示名；名册里没有（已被移除）时退回 Id，避免显示空白。</summary>
    public string NameOf(int id) => FindById(id)?.Name ?? $"#{id}";

    /// <summary>
    /// 是否显示工作入口。只有一个可派角色时不显示——矩阵只有一列，
    /// 派活等同于"让这人干活"，没有优先级可言。
    /// </summary>
    public bool HasWorkPage => WorkColumns().Count >= 2;

    /// <summary>
    /// 角色立绘资源路径。内容包里显式配了就用它；
    /// 没配则按约定取 res://content/portraits/&lt;名字&gt;.png。
    /// 两条都指不到文件时由渲染器退回线稿人形。
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
    /// 当前对话对象：优先取遮盖层的说话人，其次取角色栏选中的角色。
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
        return Hub.State.Roster.Find(Hub.SelectedCharacterId);
    }

    public string HeaderRight()
    {
        var h = Hub.Header();
        return $"季节：{SeasonName(h.Season)}　天气：{WeatherName(h.Weather)}　" +
               $"时刻 {h.Hour}:{h.Minute:00}　金钱 ${h.Money:N0}";
    }

    public IReadOnlyList<string> LogLines()
    {
        var list = new List<string>();
        foreach (var line in Hub.Log)
            list.Add(line.Text);
        return list;
    }

    /// <summary>当前所在地的设施，含容量与占用。</summary>
    public IReadOnlyList<FixtureView> Fixtures() => Hub.Here();

    public IReadOnlyList<CharacterCard> Cards() => Hub.Party();

    /// <summary>左下角头像：只有同房的其他角色，主角不在内。</summary>
    public IReadOnlyList<CharacterCard> CardsHere() => Hub.CardsHere();

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

    public bool IsPlayerRoom(int roomId) => Hub.PlayerRoomId == roomId;

    public bool IsOpen(Room room) => room.Open;

    public bool CanDevelop(Room room) => Hub.State.Territory.CanOpen(room, Hub.State.Money);

    public bool IsNeighbor(Room room)
    {
        var here = Hub.State.Territory.Rooms.Find(r => r.Id == Hub.PlayerRoomId);
        return here != null && here.Links.Contains(room.Id);
    }

    /// <summary>“此处”条目右侧的槽位：返回 (已占用, 容量)。</summary>
    public (int Used, int Capacity) Seats(FixtureView fixture) =>
        (fixture.Occupants, fixture.Occupants + fixture.SeatsLeft);

    public CharacterCard? Selected()
    {
        foreach (var card in Hub.Party())
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

    /// <summary>该物品是否被当前设施的过滤器允许。</summary>
    public bool StorageAccepts(string itemId)
    {
        var facility = Hub.OpenStorageFacility;
        if (facility == null)
            return false;
        return facility.StorageFilter.Count == 0 || facility.StorageFilter.Contains(itemId);
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

    /// <summary>左下角的页面入口。顺序即按钮顺序。</summary>
    public static readonly InkPage[] PageEntries =
    {
        InkPage.Stock,
        InkPage.Trade,
        InkPage.Craft,
        InkPage.Develop,
    };

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        Season.Winter => "冬",
        _ => "-",
    };

    private static string WeatherName(Weather weather) => weather switch
    {
        Weather.Clear => "晴",
        Weather.Cloud => "阴",
        Weather.Rain => "雨",
        Weather.Snow => "雪",
        _ => "-",
    };
}
