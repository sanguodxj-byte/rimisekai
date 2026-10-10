using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimisekai.Character;

/// <summary>
/// 生成结果。State 挂好六维属性、素质与起步好感，可直接进名册；
/// Persona 按 docs/voice-writing-guide.md 第十一节组装，供 LLM 生成层与内容检查使用。
/// </summary>
public sealed record GeneratedCharacter(
    CharacterState State,
    string Identity,
    IReadOnlyList<string> Traits,
    string Persona,
    string Summary);

/// <summary>
/// 运行时随机角色生成器。按写作规范第十一节的性格光谱掷骰：
/// 核心七维属性（角色自有，与身份无关）+ 身份轴（称呼、职责语汇与授予素质）
/// + 特质池 2~6 条（守槽位与互斥）+ 独有层三件套（经历/口癖/来意）
/// + 生活与战斗经验 3000（一半按逻辑决定，一半随机散发）。
/// 威胁等级不存数据、由流派推导；武器与流派不做预设配对，另行独立掷骰。
/// 传入带种子的 Random 即可复现同一批角色。
/// </summary>
public sealed class CharacterGenerator
{
    private readonly Random _rng;

    public CharacterGenerator(Random? rng = null) => _rng = rng ?? new Random();

    /// <summary>从名册之外取名并生成，角色直接加入名册（名册负责发 Id）。</summary>
    public GeneratedCharacter Generate(Roster roster, IEnumerable<string>? extraUsedNames = null)
    {
        var generated = Roll(roster, extraUsedNames);
        roster.Attach(generated.State);
        return generated;
    }

    /// <summary>
    /// 掷一个角色但**不入名册**：全套履历照掷，Id 先向名册预约一个不冲突的。
    /// 定时事件要提前把演员掷出来（好让后台按他的身份生成台词），
    /// 但人到点才登场，因此这一刻不能出现在名册与队伍面板里。
    /// 要让他入场时调 <see cref="Roster.Attach"/>。
    /// </summary>
    public GeneratedCharacter Roll(Roster roster, IEnumerable<string>? extraUsedNames = null)
    {
        var used = new HashSet<string>(roster.Members.Select(m => m.Name));
        if (extraUsedNames != null)
        {
            foreach (var extra in extraUsedNames)
                used.Add(extra);
        }

        var name = RollName(used);
        var state = new CharacterState(roster.ReserveId()) { Name = name };
        var idList = GetIdentities();
        var identity = idList[_rng.Next(idList.Count)];
        state.Identity = identity.Name;
        state.PortraitDiff = 1;
        var traits = RollTraits();

        foreach (var mechanic in identity.Grants)
            state.Grant(mechanic);
        foreach (var t in traits)
            state.Grant(t.Trait);

        ApplyLife(state, identity, traits.Select(t => t.Name).ToList());

        // 起步好感落在 None 档（<100）内：生成即陌生，感情留给玩法。
        var favor = _rng.Next(0, 61);
        if (favor > 0)
            state.Condition.AddFavor(favor);

        return new GeneratedCharacter(
            state,
            identity.Name,
            traits.Select(t => t.Name).ToList(),
            BuildPersona(name, identity, traits),
            $"{identity.Name}·{traits[0].Name}");
    }

    /// <summary>
    /// 给已建好的角色补全生活履历（开局种子角色走这条）：掷随机身份并授予其机制素质、
    /// 掷 2-6 条素质（多在 2-4，与 Generate 同规）授予角色、掷初始核心属性、
    /// 按身份配武器、发 3000 经验（种子素质与掷出素质一并对口），最后满状态起步。
    /// 名字、主仆、好感与种子素质归调用方。
    /// </summary>
    public void Populate(CharacterState state, IEnumerable<string> traitNames)
    {
        var idList = GetIdentities();
        var identity = idList[_rng.Next(idList.Count)];
        state.Identity = identity.Name;
        state.PortraitDiff = 1;
        foreach (var mechanic in identity.Grants)
            state.Grant(mechanic);
        var seed = traitNames.ToList();
        var rolled = RollTraits();
        foreach (var t in rolled)
            state.Grant(t.Trait);
        ApplyLife(state, identity, seed.Concat(rolled.Select(t => t.Name)).ToList());
    }

