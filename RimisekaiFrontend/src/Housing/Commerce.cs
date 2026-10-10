using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;

namespace Rimisekai.Housing;

/// <summary>访客这一趟走到哪一步：进门往店里走 → 在店里等人招呼、买货 → 往回走出领地。</summary>
public enum VisitPhase
{
    Arriving,
    Shopping,
    Leaving,
}

/// <summary>一位正在领地里的访客。人本身在 <see cref="Roster.Visitors"/>，这里只记这一趟的行程。</summary>
public sealed class Visit
{
    public int CharacterId { get; init; }
    public int RoomId { get; set; }

    /// <summary>奔着哪间店来。</summary>
    public int ShopRoomId { get; init; }

    /// <summary>从哪间房进的领地（走的时候也从这里出去）。</summary>
    public int EntryRoomId { get; init; }

    public VisitPhase Phase { get; set; }

    /// <summary>还要走的房间序列（不含脚下这间）。</summary>
    public List<int> Path { get; } = new();

    /// <summary>在店里等了多少分钟还没人招呼。</summary>
    public int Waited { get; set; }
}

/// <summary>店里成交（或没成）的一笔：谁卖给谁、卖了什么、收了多少钱、店升没升级。Count 为 0 = 客人没看上。</summary>
public readonly record struct ShopSale(
    int ShopRoomId, int SellerId, int VisitorId, string ItemId, int Count, int Income, bool LevelUp);

/// <summary>访客一步推进的结果，会话据此记账、写日志、更新在场。</summary>
public sealed class VisitReport
{
    public List<ShopSale> Sales { get; } = new();
    public List<Visit> Arrived { get; } = new();

    /// <summary>这一步走出领地的访客。</summary>
    public List<int> Left { get; } = new();

    /// <summary>在店里没等到人招呼（或店里没货）就走了的访客。</summary>
    public List<int> Unserved { get; } = new();
}

/// <summary>
/// 领地里的生意：营业性房间（<see cref="Territory.CommercialTag"/>）开门就有访客上门。
/// - 只有领地里至少有一间营业性房间才来人；每个整点（白天 <see cref="OpenHour"/>–<see cref="CloseHour"/>）
///   每间店按自己等级的几率（房间表 visitorChance）引来一位访客。
/// - 访客从最外沿的露天房间进门（<see cref="EntryFor"/>），沿门走进店里；门锁一样拦他们。
/// - 店里有人当班（在店里的交易设施上干「交易」——住户排班或主人自己站柜台）就招呼他：
///   从店里仓储设施（摊位本身、箱子等）的存货里随手挑一样、买走若干件，成交与件数随当班者的魅力与社交本领上浮。
/// - 买完（或等了 <see cref="WaitMinutes"/> 分钟没人招呼、店里没货）立刻往回走，到进门那间就离开领地。
/// - 访客不是住户：不睡、不吃、不排班；来过的人记在访客名册里，下次可能还是他来。
/// </summary>
public static class Commerce
{
    public const int OpenHour = 8;
    public const int CloseHour = 20;

    /// <summary>进了店没人招呼，等这么久就走。</summary>
    public const int WaitMinutes = 60;

    /// <summary>认识的访客里挑一位回头客的几率（百分比）；没有回头客可挑就来新面孔。</summary>
    public const int RevisitPercent = 50;

    /// <summary>访客名册记满这么多人后只来回头客。</summary>
    public const int KnownVisitorCap = 12;

    /// <summary>当班者本领为 100% 时客人买下的几率（百分比），封顶 <see cref="MaxSuccessPercent"/>。</summary>
    public const int BaseSuccessPercent = 60;
    public const int MaxSuccessPercent = 95;

    /// <summary>本领为 100% 时一单最多买几件（1 至此数随机），随本领按比例放大。</summary>
    public const int BaseBatch = 3;

    /// <summary>当班者本领的上限（百分比）。</summary>
    public const int MaxSellerPercent = 200;

