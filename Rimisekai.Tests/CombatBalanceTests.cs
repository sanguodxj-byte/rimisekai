using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Hub;
using Rimisekai.Quest;
using Rimisekai.Housing;
using Rimisekai.Save;
using Xunit;
using Xunit.Abstractions;

namespace Rimisekai.Tests;

/// <summary>
/// 胜率：玩家＋开局女仆＋第 3 天登门的旅人，打固定委托（鼠患、北坡头狼、狼群夜袭、废弃矿道）
/// 与委托板上 2–4 星的地城正主，自动战斗、骰子定种，每档跑多场算胜率。
/// </summary>
[Collection("Quest definition state")]
public sealed class CombatBalanceTests
{
    private readonly ITestOutputHelper _out;

    public CombatBalanceTests(ITestOutputHelper output) => _out = output;

    /// <summary>开局三人：照标准种子开局，跑到第 3 天旅人登门并留下。</summary>
    internal static HubSession Party(out GameState state, int seed)
    {
        var hub = TerritoryLoopTests.NewGame(out state, seed);
        TerritoryLoopTests.LoadVisitorEvent(state);
        hub.InitializeScheduledEvents();
        TerritoryLoopTests.DrainGeneration(hub);
        for (var i = 0; i < 4 * 24 * 6 && !hub.ScenePlaying; i++)
            hub.PassTime(10);
        TerritoryLoopTests.PlayScene(hub, 0);
        Assert.Equal(3, state.Roster.Members.Count);
        return hub;
    }

    internal static bool Fight(GameState state, IReadOnlyList<EnemyDef> foes, IReadOnlyList<List<EnemyDef>> waves, int seed) =>
        Brawl(state, foes, waves, seed).Outcome == CombatOutcome.AttackerWin;

