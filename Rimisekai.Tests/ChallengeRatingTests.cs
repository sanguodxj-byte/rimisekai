using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.WorldMap;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>挑战等级：CR 写法、CR→等级表、等级→属性、怪物模板表、索引 A/B、遭遇生成（可复现、站位合法、栖息地对得上、首领房有正主）。</summary>
public sealed class ChallengeRatingTests
{
    private static ChallengeRating Cr(string text) => ChallengeRating.Parse(text);

    private static readonly string[] AllCr =
        new[] { "0", "1/8", "1/4", "1/2" }.Concat(Enumerable.Range(1, 30).Select(i => i.ToString())).ToArray();

    private static IReadOnlyCollection<string> AllFamilies() =>
        DefDatabase<MonsterDef>.All.Select(m => m.Family).Distinct().ToList();

    [Fact]
    public void Cr_strings_round_trip_and_reject_anything_else()
    {
        foreach (var text in AllCr)
            Assert.Equal(text, Cr(text).ToString());
        Assert.True(Cr("1/8") < Cr("1/4") && Cr("1/2") < Cr("1") && Cr("9") < Cr("10"));
        foreach (var bad in new[] { "0.25", "1/3", "31", "-1", "01", "" })
            Assert.Throws<FormatException>(() => Cr(bad));
        Assert.Equal(Cr("5"), Cr("1/2").Plus(4));
        Assert.Equal(Cr("30"), Cr("28").Plus(5));
    }

    [Fact]
    public void Cr_to_level_table_is_max_one_round_cr_times_five_capped_at_100()
    {
        ContentDefs.EnsureInitialized();
        var rules = ChallengeDef.Standard;
        Assert.Equal(AllCr, rules.Ratings.Select(r => r.Cr.ToString()));
        foreach (var row in rules.Ratings)
        {
            var expected = Math.Clamp((int)Math.Round(row.Cr.Value * 5, MidpointRounding.AwayFromZero), 1, 100);
            Assert.Equal(expected, row.Level);
        }
        Assert.Equal(1, rules.LevelOf(Cr("1/4")));
        Assert.Equal(20, rules.LevelOf(Cr("4")));
        Assert.Equal(100, rules.LevelOf(Cr("20")));
        Assert.Equal(100, rules.LevelOf(Cr("30")));
        // 经验值（遭遇预算）随 CR 严格上升。
        Assert.All(rules.Ratings.Zip(rules.Ratings.Skip(1)), p => Assert.True(p.Second.Xp > p.First.Xp));
    }

    [Fact]
    public void Level_to_pools_is_monotonic_and_fits_the_old_numbers()
    {
        ContentDefs.EnsureInitialized();
        var rules = ChallengeDef.Standard;
        for (var level = 2; level <= 100; level++)
        {
            Assert.True(rules.CorePool(level) >= rules.CorePool(level - 1));
            Assert.True(rules.ExpPool(level) > rules.ExpPool(level - 1));
        }
        // 每升一档整数 CR，两个池都变大。
        var whole = rules.Ratings.Where(r => r.Cr >= Cr("1") && r.Level < 100).ToList();
        Assert.All(whole.Zip(whole.Skip(1)), p =>
        {
            Assert.True(rules.CorePool(p.Second.Level) > rules.CorePool(p.First.Level));
            Assert.True(rules.ExpPool(p.Second.Level) > rules.ExpPool(p.First.Level));
        });
        // 拟合原表：最弱的软泥 3/60、深渊吞噬者 42/1010（CR 13）。
        Assert.Equal((3, 60), (rules.CorePool(rules.LevelOf(Cr("1/8"))), rules.ExpPool(rules.LevelOf(Cr("1/8")))));
        var worm = rules.LevelOf(DefDatabase<MonsterDef>.Get("abyss_worm")!.Cr);
        Assert.InRange(rules.CorePool(worm), 40, 44);
        Assert.InRange(rules.ExpPool(worm), 970, 1050);
    }

