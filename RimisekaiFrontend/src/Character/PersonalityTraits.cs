using System.Collections.Generic;
using System.Linq;
using Rimisekai.Housing;

namespace Rimisekai.Character;

/// <summary>社交好感的作用面。SocialBoard 把 SocialAction 映射到这里。</summary>
public enum SocialKind
{
    Talk,
    Gift,
    Care,
    Intimate,
}

/// <summary>
/// 性格光谱（docs/voice-writing-guide.md 第十一节）的权威数据与游戏性数值。
/// 每条特质一个 Def：中文名/组/基调/感叹号档是给对话侧和 persona 用的；
/// 下方的效果方法把特质翻译成游戏性修正，全部数值集中在本文件，便于整体调整。
/// </summary>
public static class PersonalityTraits
{
    public sealed record Def(Trait Trait, string Name, string Group, string Keynote, int Exclaim, bool F);

    /// <summary>全量目录：76 项特质，严格由 content/defs/traits.json 驱动，代码内禁止硬编码。</summary>
    public static IReadOnlyList<Def> All
    {
        get
        {
            Defs.DefLoader.EnsureInitialized();
            var list = new List<Def>();
            foreach (var d in Defs.DefDatabase<Defs.TraitDef>.All)
            {
                if (System.Enum.TryParse<Trait>(d.DefName, ignoreCase: true, out var t))
                    list.Add(new Def(t, d.Label, d.Group, d.Keynote, d.Exclaim, d.IntimateBaseline));
            }
            return list;
        }
    }

    public static Def? Of(Trait trait)
    {
        Defs.DefLoader.EnsureInitialized();
        var def = Defs.DefDatabase<Defs.TraitDef>.Get(trait.ToString());
        if (def == null)
            return null;
        return new Def(trait, def.Label, def.Group, def.Keynote, def.Exclaim, def.IntimateBaseline);
    }

    /// <summary>互斥对（特质名）。权威数据源为 content/defs/trait_exclusions.json。</summary>
    public static (string A, string B)[] Exclusions
    {
        get
        {
            Defs.DefLoader.EnsureInitialized();
            return Defs.DefDatabase<Defs.TraitExclusionDef>.All
                .Select(e => (e.A, e.B))
                .ToArray();
        }
    }


    /// <summary>生成器的抽取池：数据表里 notInPool=false 的光谱特质。</summary>
    public static IReadOnlyList<Def> Pool
    {
        get
        {
            Defs.DefLoader.EnsureInitialized();
            var list = new List<Def>();
            foreach (var d in Defs.DefDatabase<Defs.TraitDef>.All)
            {
                if (d.NotInPool)
                    continue;
                if (System.Enum.TryParse<Trait>(d.DefName, ignoreCase: true, out var t))
                    list.Add(new Def(t, d.Label, d.Group, d.Keynote, d.Exclaim, d.IntimateBaseline));
            }
            return list;
        }
    }

    /// <summary>全量目录：与 All 同源，严格由数据表驱动。</summary>
    public static IReadOnlyList<Def> Catalog => All;

    // ---------- 游戏性效果 ----------