    /// <summary>角色的属性池：底线 6 之上再分 21 点。</summary>
    public const int CorePool = 21;

    /// <summary>
    /// 角色的经验池：一半按逻辑、一半随机散发（拟案，待主人核定）。
    /// 定成 720：武器/流派最坏情况拿到逻辑的 1/4 加上全部随机份（共 3/4 池 = 540），开局战斗熟练不超过 5；
    /// 生活最坏情况拿满整池（720），开局生活等级不超过 10。
    /// </summary>
    public const int ExpPool = 720;

    /// <summary>
    /// 核心属性：全属性底线 6，再把 <paramref name="pool"/> 点按天资倾向分下去——
    /// 主属性 5 点、副属性 3 点（池不够就先紧着主、副），余下逐点随机散发。
    /// 主副属性不给就随机掷（每个个体有自己的天赋倾向）。速度保底在 [10, 16]。
    /// </summary>
    private void RollCore(CharacterState state, int pool, CoreStat? primary = null, CoreStat? secondary = null)
    {
        const int BaseAttribute = 6;
        for (var i = 0; i < state.Core.Length; i++)
            state.Core[i] = BaseAttribute;

        var primaryStat = primary ?? (CoreStat)_rng.Next(state.Core.Length);
        var secondaryStat = secondary ?? (CoreStat)_rng.Next(state.Core.Length);
        while (secondaryStat == primaryStat)
            secondaryStat = (CoreStat)_rng.Next(state.Core.Length);

        var left = Math.Max(0, pool);
        var toPrimary = Math.Min(5, left);
        state.Core[(int)primaryStat] += toPrimary;
        left -= toPrimary;
        var toSecondary = Math.Min(3, left);
        state.Core[(int)secondaryStat] += toSecondary;
        left -= toSecondary;
        while (left-- > 0)
            state.Core[_rng.Next(state.Core.Length)]++;

        // 速度保底在 [10, 16] 竞技作战合理区间，杜绝残疾速度
        state.Core[(int)CoreStat.Speed] = Math.Clamp(state.Core[(int)CoreStat.Speed], 10, 16);
    }

    /// <summary>
    /// 掷一个怪物：视为**没有生活技能的角色**，与角色走同一套规则——
    /// 底线 6 加属性池（默认与角色同为 21 点，主副属性可由内容指定），
    /// 经验池（默认与角色同池）一半按逻辑给手持武器与推导流派、一半按 1/30 池一份在全部经验轨上散发，
    /// 只是落到生活轨上的那几份作废（怪物没有生活技能）。不给武器即徒手（爪牙）。
    /// 不入名册、不掷身份与特质、不发好感；等级与角色开局一样从 1 起，满状态起步。
    /// </summary>
    public CharacterState RollMonster(int id, string name, WeaponType? weapon = null,
        CoreStat? primary = null, CoreStat? secondary = null, int corePool = CorePool, int expPool = ExpPool)
    {
        var state = new CharacterState(id) { Name = name };
        RollCore(state, corePool, primary, secondary);
        var main = weapon ?? WeaponType.Unarmed;
        state.Equip(main);
        var style = state.EquippedStyle ?? StyleType.Unarmed;

        const int ExpChunk = ExpPool / 30;
        var logic = Math.Max(0, expPool) / 2;
        state.Weapons[(int)main].AddExp(logic / 2);
        state.Styles[(int)style].AddExp(logic - logic / 2);
        var tracks = AttributeMap.LifeCount + state.Weapons.Length + state.Styles.Length;
        var scatterLeft = Math.Max(0, expPool) - logic;
        while (scatterLeft > 0)
        {
            var add = Math.Min(ExpChunk, scatterLeft);
            var track = _rng.Next(tracks);
            if (track >= AttributeMap.LifeCount + state.Weapons.Length)
                state.Styles[track - AttributeMap.LifeCount - state.Weapons.Length].AddExp(add);
            else if (track >= AttributeMap.LifeCount)
                state.Weapons[track - AttributeMap.LifeCount].AddExp(add);
            // 落在生活轨上的作废：怪物没有生活技能。
            scatterLeft -= add;
        }
        state.Condition.RecoverFull();
        return state;
    }

