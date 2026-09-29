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
/// 六维属性（带身份加成）+ 身份轴（称呼与职责语汇）+ 特质池 2~4 条（守槽位与互斥）
/// + 独有层三件套（经历/口癖/来意）。传入带种子的 Random 即可复现同一批角色。
/// </summary>
public sealed class CharacterGenerator
{
    private readonly Random _rng;

    public CharacterGenerator(Random? rng = null) => _rng = rng ?? new Random();

    /// <summary>从名册之外取名并生成，角色直接加入名册（名册负责发 Id）。</summary>
    public GeneratedCharacter Generate(Roster roster, IEnumerable<string>? extraUsedNames = null)
    {
        var used = new HashSet<string>(roster.Members.Select(m => m.Name));
        if (extraUsedNames != null)
        {
            foreach (var extra in extraUsedNames)
                used.Add(extra);
        }

        var name = RollName(used);
        var state = roster.Add(name);
        var identity = Identities[_rng.Next(Identities.Length)];
        var traits = RollTraits();

        foreach (var mechanic in identity.Grants)
            state.Grant(mechanic);

        // 六维属性：基础 3~8，再叠身份加成。
        for (var i = 0; i < state.Core.Length; i++)
            state.Core[i] = 3 + _rng.Next(6);
        foreach (var (stat, add) in identity.CoreBonus)
            state.Core[(int)stat] += add;

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

    // ---------- 身份轴（规范 11.8） ----------

    private sealed record IdentityDef(
        string Name,
        string Catchphrase,
        (CoreStat Stat, int Add)[] CoreBonus,
        Trait[] Grants);

    private static readonly IdentityDef[] Identities =
    {
        new("女仆", "请吩咐",
            new[] { (CoreStat.Dexterity, 1), (CoreStat.Constitution, 1) }, new[] { Trait.Maid }),
        new("骑士", "遵命",
            new[] { (CoreStat.Strength, 2), (CoreStat.Constitution, 1) }, Array.Empty<Trait>()),
        new("商人", "这笔买卖划算",
            new[] { (CoreStat.Charm, 2) }, Array.Empty<Trait>()),
        new("学者", "属下有一事禀报",
            new[] { (CoreStat.Intellect, 1), (CoreStat.Perception, 1) }, Array.Empty<Trait>()),
        new("神官", "愿 光保佑您",
            new[] { (CoreStat.Intellect, 1), (CoreStat.Charm, 1) }, Array.Empty<Trait>()),
        new("魔法师", "这个术式的原理是",
            new[] { (CoreStat.Intellect, 2) }, new[] { Trait.Mage }),
    };

    // ---------- 特质掷骰（池与互斥在 PersonalityTraits） ----------

    private List<PersonalityTraits.Def> RollTraits()
    {
        var target = _rng.Next(10) switch
        {
            < 3 => 2,
            < 7 => 3,
            _ => 4,
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

    private string RollName(ISet<string> used)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var name = NameStarts[_rng.Next(NameStarts.Length)];
            if (_rng.Next(2) == 0)
                name += NameMids[_rng.Next(NameMids.Length)];
            name += NameEnds[_rng.Next(NameEnds.Length)];
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
               $"经历：{Origins[_rng.Next(Origins.Length)]}，{Turns[_rng.Next(Turns.Length)]}。来到领地：{Reasons[_rng.Next(Reasons.Length)]}。\n" +
               $"口癖：{Tics[_rng.Next(Tics.Length)]}。\n" +
               $"亲密基线：{baseline}。感叹号档：{exclaim}。";
    }
}