    /// <summary>工作进度百分比（100 为基准）。hour 用于作息类特质的时段修正。</summary>
    public static int WorkProgressPercent(CharacterState c, ActionKind action, int hour)
    {
        var p = 100;
        if (c.Has(Trait.Dutiful)) p += 10;
        if (c.Has(Trait.Procrastinator)) p -= 15;
        if (c.Has(Trait.Methodical)) p += 5;
        if (c.Has(Trait.FreeSpirit)) p -= 5;
        if (c.Has(Trait.HotTempered)) p += 10;
        if (c.Has(Trait.Dreamer)) p -= 5;
        if (c.Has(Trait.Defiant)) p -= 5;
        if ((c.Has(Trait.Nervous) || c.Has(Trait.Timid)) && action == ActionKind.Sew) p -= 5;
        if (c.Has(Trait.Artisan) && action is ActionKind.Woodwork or ActionKind.Sew or ActionKind.Forge) p += 10;
        if (c.Has(Trait.Alchemist) && action == ActionKind.Brew) p += 15;

        // 认知/手艺向的任务对口加成
        if (c.Has(Trait.DownToEarth) && action is ActionKind.Till or ActionKind.Mine or ActionKind.Fell) p += 10;
        if (c.Has(Trait.Bookish) && action == ActionKind.Brew) p += 15;
        if (c.Has(Trait.Meticulous) && action == ActionKind.Sew) p += 10;
        if (c.Has(Trait.DeftHands) && action is ActionKind.Sew or ActionKind.Woodwork) p += 15;
        if (c.Has(Trait.NeatFreak) && action == ActionKind.Cook) p += 10;

        // 作息类：夜猫子昼弱夜强，怕鬼夜里拖后腿，起床气只在清晨
        var night = hour >= 21 || hour < 5;
        var morning = hour >= 5 && hour < 9;
        if (night && c.Has(Trait.NightOwl)) p += 15;
        if (morning && c.Has(Trait.NightOwl)) p -= 10;
        if (night && c.Has(Trait.NightFearful)) p -= 25;
        if (morning && c.Has(Trait.GrouchyMornings)) p -= 15;

        return System.Math.Clamp(p, 25, 200);
    }

    /// <summary>心情增益百分比（100 为基准）。只用于正数心情。</summary>
    public static int MoodGainPercent(CharacterState c)
    {
        var p = 100;
        if (c.Has(Trait.Expressive)) p += 20;
        if (c.Has(Trait.Optimist)) p += 10;
        if (c.Has(Trait.FreeSpirit)) p += 10;
        if (c.Has(Trait.Dreamer)) p += 10;
        if (c.Has(Trait.Melancholy)) p -= 15;
        if (c.Has(Trait.PeoplePleaser)) p -= 10;
        if (c.Has(Trait.StillWaters)) p -= 10;
        if (c.Has(Trait.Sentimental)) p -= 5;
        return System.Math.Clamp(p, 25, 200);
    }

    /// <summary>心情换算。负数（惩罚）不放大，只缩放正反馈。</summary>
    public static int ScaledMood(CharacterState c, int mood) =>
        mood > 0 ? mood * MoodGainPercent(c) / 100 : mood;

    /// <summary>
    /// 接触接受门槛的特质百分比（100 为基准，乘在基础好感档上）。
    /// 沿用好感面的同一批性格：傲慢/自尊更矜持，讨好/体贴更受用，
    /// 慢热在陌生期上浮，护短在心情好时把这里当家、门槛下调。
    /// </summary>
    public static int TouchGatePercent(CharacterState c)
    {
        var p = 100;
        if (c.Has(Trait.Arrogant) || c.Has(Trait.Prideful)) p += 30;
        if (c.Has(Trait.PeoplePleaser)) p -= 25;
        if (c.Has(Trait.Attentive)) p -= 20;
        if (c.Has(Trait.SlowWarmer) && c.Condition.Bond == Bond.None) p += 30;
        if (c.Has(Trait.Protective) && c.Affect.Mood >= 60) p -= 15;
        return System.Math.Clamp(p, 50, 200);
    }

    /// <summary>社交好感换算。调用方把原始好感增量传进来。</summary>
    public static int ScaledFavor(CharacterState c, SocialKind kind, int favor) =>
        favor * SocialFavorPercent(c, kind) / 100;