    /// <summary>两条生成路径共用的生活履历：初始属性、身份装备、经验池、满状态起步。</summary>
    private void ApplyLife(CharacterState state, IdentityDef identity, IReadOnlyList<string> traitNames)
    {
        // ---------- 1. 强制分配初始核心属性点（与身份完全无关） ----------
        RollCore(state, CorePool);

        // ---------- 2. 初始装备按身份授予（战斗类身份自带武器，非战斗类为空） ----------
        state.Equip(identity.MainWeapon, identity.OffWeapon, identity.OffShield);
        var equippedStyle = state.EquippedStyle;

        // ---------- 3. 经验池：一半按逻辑，一半随机 ----------
        // 仅核心七维是角色自有；经验是发出来的，不预设搭配。
        // 逻辑的一半：特质对口的技能各 1/20 池。
        // 若身份自带武器（战斗类），余量分给手持主武器与实际推导流派；
        // 若身份无武器（非战斗类），余量注入生活技能池（模拟过往生活阅历）。
        // 随机的一半：1/30 池一份，在所有经验轨（生活 + 武器 + 流派）上散发。
        // 份额都按池等比，池缩小时分配规则不变。
        const int ExpPerTrait = ExpPool / 20;
        const int ExpChunk = ExpPool / 30;
        var skillOfTrait = new Dictionary<string, LifeSkill>
        {
            ["吃货"] = LifeSkill.Cooking,
            ["工匠"] = LifeSkill.Craft,
            ["手巧"] = LifeSkill.Craft,
            ["炼金"] = LifeSkill.Research,
            ["书痴"] = LifeSkill.Research,
            ["好学"] = LifeSkill.Research,
            ["务实"] = LifeSkill.Mining,
            ["圆滑"] = LifeSkill.Social,
            ["健谈"] = LifeSkill.Social,
        };
        var tracks = AttributeMap.LifeCount + state.Weapons.Length + state.Styles.Length;
        void AddExp(int track, int amount)
        {
            if (track < AttributeMap.LifeCount)
                state.LifeExp[track] += amount;
            else if (track < AttributeMap.LifeCount + state.Weapons.Length)
                state.Weapons[track - AttributeMap.LifeCount].AddExp(amount);
            else
                state.Styles[track - AttributeMap.LifeCount - state.Weapons.Length].AddExp(amount);
        }

        var logicLeft = ExpPool / 2;
        foreach (var t in traitNames)
        {
            if (logicLeft < ExpPerTrait || !skillOfTrait.TryGetValue(t, out var skill))
                continue;
            state.LifeExp[(int)skill] += ExpPerTrait;
            logicLeft -= ExpPerTrait;
        }
        if (logicLeft > 0)
        {
            if (identity.MainWeapon != null)
            {
                // 战斗类身份：逻辑余量分给手持主武器与实际推导流派
                var mainShare = logicLeft / 2;
                state.Weapons[(int)identity.MainWeapon.Value].AddExp(mainShare);
                state.Styles[(int)equippedStyle!.Value].AddExp(logicLeft - mainShare);
            }
            else
            {
                // 非战斗类身份：无初始装备，逻辑余量注入生活技能池（过往生活历练）
                while (logicLeft > 0)
                {
                    var skill = (LifeSkill)_rng.Next(AttributeMap.LifeCount);
                    var add = Math.Min(ExpChunk, logicLeft);
                    state.LifeExp[(int)skill] += add;
                    logicLeft -= add;
                }
            }
        }
        var scatterLeft = ExpPool / 2;
        while (scatterLeft > 0)
        {
            var add = Math.Min(ExpChunk, scatterLeft);
            AddExp(_rng.Next(tracks), add);
            scatterLeft -= add;
        }

        // 满状态起步：生命与体力充沛
        state.Condition.RecoverFull();
        // 身份技能池：按本身份抽一池（身份没有技能池即空）。骰子由掷好的人推出、不动生成骰子，
        // 同一种子掷出的人与技能池都不变。
        Combat.SkillPool.Assign(state, identity.Name, new Random(PoolSeed(state)));
    }

