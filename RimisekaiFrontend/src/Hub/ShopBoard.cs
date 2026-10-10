using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

/// <summary>领地里自己开的店：访客进出、店里成交入账、邀请访客入伙。规则见 <see cref="Commerce"/>。</summary>
public sealed partial class HubSession
{
    /// <summary>这一次推进时间里走出领地的访客（推进完统一从在场表里摘掉）。</summary>
    private readonly HashSet<int> _visitorsGone = new();

    /// <summary>推进一格时间里的访客与生意：成交的钱进账；玩家同屋看得见的才写日志。</summary>
    private void SettleVisits(VisitReport report)
    {
        foreach (var visit in report.Arrived)
        {
            _visitorsGone.Remove(visit.CharacterId);
            if (visit.RoomId == PlayerRoomId)
                WriteActivity(visit.CharacterId, $"{NameOfPerson(visit.CharacterId)}从外面走了进来。");
        }
        foreach (var sale in report.Sales)
        {
            State.Money += sale.Income;
            var shop = Room(sale.ShopRoomId);
            if (shop == null || shop.Id != PlayerRoomId)
                continue;
            var buyer = NameOfPerson(sale.VisitorId);
            var seller = sale.SellerId == State.Roster.Master?.Id ? "你" : NameOfPerson(sale.SellerId);
            if (sale.Count == 0)
                WriteActivity(sale.VisitorId, $"{buyer}在{shop.Name}里看了一圈，没有看上什么。");
            else
                WriteActivity(sale.VisitorId,
                    $"{seller}把{sale.Count}件{ItemName(sale.ItemId)}卖给了{buyer}，收了 {sale.Income}G。");
            if (sale.LevelUp)
                Write($"{shop.Name}的名声传开了，升到了 {shop.ShopLevel} 级，往后上门的客人会更多。");
        }
        foreach (var id in report.Unserved)
        {
            var visit = State.Territory.Visits.Find(v => v.CharacterId == id);
            var shop = visit == null ? null : Room(visit.ShopRoomId);
            if (shop != null && shop.Id == PlayerRoomId && !report.Sales.Exists(s => s.VisitorId == id))
                WriteActivity(id, $"{NameOfPerson(id)}在{shop.Name}里没买成东西，转身走了。");
        }
        foreach (var id in report.Left)
            _visitorsGone.Add(id);
    }

    /// <summary>推进结束：在场表按访客此刻的位置落定，走出领地的摘掉。</summary>
    private void PlaceVisitors()
    {
        foreach (var id in _visitorsGone)
        {
            if (!State.Territory.Visits.Exists(v => v.CharacterId == id))
                _presence.Remove(id);
        }
        _visitorsGone.Clear();
        foreach (var visit in State.Territory.Visits)
            _presence[visit.CharacterId] = visit.RoomId;
    }

    /// <summary>
    /// 请访客留下入伙：好感到「好感」档（与邀请同行同一道门槛）就答应，从访客名册挪进住户名册——
    /// 从此睡、吃、排班都与别的住户一样；没地方睡会提一句怎么安顿。不够就婉拒。
    /// </summary>
    private void InviteVisitor(CharacterState who)
    {
        if (!who.AcceptsInvite())
        {
            Write($"{who.Name}笑着摇了摇头——还没熟到能留下来的地步。");
            return;
        }
        State.Territory.Visits.RemoveAll(v => v.CharacterId == who.Id);
        State.Roster.Admit(who.Id);
        Day.Track(who.Id, PlayerRoomId).RoomId = PlayerRoomId;
        _presence[who.Id] = PlayerRoomId;
        Write($"{who.Name}答应留下来，成了领地的一员。");
        HintHousing(who);
    }

    private string NameOfPerson(int id) => State.Roster.Person(id)?.Name ?? "";

    private static string ItemName(string itemId) =>
        Defs.Items.Get(itemId) is { } def && def.Label.Length > 0 ? def.Label : itemId;
}
