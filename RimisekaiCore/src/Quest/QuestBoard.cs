using System;
using System.Collections.Generic;
using Rimisekai.Defs;
using Rimisekai.Save;
using Rimisekai.WorldMap;

namespace Rimisekai.Quest;

/// <summary>
/// 委托板：quests.json 里不在冷却的固定委托，加上按日现生成的地城探索委托。
/// 生成委托只由（世界种子, 张贴日, 第几张）决定，不进存档；了结记在通关次数里，了结过的不再挂出。
/// 第 d 天贴出的委托挂到第 d＋lifeDays−1 天，开局第一天板上就是满的（之前几天的也算贴过）。
/// </summary>
public static class QuestBoard
{
    /// <summary>此刻板上的全部委托，按编号排：固定的在前，生成的按张贴先后。</summary>
    public static List<QuestDef> Open(GameState state)
    {
        var list = new List<QuestDef>();
        foreach (var def in DefDatabase<QuestDef>.All)
            if (state.Quests.IsAvailable(def.Id))
                list.Add(def);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        var board = MapCatalog.Default.Dungeon.Quest.Board;
        var today = state.Clock.Day;
        for (var day = today - board.LifeDays + 1; day <= today; day++)
            for (var slot = 0; slot < board.PerDay; slot++)
            {
                var def = Posting(state, day, slot);
                if (state.Quests.ClearCount.GetValueOrDefault(def.Id) == 0)
                    list.Add(def);
            }
        return list;
    }

    /// <summary>第 <paramref name="day"/> 天贴出的第 <paramref name="slot"/> 张。同一局同一张永远一样。</summary>
    public static QuestDef Posting(GameState state, int day, int slot)
    {
        var board = MapCatalog.Default.Dungeon.Quest.Board;
        var id = board.IdBase + (day + board.LifeDays) * board.PerDay + slot;
        var key = (state.WorldSeed, id);
        if (Cache.TryGetValue(key, out var hit))
            return hit;

        var rng = new Random(HashCode.Combine(state.WorldSeed, id, 0x51ED));
        // 地点按张贴顺序轮转（起点随世界种子），板上同时挂着的不会撞名。
        var serial = (day + board.LifeDays) * board.PerDay + slot;
        var site = board.Sites[(int)(((uint)state.WorldSeed + (uint)serial) % (uint)board.Sites.Count)];
        var towns = state.World.Pois.FindAll(p => p.Type != WorldPoiType.Ruin && p.NameZh.Length > 0);
        var town = towns[rng.Next(towns.Count)];
        // 星数上限随日子从 StarsEarly 涨到 StarsMax（RampDays 天涨满），开局不会一上来就是骸骨王。
        var ramp = Math.Clamp((day - 1) / (double)board.RampDays, 0, 1);
        var cap = board.StarsEarly + (board.StarsMax - board.StarsEarly) * ramp;
        var halves = (int)Math.Round(board.StarsMin * 2) + rng.Next((int)Math.Round((cap - board.StarsMin) * 2) + 1);
        var stars = halves / 2.0;
        var tier = TierOf(stars);
        var bosses = MapCatalog.Default.Dungeon.Bosses.FindAll(b => b.MinTier <= tier);
        var boss = bosses[rng.Next(bosses.Count)];
        var money = (int)Math.Round((board.MoneyBase + board.MoneyPerStar * stars) / 10.0) * 10;
        var def = new QuestDef
        {
            Id = id,
            DefName = $"QuestBoard_{id}",
            Name = site.Name,
            Kind = QuestKind.Dungeon,
            Generated = true,
            Description = string.Format(site.Description, town.NameZh),
            Rumor = site.Rumors[rng.Next(site.Rumors.Count)],
            Difficulty = stars,
            MaxPartySize = board.PartyBase + tier,
            RewardMoney = money,
            Rewards = new List<string> { string.Format(board.RewardText, money) },
            Foes = boss.Foes,
        };
        Cache[key] = def;
        return def;
    }

    /// <summary>委托地城的危险等级：1＋星数÷每级星数，夹在 1–4。</summary>
    public static int TierOf(double stars) =>
        Math.Clamp(1 + (int)(stars / MapCatalog.Default.Dungeon.Quest.StarsPerTier), 1, 4);

    private static readonly Dictionary<(int Seed, int Id), QuestDef> Cache = new();
}