    /// <summary>社交好感百分比（100 为基准）。慢热在陌生期全面打折。</summary>
    public static int SocialFavorPercent(CharacterState c, SocialKind kind)
    {
        var p = 100;
        switch (kind)
        {
            case SocialKind.Talk:
                if (c.Has(Trait.Talkative)) p += 10;
                if (c.Has(Trait.Quiet)) p -= 10;
                if (c.Has(Trait.SharpTongued)) p -= 15;
                if (c.Has(Trait.SocialChameleon)) p += 15; // 圆滑：擅长客套应酬话术
                if (c.Has(Trait.PeoplePleaser)) p += 20;
                break;
            case SocialKind.Gift:
                if (c.Has(Trait.PeoplePleaser)) p += 20;
                if (c.Has(Trait.Frugal)) p += 20;
                if (c.Has(Trait.Generous)) p += 20; // 大方：出手阔绰爱分享礼赠
                break;
            case SocialKind.Care:
                if (c.Has(Trait.Attentive)) p += 20;
                if (c.Has(Trait.Worrier)) p += 10;
                break;
            case SocialKind.Intimate:
                if (c.Has(Trait.Arrogant) || c.Has(Trait.Prideful)) p -= 20;
                break;
        }
        if (c.Has(Trait.SlowWarmer) && c.Condition.Bond == Bond.None)
            p -= 20;
        // 护短：日子过得好（心情高）时，把这里当家守护，正反馈更多。
        if (c.Has(Trait.Protective) && c.Affect.Mood >= 60)
            p += 15;
        return System.Math.Clamp(p, 25, 200);
    }

    /// <summary>进餐体力恢复加成。</summary>
    public static int MealStamina(CharacterState c, int stamina) =>
        c.Has(Trait.Foodie) ? stamina + 10 : stamina;

    /// <summary>进餐精神恢复加成（吃货吃得香恢复更好）。</summary>
    public static int MealSpirit(CharacterState c, int spirit) =>
        c.Has(Trait.Foodie) ? spirit + 10 : spirit;

    /// <summary>进餐心情百分比（100 为基准）。吃货吃什么都香。</summary>
    public static int MealMoodPercent(CharacterState c) =>
        c.Has(Trait.Foodie) ? 200 : 100;

    /// <summary>学习速度加成（叠加进 Traits.LearnPercent）。</summary>
    public static int LearnBonusPercent(CharacterState c)
    {
        var p = 0;
        if (c.Has(Trait.Inquisitive)) p += 25;
        if (c.Has(Trait.Bookish)) p += 10;
        return p;
    }

    /// <summary>交谈难度加成（叠加进 Traits.TalkDifficulty）。</summary>
    public static int TalkDifficultyBonus(CharacterState c)
    {
        var d = 0;
        if (c.Has(Trait.Nervous) || c.Has(Trait.Timid)) d += 1;
        if (c.Has(Trait.Arrogant) || c.Has(Trait.Defiant)) d += 1;
        if (c.Has(Trait.Forthright)) d -= 1;
        return d;
    }

    /// <summary>主动找人搭话的等待预算修正（负数=更早放弃）。</summary>
    public static int SeekBudgetDelta(CharacterState c) =>
        c.Has(Trait.Directionless) ? -12 : 0;

    /// <summary>休息时的额外精神恢复（迷信：祈神安心）。</summary>
    public static int RestSpiritBonus(CharacterState c) =>
        c.Has(Trait.Superstitious) ? 10 : 0;

    // ---------- 机制素质的全量计算（Traits 门面委托到这里） ----------

    public static int LearnPercentTotal(CharacterState c)
    {
        var percent = 100;
        if (c.Has(Trait.FastLearner)) percent += 50;
        if (c.Has(Trait.SlowLearner)) percent -= 50;
        percent += LearnBonusPercent(c);
        return percent < 0 ? 0 : percent;
    }

    public static int TalkDifficultyTotal(CharacterState c)
    {
        var difficulty = 0;
        if (c.Has(Trait.Cold) || c.Has(Trait.Prideful)) difficulty += 1;
        if (c.Condition.Bond == Bond.Dislike) difficulty += 1;
        if (c.Has(Trait.Curious) || c.Has(Trait.Honest)) difficulty -= 1;
        difficulty += TalkDifficultyBonus(c);
        return difficulty;
    }

    public static bool WillWork(CharacterState c, bool hardLabor) =>
        hardLabor
            ? !c.Has(Trait.Lazy) && !c.Has(Trait.FearPain)
            : true;