    /// <summary>
    /// 名册全员（<paramref name="fresh"/> 时先回满血，否则体力多少血就多少）对一组敌人自动打完一场，骰子由 <paramref name="seed"/> 定。
    /// 上场与带药同 <see cref="Encounters.Start"/>：主手兵器面板、身上甲折护甲、主人背包里的药剂。
    /// </summary>
    internal static Battle Brawl(GameState state, IReadOnlyList<EnemyDef> foes, IReadOnlyList<List<EnemyDef>> waves, int seed,
        bool fresh = true)
    {
        var rng = new Random(seed);
        var battle = new Battle(null, () => rng.Next(100)) { AutoBattle = true };
        foreach (var c in state.Roster.Members)
        {
            if (fresh)
                c.Condition.RecoverFull();
            battle.Add(Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips));
        }
        Encounters.Pack(state, battle);
        var id = 1000;
        foreach (var f in foes)
            battle.Add(Deploy.FromEnemy(f, id++, CombatSide.Defender));
        foreach (var wave in waves)
            battle.QueueWave(wave.Select(f => Deploy.FromEnemy(f, id++, CombatSide.Defender)).ToList());
        battle.StartBattle();
        for (var guard = 0; guard < 20000 && battle.Outcome == CombatOutcome.Ongoing; guard++)
        {
            if (battle.SupplyRound)
                battle.Resupply();
            else
                battle.StepTurn(out _);
        }
        return battle;
    }

    private static List<(string Name, double Stars, IReadOnlyList<EnemyDef> Foes, IReadOnlyList<List<EnemyDef>> Waves)> Targets(GameState state)
    {
        var list = DefDatabase<QuestDef>.All.Where(q => !q.Generated && q.Foes.Count + q.Waves.Count > 0)
            .Select(q => (q.Name, q.Difficulty, (IReadOnlyList<EnemyDef>)q.Foes, (IReadOnlyList<List<EnemyDef>>)q.Waves)).ToList();
        var seen = new HashSet<string>();
        for (var day = 1; day <= 40; day++)
            for (var slot = 0; slot < 3; slot++)
            {
                var q = QuestBoard.Posting(state, day, slot);
                var tier = QuestBoard.TierOf(q.Difficulty);
                if (q.Difficulty < 2 || q.Difficulty > 4 || !seen.Add($"{q.Difficulty}:{string.Join(",", q.Foes.Select(f => f.Id))}"))
                    continue;
                list.Add(($"板·{q.Name}·{q.Difficulty}★·{string.Join("+", q.Foes.Select(f => f.Name))}", q.Difficulty, q.Foes, new List<List<EnemyDef>>()));
            }
        return list;
    }

    private static readonly EquipSlot[] ArmorSlots = { EquipSlot.Head, EquipSlot.Torso, EquipSlot.Legs, EquipSlot.Hands, EquipSlot.Feet };

    /// <summary>
    /// 给三人换一身：兵器按各自的原配种类（女仆没有原配，给匕首）用 <paramref name="weapon"/> 材料锻，
    /// 甲五件用 <paramref name="armor"/> 材料缝（空串 = 不穿）。品质一律普通，与集市/工坊出货同档。
    /// </summary>
    internal static void Kit(GameState state, string weapon, string armor, Quality quality = Quality.Common)
    {
        var smith = state.Roster.Master!;
        foreach (var c in state.Roster.Members)
        {
            var type = c.MainWeapon ?? WeaponType.Dagger;
            var id = state.Territory.ForgeGear(smith, new RecipeGear { Material = weapon, Weapon = type }, quality);
            smith.Bag.Add(id, -1);
            c.Equip(type);
            c.SetEquippedId(EquipSlot.MainHand, id);
            foreach (var slot in ArmorSlots)
            {
                c.SetEquippedId(slot, "");
                if (armor.Length == 0)
                    continue;
                var piece = state.Territory.ForgeGear(smith, new RecipeGear { Material = armor, Slot = slot }, quality);
                smith.Bag.Add(piece, -1);
                c.SetEquippedId(slot, piece);
            }
        }
    }

    private static readonly (string Name, string Weapon, string Armor, int Potions)[] Loadouts =
    {
        ("开局原配", "", "", 0),
        ("集市·木", "木材", "", 0),
        ("集市·青铜", "青铜", "", 0),
        ("工坊·铁兵＋皮甲", "铁", "皮", 0),
        ("工坊·铁兵＋铁甲", "铁", "铁", 0),
        ("工坊·钢兵＋铁甲＋药剂3", "钢", "铁", 3),
    };

    /// <summary>
    /// 各身行头 × 各委托跑 40 场，记胜率并卡住大致形状：
    /// 2–4 星板上委托与头狼、狼群开局原配就打得过（不至于卡关），换上工坊的铁兵铁甲更稳；
    /// 废弃矿道（12 星）原配打不下来，要工坊装备才有胜算。
    /// </summary>
    [Fact]
    public void Win_rates_by_loadout()
    {
        var rates = new Dictionary<(string Load, string Target), int>();
        var stars = new Dictionary<string, double>();
        foreach (var load in Loadouts)
        {
            Party(out var state, 3);
            if (load.Weapon.Length > 0)
                Kit(state, load.Weapon, load.Armor);
            state.Roster.Master!.Bag.Add("药剂", load.Potions);
            _out.WriteLine($"== {load.Name}");
            foreach (var c in state.Roster.Members)
            {
                var sheet = Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips);
                _out.WriteLine($"  {c.Name} 武器={c.MainWeapon} 熟练={c.Weapons[(int)(c.MainWeapon ?? WeaponType.Unarmed)].Level} HP={sheet.MaxHp} 出手={sheet.StrikePower} 防={sheet.Defence} 闪={sheet.Dodge} 甲={sheet.Armour}");
            }
            foreach (var t in Targets(state))
            {
                var wins = Enumerable.Range(0, 40).Count(i => Fight(state, t.Foes, t.Waves, i));
                rates[(load.Name, t.Name)] = wins;
                stars[t.Name] = t.Stars;
                _out.WriteLine($"  {t.Name} {t.Stars}★ 胜 {wins}/40");
            }
        }
        const string start = "开局原配", crafted = "工坊·铁兵＋铁甲", best = "工坊·钢兵＋铁甲＋药剂3";
        foreach (var (name, star) in stars)
        {
            Assert.True(rates[(crafted, name)] >= rates[(start, name)], $"{name}：工坊装备不该比原配差");
            if (star > 6.5)
                continue;
            Assert.True(rates[(start, name)] >= 20, $"{name} {star}★ 原配胜率 {rates[(start, name)]}/40");
            Assert.True(rates[(crafted, name)] >= 30, $"{name} {star}★ 工坊胜率 {rates[(crafted, name)]}/40");
        }
        Assert.True(rates[(start, "废弃矿道")] < 10, "废弃矿道原配打不下来");
        Assert.True(rates[(best, "废弃矿道")] >= 16, $"废弃矿道钢兵胜率 {rates[(best, "废弃矿道")]}/40");
    }
}