    [Fact]
    public void Monster_templates_are_complete_and_reference_real_content()
    {
        ContentDefs.EnsureInitialized();
        var map = MapCatalog.Default;
        var monsters = DefDatabase<MonsterDef>.All;
        Assert.NotEmpty(monsters);
        Assert.Equal(monsters.Count, monsters.Select(m => m.Id).Distinct().Count());
        var dungeonRoomTerrains = new[] { "ruins", "forest", "ancient_battlefield", "mine" }
            .ToDictionary(d => d, d => map.GetDungeonRoomTemplates(d).Select(r => r.Terrain).ToHashSet());
        foreach (var m in monsters)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Name), m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Family), m.Id);
            Assert.InRange(m.ThreatTier, 1, 4);
            Assert.InRange(m.Size, 1, m.ThreatTier);
            Assert.True(m.ActionPoints >= 1 && m.Armour >= 0 && m.Money >= 0, m.Id);
            Assert.All(m.Skills, id => Assert.NotNull(DefDatabase<SkillDef>.Get(id)));
            Assert.All(m.Loot, drop => Assert.NotNull(Items.Get(drop.ItemId)));
            if (m.Gear is { MinMaterial.Length: > 0 } gear)
                Assert.NotNull(DefDatabase<MaterialDef>.Get(gear.MinMaterial));
            Assert.All(m.Dungeons, dungeon => Assert.True(dungeonRoomTerrains.ContainsKey(dungeon), $"{m.Id} 的地城 {dungeon} 不存在"));
            // 写了房间地形就得是它那几种地城里真有的房间。
            Assert.All(m.Rooms, room => Assert.Contains(m.Dungeons, dungeon => dungeonRoomTerrains[dungeon].Contains(room)));
            Assert.All(m.Terrains, t => Assert.True(Enum.TryParse<WorldTerrainType>(t, out var terrain)
                && map.GetTravelMinutes(terrain) > 0, $"{m.Id} 的野外地形 {t} 不可通行或不存在"));
        }
    }

    [Fact]
    public void Index_a_buckets_by_cr_index_b_by_habitat_and_draw_is_their_intersection()
    {
        ContentDefs.EnsureInitialized();
        var monsters = DefDatabase<MonsterDef>.All;
        var index = MonsterIndex.Build();

        var low = index.ByCr(Cr("0"), Cr("1")).ToList();
        Assert.Equal(monsters.Where(m => m.Cr <= Cr("1")).OrderBy(m => m.Id), low.OrderBy(m => m.Id));

        var forest = index.ByHabitat(Habitat.Wild("Forest")).ToList();
        Assert.Equal(monsters.Where(m => m.Terrains.Contains("Forest")).OrderBy(m => m.Id), forest.OrderBy(m => m.Id));

        var mineLake = index.ByHabitat(Habitat.InDungeon("mine", "Lake")).ToList();
        Assert.All(mineLake, m => Assert.True(m.Dungeons.Contains("mine") && (m.Rooms.Count == 0 || m.Rooms.Contains("Lake"))));
        Assert.DoesNotContain(mineLake, m => m.Rooms.Count > 0 && !m.Rooms.Contains("Lake"));

        var habitat = Habitat.InDungeon("ruins", "Plains");
        var drawn = index.Draw(Cr("1/4"), Cr("4"), habitat);
        var a = index.ByCr(Cr("1/4"), Cr("4")).ToHashSet();
        var b = index.ByHabitat(habitat).ToHashSet();
        Assert.Equal(monsters.Where(m => a.Contains(m) && b.Contains(m)), drawn);
        Assert.NotEmpty(drawn);
    }

    /// <summary>阵容在 4×4 上合法：size ≤ 站位层、不出右边界、互不压格。</summary>
    internal static void AssertLegal(IReadOnlyList<MonsterSlot> lineup)
    {
        var grid = new bool[4, 4];
        foreach (var slot in lineup)
        {
            var size = slot.Monster.Size;
            Assert.InRange(slot.ThreatTier, size, 4);
            Assert.InRange(slot.Column, 1, 4 - size + 1);
            for (var c = slot.Column; c < slot.Column + size; c++)
                for (var l = slot.ThreatTier - size + 1; l <= slot.ThreatTier; l++)
                {
                    Assert.False(grid[c - 1, l - 1], $"{slot.Monster.Name} 压格");
                    grid[c - 1, l - 1] = true;
                }
        }
    }

    [Fact]
    public void Same_seed_same_lineup_and_every_lineup_is_legal_and_in_budget()
    {
        ContentDefs.EnsureInitialized();
        var rules = ChallengeDef.Standard;
        var families = AllFamilies();
        foreach (var dungeon in new[] { "ruins", "forest", "ancient_battlefield", "mine" })
            foreach (var room in MapCatalog.Default.GetDungeonRoomTemplates(dungeon).Select(r => r.Terrain).Distinct())
                for (var tier = 1; tier <= 4; tier++)
                {
                    var habitat = Habitat.InDungeon(dungeon, room);
                    var band = rules.Tier(tier);
                    for (var seed = 0; seed < 30; seed++)
                    {
                        var guard = EncounterGenerator.Compose(EncounterKind.Guard, tier, habitat, families, seed);
                        var again = EncounterGenerator.Compose(EncounterKind.Guard, tier, habitat, families, seed);
                        Assert.Equal(guard.Select(s => (s.Monster.Id, s.Column, s.ThreatTier)), again.Select(s => (s.Monster.Id, s.Column, s.ThreatTier)));
                        AssertLegal(guard);
                        Assert.InRange(guard.Count, 1, rules.Composition(EncounterKind.Guard).MaxCount);
                        Assert.True(guard.Sum(s => rules.XpOf(s.Cr)) <= band.BudgetXp);
                        Assert.All(guard, s => Assert.InRange(s.Cr.Eighths, band.CrMin.Eighths, band.CrMax.Eighths));
                        // 某地城类型只出该类型的怪，房间地形也对得上。
                        Assert.All(guard, s => Assert.Contains(dungeon, s.Monster.Dungeons));
                        Assert.All(guard, s => Assert.True(s.Monster.Rooms.Count == 0 || s.Monster.Rooms.Contains(room)));
                        Assert.DoesNotContain(guard, s => s.Boss);
                    }
                }
    }

    [Fact]
    public void Boss_rooms_always_have_a_leader_with_weaker_minions()
    {
        ContentDefs.EnsureInitialized();
        var rules = ChallengeDef.Standard;
        var families = AllFamilies();
        foreach (var dungeon in new[] { "ruins", "forest", "ancient_battlefield", "mine" })
            for (var tier = 1; tier <= 4; tier++)
                for (var seed = 0; seed < 30; seed++)
                {
                    var band = rules.Tier(tier);
                    var lineup = EncounterGenerator.Compose(EncounterKind.Boss, tier, Habitat.InDungeon(dungeon, "Road"), families, seed);
                    AssertLegal(lineup);
                    var leader = Assert.Single(lineup, s => s.Boss);
                    Assert.True(leader.Monster.CanLead);
                    Assert.InRange(leader.Cr.Eighths, band.BossCrMin.Eighths, band.BossCrMax.Eighths);
                    Assert.All(lineup.Where(s => !s.Boss), s => Assert.True(s.Cr < leader.Cr && s.Monster.CanFollow));
                    Assert.True(lineup.Where(s => !s.Boss).Sum(s => rules.XpOf(s.Cr))
                        <= rules.XpOf(leader.Cr) * rules.Composition(EncounterKind.Boss).MinionBudgetPercent / 100);
                    Assert.All(lineup, s => Assert.Contains(dungeon, s.Monster.Dungeons));
                }
        // 委托板加码：正主按整数档升 CR。
        var raised = EncounterGenerator.Compose(EncounterKind.Boss, 1, Habitat.InDungeon("ruins", "Road"), families, 7, leaderCrBonus: 3);
        var plain = EncounterGenerator.Compose(EncounterKind.Boss, 1, Habitat.InDungeon("ruins", "Road"), families, 7);
        Assert.Equal(plain[0].Monster, raised[0].Monster);
        Assert.Equal(plain[0].Cr.Plus(3), raised[0].Cr);
    }

    [Fact]
    public void Wild_lineups_follow_the_terrain()
    {
        ContentDefs.EnsureInitialized();
        var families = AllFamilies();
        foreach (var terrain in new[] { "Forest", "Swamp", "Plains" })
            for (var tier = 1; tier <= 4; tier++)
            {
                if (!EncounterGenerator.CanCompose(EncounterKind.Wild, tier, Habitat.Wild(terrain), families))
                    continue;
                for (var seed = 0; seed < 20; seed++)
                {
                    var lineup = EncounterGenerator.Compose(EncounterKind.Wild, tier, Habitat.Wild(terrain), families, seed);
                    AssertLegal(lineup);
                    Assert.All(lineup, s => Assert.Contains(terrain, s.Monster.Terrains));
                }
            }
        Assert.False(EncounterGenerator.CanCompose(EncounterKind.Wild, 1, Habitat.Wild("Swamp"), new[] { "wolf" }));
        Assert.True(EncounterGenerator.CanCompose(EncounterKind.Wild, 1, Habitat.Wild("Swamp"), new[] { "rat" }));
    }

    [Fact]
    public void Formation_keeps_big_units_inside_the_board()
    {
        var formation = new Formation();
        Assert.Equal((1, 4), formation.Find(4, 4));
        formation.Take(1, 4, 4);
        Assert.Null(formation.Find(1, 1));
        var small = new Formation();
        Assert.Equal((1, 1), small.Find(1, 1));
        Assert.Equal((1, 2), small.Find(1, 2));
        Assert.False(small.Fits(4, 2, 2));
        Assert.False(small.Fits(1, 1, 2));
    }
}