    /// <summary>由已掷好的角色推出技能池种子：名字、编号、核心属性、经验，确定且与生成骰子无关。</summary>
    private static int PoolSeed(CharacterState state)
    {
        var seed = state.Id * 7919 + state.LevelExp;
        foreach (var ch in state.Name)
            seed = seed * 31 + ch;
        foreach (var v in state.Core)
            seed = seed * 17 + v;
        return seed;
    }

    // ---------- 身份轴（规范 11.8） ----------

    private sealed record IdentityDef(
        string Name,
        string Catchphrase,
        Trait[] Grants,
        WeaponType? MainWeapon = null,
        WeaponType? OffWeapon = null,
        bool OffShield = false);

    private static readonly IdentityDef[] Identities =
    {
        new("女仆", "请吩咐", new[] { Trait.Maid }),
        new("骑士", "遵命", Array.Empty<Trait>(), WeaponType.Sword, null, true),
        new("商人", "这笔买卖划算", Array.Empty<Trait>()),
        new("学者", "属下有一事禀报", Array.Empty<Trait>()),
        new("神官", "愿 光保佑您", Array.Empty<Trait>()),
        new("魔法师", "这个术式的原理是", new[] { Trait.Mage }, WeaponType.Staff),
        // ---------- 第二批扩充（2026-09-30 主人指示补充） ----------
        new("战士", "跟紧我", Array.Empty<Trait>(), WeaponType.Sword, WeaponType.Sword),
        new("护卫", "这里有我", Array.Empty<Trait>(), WeaponType.Spear, null, true),
        new("佣兵", "钱到位什么都好说", Array.Empty<Trait>(), WeaponType.Axe),
        new("弓箭手", "风向我看过了", Array.Empty<Trait>(), WeaponType.Bow),
        new("猎人", "这一带山路我熟", Array.Empty<Trait>(), WeaponType.Crossbow),
        new("刺客", "别出声", Array.Empty<Trait>(), WeaponType.Dagger, WeaponType.Sword),
        new("盗贼", "就当没来过", Array.Empty<Trait>(), WeaponType.Dagger),
        new("吟游诗人", "听我唱一段", Array.Empty<Trait>()),
        new("舞娘", "看清楚每一个动作", Array.Empty<Trait>()),
        new("药师", "苦口良药", new[] { Trait.Alchemist }),
        new("炼金术士", "配方还差一味", new[] { Trait.Alchemist }),
        new("铁匠", "火候差一分都不行", new[] { Trait.Artisan }),
        new("木匠", "木头有木头的脾气", new[] { Trait.Artisan }),
        new("厨师", "先尝一口再说", Array.Empty<Trait>()),
        new("花匠", "浇水要趁天没亮", Array.Empty<Trait>()),
        new("信使", "顺路的都归我送", Array.Empty<Trait>()),
        new("修女", "愿您心安", Array.Empty<Trait>()),
        new("僧侣", "静以修身", Array.Empty<Trait>()),
        new("德鲁伊", "万物自有其时", Array.Empty<Trait>(), WeaponType.Staff),
        new("游侠", "路在脚下", Array.Empty<Trait>(), WeaponType.Bow),
        new("圣骑士", "誓约所指", Array.Empty<Trait>(), WeaponType.Sword, null, true),
        new("贵族", "注意你的身份", Array.Empty<Trait>()),
        new("管家", "一切都已安排妥当", Array.Empty<Trait>()),
        new("学者助手", "资料我整理好了", Array.Empty<Trait>()),
        new("星术师", "星象不会说谎", Array.Empty<Trait>()),
    };