    /// <summary>
    /// 当班者的本领（百分比）：100 + 2 ×（魅力 + 社交等级），封顶 <see cref="MaxSellerPercent"/>。
    /// 成交几率、一单件数、成交价都按它放大。
    /// </summary>
    public static int SellerPercent(CharacterState seller) =>
        Math.Min(MaxSellerPercent, 100 + 2 * (seller[CoreStat.Charm] + seller.Life(LifeSkill.Social)));

    /// <summary>成交几率（百分比）。</summary>
    public static int SuccessPercent(int sellerPercent) =>
        Math.Min(MaxSuccessPercent, BaseSuccessPercent * sellerPercent / 100);

    /// <summary>这件货在店里卖一件的价：物品的基准价值 × 当班者本领。卖不出价的（没有身价）返回 0。</summary>
    public static int UnitPrice(Territory territory, string itemId, int sellerPercent)
    {
        var value = Defs.Items.Get(itemId)?.MarketValue ?? territory.Listing(itemId)?.SellPrice ?? 0;
        return value <= 0 ? 0 : Math.Max(1, value * sellerPercent / 100);
    }

    /// <summary>领地里有没有开着的营业性房间。没有就不来人。</summary>
    public static bool HasShop(Territory territory) =>
        territory.Rooms.Exists(r => IsOwnShop(r));

    private static bool IsOwnShop(Room r) =>
        r.Commercial && r.Open && r.X >= 0 && r.RegionId < Territory.MaxTerritoryRegions;

    /// <summary>店里的存货：店里每件能存东西的设施里、有身价的那些（摊位本身、箱子……）。</summary>
    public static List<(Facility Holder, string ItemId, int Count)> StockOf(Territory territory, Room shop) =>
        territory.Facilities
            .Where(f => f.Built && f.RoomId == shop.Id && f.CanStore)
            .SelectMany(f => f.Contents.Items.Where(kv => kv.Value > 0 && UnitPrice(territory, kv.Key, 100) > 0)
                .Select(kv => (f, kv.Key, kv.Value)))
            .OrderBy(s => s.Item1.Id).ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>房间在整片领地（3×3 区拼图）里的格坐标。</summary>
    private static (int X, int Y) Global(Room r)
    {
        var (rx, ry) = Territory.RegionCellOf(r.RegionId);
        return (rx * Territory.RegionSize + r.X, ry * Territory.RegionSize + r.Y);
    }

    /// <summary>
    /// 访客从哪进门：领地里最外沿的露天房间——离整片领地正中最远（切比雪夫距离），
    /// 还得从那里走得到店里（门锁按这位访客算）。同样远的取到店路最短的，再按房号。没有返回 null。
    /// </summary>
    public static Room? EntryFor(Territory territory, Room shop, CharacterState who)
    {
        var center = Territory.RegionCellOf(Territory.CenterRegion).X * Territory.RegionSize + Territory.RegionSize / 2;
        Room? best = null;
        var bestDist = -1;
        var bestRoute = int.MaxValue;
        foreach (var r in territory.Rooms)
        {
            if (!r.Open || r.X < 0 || r.RegionId >= Territory.MaxTerritoryRegions || r.HasTag(Territory.IndoorTag)
                || territory.BarsEntry(r, who))
                continue;
            var route = r.Id == shop.Id ? 0 : territory.Route(r.Id, shop.Id, barred: x => territory.BarsEntry(x, who)).Count;
            if (r.Id != shop.Id && route == 0)
                continue;
            var (gx, gy) = Global(r);
            var dist = Math.Max(Math.Abs(gx - center), Math.Abs(gy - center));
            if (dist > bestDist || (dist == bestDist && (route < bestRoute || (route == bestRoute && r.Id < best!.Id))))
            {
                best = r;
                bestDist = dist;
                bestRoute = route;
            }
        }
        return best;
    }