    public static bool RequiresWage(CharacterState c) => !c.Has(Trait.Maid);

    public static bool AcceptsInvite(CharacterState c) =>
        c.Has(Trait.Maid) || c.Condition.Bond >= Bond.Fond;

    /// <summary>找人欲增速加成（好奇）。</summary>
    public static int ChatDesireBonus(CharacterState c) =>
        c.Has(Trait.Curious) ? 1 : 0;

    /// <summary>起床钟点（懒散晚起）。</summary>
    public static int WakeHourFor(CharacterState c) =>
        c.Has(Trait.Lazy) ? TerritoryClock.WakeHourLazy : TerritoryClock.WakeHourDefault;

    /// <summary>就寝钟点（好奇晚睡）。</summary>
    public static int BedHourFor(CharacterState c) =>
        (c.Has(Trait.NightOwl) || c.Has(Trait.Curious)) ? TerritoryClock.BedHourCurious : TerritoryClock.BedHourDefault;

    /// <summary>闲时活动持续格数（懒散久、好奇短）。</summary>
    public static int LoiterTicksFor(CharacterState c) =>
        c.Has(Trait.Lazy) ? 18 : c.Has(Trait.Curious) ? 6 : 12;

    /// <summary>闲坐权重修正（懒散爱坐着，好奇坐不住）。</summary>
    public static int SitWeightDelta(CharacterState c)
    {
        var d = 0;
        if (c.Has(Trait.Lazy)) d += 25;
        if (c.Has(Trait.Curious)) d -= 10;
        return d;
    }

    /// <summary>串门倾向（百分比）：好奇爱走动，冷淡/懒散恋家。</summary>
    public static int WanderChance(CharacterState c)
    {
        var chance = 45;
        if (c.Has(Trait.Curious)) chance += 30;
        if (c.Has(Trait.Cold) || c.Has(Trait.Arrogant) || c.Has(Trait.Prideful)) chance -= 15;
        if (c.Has(Trait.Lazy)) chance -= 25;
        return System.Math.Clamp(chance, 5, 90);
    }
    // ---------- 界面：特质的具体影响 ----------