    // ---------- 特质掷骰（池与互斥在 PersonalityTraits） ----------

    private List<PersonalityTraits.Def> RollTraits()
    {
        var target = _rng.Next(9) switch
        {
            < 2 => 2,
            < 5 => 3,
            < 7 => 4,
            < 8 => 5,
            _ => 6,
        };

        var picked = new List<PersonalityTraits.Def>();
        for (var attempt = 0; attempt < 400 && picked.Count < target; attempt++)
        {
            var candidate = Traits.Pool[_rng.Next(Traits.Pool.Count)];
            if (picked.Contains(candidate))
                continue;
            if (picked.Count(t => t.Group == candidate.Group) >= 2)
                continue;
            if (picked.Any(t => Excludes(t.Name, candidate.Name)))
                continue;
            picked.Add(candidate);
        }
        return picked;
    }

    private static bool Excludes(string a, string b) =>
        Traits.Exclusions.Any(x => (x.A == a && x.B == b) || (x.A == b && x.B == a));

    // ---------- 独有层素材表 ----------

    private static readonly string[] Origins =
    {
        "学院毕业不久，实战经验尚浅",
        "商队里长大，识得各地行情",
        "小乡村出身，被招募而来",
        "流落至此，被好意收留",
        "行会一纸推荐信",
        "家道中落，出来做事",
        "自小在修道院里长大",
        "在旧东家那里学会了少说话多做事",
    };

    private static readonly string[] Turns =
    {
        "一次失败让她比谁都努力",
        "被人夸过一次，记到今天",
        "在旧东家那里学会了看脸色",
        "一场大病之后看开了许多",
        "被人瞧不起过，格外珍惜现在",
        "师父的叮嘱一直记着",
        "赢过一次比试，从此有了底气",
        "弄丢过要紧的东西，再不敢马虎",
    };

    private static readonly string[] Reasons =
    {
        "听说这片领地正在招人",
        "走投无路时被大人收留",
        "行会的推荐信指名来这里",
        "为还一笔债，愿意做工抵偿",
        "慕这片领地的名而来",
        "旧识说这里的主厚道",
    };

    private static readonly string[] Tics =
    {
        "紧张时重复句尾（『……的、的样子』）",
        "句首带『呐』",
        "句尾缀『……罢了』",
        "自称用自己的名字",
        "叹气开头『唉』",
        "敬语过剩",
        "爱说『总、总之』",
        "句尾挂『——大概』",
        "说到兴起会冒出术语",
        "常说『请、请别介意』",
        "口头『真是的』",
        "句尾『……才怪』",
    };

    // ---------- 名字音节 ----------

    private static readonly string[] NameStarts =
    {
        "璐", "艾", "莉", "赛", "玛", "薇", "诺", "米",
        "安", "瑞", "菲", "萝", "伊", "缇", "洁", "兰",
    };

    private static readonly string[] NameMids =
    {
        "丽", "娜", "拉", "薇", "丝", "瑞", "尔", "莉",
        "雅", "米", "娅", "菲", "莎", "维", "黛", "茵",
    };

    private static readonly string[] NameEnds =
    {
        "尔", "娜", "丝", "特", "雅", "拉", "莉", "娅",
        "恩", "薇", "莎", "茵", "黛", "兰", "埃", "蕾",
    };

    // ---------- 掷骰 ----------