    /// <summary>
    /// 推进一格时间（<see cref="TerritoryClock.StepMinutes"/> 分钟）：在场访客走一步 / 买货 / 离开，
    /// 到整点按各店等级掷骰引人。<paramref name="masterId"/> 与 <paramref name="playerRoomId"/>、
    /// <paramref name="playerFixtureId"/> 用来判断主人是不是正站在店里的柜台后。
    /// </summary>
    public static VisitReport Step(Territory territory, Roster roster, IReadOnlyList<Worker> workers,
        int nowTotal, int masterId, int playerRoomId, int playerFixtureId, Random rng)
    {
        var report = new VisitReport();
        foreach (var visit in territory.Visits.ToList())
            Advance(territory, roster, workers, visit, masterId, playerRoomId, playerFixtureId, rng, report);
        var hour = nowTotal / 60 % 24;
        if (nowTotal % 60 == 0 && hour >= OpenHour && hour < CloseHour)
        {
            foreach (var shop in territory.Rooms.Where(IsOwnShop).ToList())
            {
                if (rng.Next(100) < shop.VisitorPercent && Spawn(territory, roster, shop, rng) is { } visit)
                    report.Arrived.Add(visit);
            }
        }
        return report;
    }

    /// <summary>引来一位访客：回头客或新面孔（从身份池现掷），落在进门那间房。进不来（没有能走到店的露天入口）就不来。</summary>
    public static Visit? Spawn(Territory territory, Roster roster, Room shop, Random rng)
    {
        var away = roster.Visitors.Where(v => !territory.Visits.Exists(x => x.CharacterId == v.Id)).ToList();
        CharacterState who;
        var fresh = false;
        if (away.Count > 0 && (roster.Visitors.Count >= KnownVisitorCap || rng.Next(100) < RevisitPercent))
            who = away[rng.Next(away.Count)];
        else if (roster.Visitors.Count < KnownVisitorCap)
        {
            who = new CharacterGenerator(rng).Roll(roster, roster.Visitors.Select(v => v.Name)).State;
            fresh = true;
        }
        else
            return null;
        var entry = EntryFor(territory, shop, who);
        if (entry == null)
            return null;
        if (fresh)
            roster.AttachVisitor(who);
        var visit = new Visit
        {
            CharacterId = who.Id, RoomId = entry.Id, ShopRoomId = shop.Id, EntryRoomId = entry.Id,
            Phase = VisitPhase.Arriving,
        };
        if (entry.Id != shop.Id)
            visit.Path.AddRange(territory.Route(entry.Id, shop.Id, barred: r => territory.BarsEntry(r, who)));
        territory.Visits.Add(visit);
        return visit;
    }

    private static void Advance(Territory territory, Roster roster, IReadOnlyList<Worker> workers, Visit visit,
        int masterId, int playerRoomId, int playerFixtureId, Random rng, VisitReport report)
    {
        var who = roster.Visitor(visit.CharacterId);
        if (who == null)
        {
            // 人已经不是访客了（入伙成了住户）：这一趟就此作罢。
            territory.Visits.Remove(visit);
            return;
        }
        if (visit.Path.Count > 0)
        {
            var next = territory.Room(visit.Path[0]);
            if (next != null && next.Open && !territory.BarsEntry(next, who))
            {
                visit.RoomId = next.Id;
                visit.Path.RemoveAt(0);
            }
            else
            {
                // 门锁上了：换条路；哪条都走不通，这一趟就算了，人直接离开。
                var goal = visit.Phase == VisitPhase.Leaving ? visit.EntryRoomId : visit.ShopRoomId;
                var route = territory.Route(visit.RoomId, goal, barred: r => territory.BarsEntry(r, who));
                visit.Path.Clear();
                if (route.Count == 0)
                {
                    territory.Visits.Remove(visit);
                    report.Left.Add(visit.CharacterId);
                    return;
                }
                visit.Path.AddRange(route);
            }
            return;
        }
        switch (visit.Phase)
        {
            case VisitPhase.Arriving:
                visit.Phase = VisitPhase.Shopping;
                visit.Waited = 0;
                return;
            case VisitPhase.Shopping:
            {
                var shop = territory.Room(visit.ShopRoomId);
                var seller = shop == null ? null : SellerIn(territory, roster, workers, shop, masterId, playerRoomId, playerFixtureId);
                if (seller != null && shop != null)
                {
                    var sale = Sell(territory, shop, seller, who.Id, rng);
                    if (sale == null)
                        report.Unserved.Add(who.Id);
                    else
                        report.Sales.Add(sale.Value);
                    HeadOut(territory, visit, who);
                    return;
                }
                visit.Waited += TerritoryClock.StepMinutes;
                if (visit.Waited >= WaitMinutes)
                {
                    report.Unserved.Add(who.Id);
                    HeadOut(territory, visit, who);
                }
                return;
            }
            default:
                territory.Visits.Remove(visit);
                report.Left.Add(visit.CharacterId);
                return;
        }
    }