    /// <summary>
    /// 某特质对数值的具体影响，逐行给界面列示（无冒号，标签与数值空格分隔）。
    /// 不另写一份数值表：拿「只有这条特质」与「什么特质都没有」的两个探针角色，
    /// 逐个调用本文件的效果方法求差，所以改了上面的数值，这里自动跟着变。
    /// 条件性效果用三种情境区分：陌生且心情平常 / 陌生且心情好 / 熟络且心情平常。
    /// </summary>
    public static IReadOnlyList<string> EffectLines(Trait trait)
    {
        var lines = new List<string>();
        CharacterState Probe(bool with, bool fond, bool happy)
        {
            var c = new CharacterState(-1);
            if (with)
                c.Talents.Add((int)trait);
            if (fond)
                c.Condition.AddFavor(Vitals.FondAt + 50);
            c.Affect.Mood = happy ? 80 : Affect.Neutral;
            return c;
        }

        // 三情境求差；返回 (修饰前缀, 差值)，差值为 0 表示无影响。
        (string Prefix, int Delta) Diff(System.Func<CharacterState, int> f)
        {
            var d0 = f(Probe(true, false, false)) - f(Probe(false, false, false));
            var dm = f(Probe(true, false, true)) - f(Probe(false, false, true));
            var df = f(Probe(true, true, false)) - f(Probe(false, true, false));
            if (d0 == dm && d0 == df)
                return ("", d0);
            if (d0 == dm && df == 0)
                return ("陌生时", d0);
            if (d0 == df && d0 == 0)
                return ("心情好时", dm);
            return ("", d0 != 0 ? d0 : dm != 0 ? dm : df);
        }

        static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString();

        void Add(string label, System.Func<CharacterState, int> f, string unit = "%")
        {
            var (prefix, d) = Diff(f);
            if (d != 0)
                lines.Add($"{prefix}{label} {Signed(d)}{unit}");
        }

        // 工作效率：白天按工种分组；全部工种同幅度则合成一行。夜间、清晨另算相对白天的差。
        var works = ActionKindMap.WorkOrdered;
        var day = works.Select(a => (Action: a, Delta: Diff(c => WorkProgressPercent(c, a, 12)).Delta)).ToList();
        foreach (var group in day.GroupBy(x => x.Delta).Where(g => g.Key != 0))
        {
            var label = group.Count() == works.Length ? "工作效率"
                : string.Join("、", group.Select(x => ActionKindMap.LabelOf(x.Action))) + "效率";
            lines.Add($"{label} {Signed(group.Key)}%");
        }
        var probeWork = works.FirstOrDefault(a => day.First(x => x.Action == a).Delta == day.GroupBy(x => x.Delta)
            .OrderByDescending(g => g.Count()).First().Key);
        var baseDay = day.First(x => x.Action == probeWork).Delta;
        var night = Diff(c => WorkProgressPercent(c, probeWork, 23)).Delta - baseDay;
        if (night != 0)
            lines.Add($"夜间工作效率 {Signed(night)}%");
        var morning = Diff(c => WorkProgressPercent(c, probeWork, 7)).Delta - baseDay;
        if (morning != 0)
            lines.Add($"清晨工作效率 {Signed(morning)}%");
        if (!WillWork(Probe(true, false, false), true) && WillWork(Probe(false, false, false), true))
            lines.Add("不肯干重活");

        Add("心情收益", MoodGainPercent);
        Add("亲密接触门槛", TouchGatePercent);

        // 四类社交好感：同幅度合成一行。
        var social = new[] { (SocialKind.Talk, "交谈"), (SocialKind.Gift, "送礼"), (SocialKind.Care, "关怀"), (SocialKind.Intimate, "亲密") }
            .Select(k => (k.Item2, Diff(c => SocialFavorPercent(c, k.Item1)))).Where(x => x.Item2.Delta != 0).ToList();
        if (social.Count == 4 && social.All(x => x.Item2 == social[0].Item2))
            lines.Add($"{social[0].Item2.Prefix}社交好感 {Signed(social[0].Item2.Delta)}%");
        else
            foreach (var (label, (prefix, delta)) in social)
                lines.Add($"{prefix}{label}好感 {Signed(delta)}%");

        Add("进餐体力恢复", c => MealStamina(c, 0), "");
        Add("进餐精神恢复", c => MealSpirit(c, 0), "");
        Add("进餐心情", MealMoodPercent);
        Add("休息精神恢复", RestSpiritBonus, "");
        Add("学习速度", LearnPercentTotal);
        Add("交谈难度", TalkDifficultyTotal, "");
        Add("主动搭话的耐心", SeekBudgetDelta, "");
        Add("找人聊天的欲望", ChatDesireBonus, "");
        Add("闲坐倾向", SitWeightDelta, "");
        Add("串门倾向", WanderChance);

        var loiter = Diff(LoiterTicksFor).Delta;
        if (loiter != 0)
            lines.Add($"闲时活动时长 {Signed(loiter * 100 / LoiterTicksFor(Probe(false, false, false)))}%");
        var wake = Diff(WakeHourFor).Delta;
        if (wake != 0)
            lines.Add($"起床推迟到 {WakeHourFor(Probe(true, false, false))}时");
        var bed = Diff(BedHourFor).Delta;
        if (bed != 0)
            lines.Add($"就寝改到 {BedHourFor(Probe(true, false, false))}时");

        if (!RequiresWage(Probe(true, false, false)))
            lines.Add("不要工钱");
        if (AcceptsInvite(Probe(true, false, false)) && !AcceptsInvite(Probe(false, false, false)))
            lines.Add("不看好感，邀请必应");
        if (trait == Trait.QuickChant)
            lines.Add("战斗咏唱少一回合");
        return lines;
    }
}