    private IReadOnlyList<IdentityDef> GetIdentities()
    {
        Defs.DefLoader.EnsureInitialized();
        var defs = Defs.DefDatabase<Defs.IdentityDef>.All;
        if (defs.Count > 0)
        {
            var list = new List<IdentityDef>();
            foreach (var d in defs)
            {
                var grants = new List<Trait>();
                foreach (var g in d.Grants)
                {
                    if (System.Enum.TryParse<Trait>(g, ignoreCase: true, out var t))
                        grants.Add(t);
                }
                WeaponType? main = null;
                if (!string.IsNullOrEmpty(d.MainWeapon) && Enum.TryParse<WeaponType>(d.MainWeapon, ignoreCase: true, out var mw))
                    main = mw;
                WeaponType? off = null;
                if (!string.IsNullOrEmpty(d.OffWeapon) && Enum.TryParse<WeaponType>(d.OffWeapon, ignoreCase: true, out var ow))
                    off = ow;
                list.Add(new IdentityDef(d.Label, d.Catchphrase, grants.ToArray(), main, off, d.OffShield));
            }
            return list;
        }
        return Identities;
    }

    private IReadOnlyList<string> GetOrigins() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.Origins is { Count: > 0 } l ? l : Origins;
    private IReadOnlyList<string> GetTurns() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.Turns is { Count: > 0 } l ? l : Turns;
    private IReadOnlyList<string> GetReasons() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.Reasons is { Count: > 0 } l ? l : Reasons;
    private IReadOnlyList<string> GetTics() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.Tics is { Count: > 0 } l ? l : Tics;
    private IReadOnlyList<string> GetNameStarts() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.NameStarts is { Count: > 0 } l ? l : NameStarts;
    private IReadOnlyList<string> GetNameMids() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.NameMids is { Count: > 0 } l ? l : NameMids;
    private IReadOnlyList<string> GetNameEnds() => Defs.DefDatabase<Defs.PersonaPartsDef>.Get("Default")?.NameEnds is { Count: > 0 } l ? l : NameEnds;

    private string RollName(ISet<string> used)
    {
        var starts = GetNameStarts();
        var mids = GetNameMids();
        var ends = GetNameEnds();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var name = starts[_rng.Next(starts.Count)];
            if (_rng.Next(2) == 0 && mids.Count > 0)
                name += mids[_rng.Next(mids.Count)];
            name += ends[_rng.Next(ends.Count)];
            if (name.Length >= 2 && !used.Contains(name))
                return name;
        }
        var fallback = "旅人" + _rng.Next(100, 1000);
        while (used.Contains(fallback))
            fallback = "旅人" + _rng.Next(100, 1000);
        return fallback;
    }

    private string BuildPersona(string name, IdentityDef identity, List<PersonalityTraits.Def> traits)
    {
        var origins = GetOrigins();
        var turns = GetTurns();
        var reasons = GetReasons();
        var tics = GetTics();

        var traitText = string.Join("、", traits.Select(t => $"{t.Name}（{t.Keynote}）"));
        var baseline = traits.Any(t => t.F)
            ? "F——按第六节四档（礼貌距离→决堤）写作"
            : "T——关心翻译成事务，Lover 档只失守一次";
        var exclaim = traits.Max(t => t.Exclaim) switch
        {
            0 => "禁用",
            1 => "偶用",
            _ => "常用",
        };

        return $"{name}：{traitText}；{identity.Name}。\n" +
               $"称呼玩家为「大人」，惯用语汇「{identity.Catchphrase}」。\n" +
               $"经历：{origins[_rng.Next(origins.Count)]}，{turns[_rng.Next(turns.Count)]}。来到领地：{reasons[_rng.Next(reasons.Count)]}。\n" +
               $"口癖：{tics[_rng.Next(tics.Count)]}。\n" +
               $"亲密基线：{baseline}。感叹号档：{exclaim}。";
    }
}