    private static void HeadOut(Territory territory, Visit visit, CharacterState who)
    {
        visit.Phase = VisitPhase.Leaving;
        visit.Path.Clear();
        if (visit.RoomId != visit.EntryRoomId)
            visit.Path.AddRange(territory.Route(visit.RoomId, visit.EntryRoomId, barred: r => territory.BarsEntry(r, who)));
    }

    /// <summary>
    /// 店里此刻谁在当班：在店里的交易设施上干着「交易」的住户（排班来的，主人排班也算），
    /// 或正站在店里交易设施上的主人。没有返回 null。
    /// </summary>
    public static CharacterState? SellerIn(Territory territory, Roster roster, IReadOnlyList<Worker> workers, Room shop,
        int masterId, int playerRoomId, int playerFixtureId)
    {
        foreach (var w in workers)
        {
            if (w.Phase == WorkPhase.Working && w.Task == ActionKind.Trade && w.RoomId == shop.Id && w.Path.Count == 0
                && territory.Facilities.Exists(f => f.Id == w.FacilityId && f.RoomId == shop.Id && f.Supports(ActionKind.Trade))
                && roster.Find(w.CharacterId) is { } staff)
                return staff;
        }
        if (playerRoomId == shop.Id && playerFixtureId >= 0
            && territory.Facilities.Exists(f => f.Id == playerFixtureId && f.Built && f.RoomId == shop.Id && f.Supports(ActionKind.Trade)))
            return roster.Find(masterId);
        return null;
    }

    /// <summary>
    /// 当班者招呼一位客人：按成交几率掷骰；成了就从店里存货里随手挑一样，买 1 至（<see cref="BaseBatch"/> × 本领）件
    /// （不超过存货），每件按 <see cref="UnitPrice"/> 收钱。卖出的件数记进店的累计，越过门槛就升级。店里没货返回 null。
    /// </summary>
    public static ShopSale? Sell(Territory territory, Room shop, CharacterState seller, int visitorId, Random rng)
    {
        var stock = StockOf(territory, shop);
        if (stock.Count == 0)
            return null;
        var percent = SellerPercent(seller);
        seller.GainLifeExp(LifeSkill.Social, Territory.CraftExp);
        if (rng.Next(100) >= SuccessPercent(percent))
            return new ShopSale(shop.Id, seller.Id, visitorId, "", 0, 0, false);
        var (holder, itemId, have) = stock[rng.Next(stock.Count)];
        var count = Math.Min(have, 1 + rng.Next(Math.Max(1, BaseBatch * percent / 100)));
        holder.Contents.Add(itemId, -count);
        var level = shop.ShopLevel;
        var income = count * UnitPrice(territory, itemId, percent);
        shop.Sales += count;
        shop.Revenue += income;
        return new ShopSale(shop.Id, seller.Id, visitorId, itemId, count, income, shop.ShopLevel > level);
    }
}
