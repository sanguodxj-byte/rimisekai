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

    /// <summary>四十条特质。顺序与 Trait 枚举的性格段一一对应。</summary>
    public static readonly Def[] All =
    {
        // 外向组
        new(Trait.Talkative, "健谈", "外向", "主动开场，话密", 2, false),
        new(Trait.Quiet, "寡言", "外向", "答比问短", 0, false),
        new(Trait.SlowWarmer, "慢热", "外向", "初见少语，熟后放开", 0, false),
        new(Trait.SocialChameleon, "场面应答", "外向", "对外套话、对大人一律", 1, false),
        // 情绪组
        new(Trait.Nervous, "易紧张", "情绪", "结巴、改口、自问自答", 1, false),
        new(Trait.Expressive, "情绪外露", "情绪", "喜怒都在语气里", 2, true),
        new(Trait.StillWaters, "静水深流", "情绪", "感情只走括号动作", 0, true),
        new(Trait.Optimist, "乐观", "情绪", "往好处说，爱打气", 1, false),
        new(Trait.Melancholy, "忧郁", "情绪", "凡事先想难处", 0, false),
        new(Trait.HotTempered, "脾气急", "情绪", "抢话，短句连发", 2, false),
        new(Trait.Unflappable, "泰然", "情绪", "天塌下来语速不变", 0, false),
        // 待人组
        new(Trait.Attentive, "体贴入微", "待人", "记偏好，先一步", 0, true),
        new(Trait.SharpTongued, "毒舌", "待人", "夸奖藏在损话里", 1, false),
        new(Trait.Worrier, "爱操心", "待人", "唠叨式叮嘱连发", 1, true),
        new(Trait.PeoplePleaser, "讨好", "待人", "先看脸色，道歉是口头禅", 0, true),
        new(Trait.Arrogant, "傲慢", "待人", "评价式口吻，夸奖稀缺", 0, false),
        new(Trait.Forthright, "直率", "待人", "有话直说，不会绕", 1, false),
        new(Trait.Protective, "护短", "待人", "对外强硬对内柔软", 1, false),
        // 做事组
        new(Trait.Dutiful, "尽责", "做事", "报进度，守诺语汇", 0, false),
        new(Trait.Perfectionist, "完美主义", "做事", "自我修正常挂嘴边", 0, false),
        new(Trait.Procrastinator, "拖沓", "做事", "自嘲式『我老是这样』", 0, false),
        new(Trait.Methodical, "有条理", "做事", "凡事先列一二三", 0, false),
        new(Trait.FreeSpirit, "随性", "做事", "话随情绪断", 1, false),
        // 认知组
        new(Trait.Inquisitive, "好学", "认知", "新词多，爱问为什么", 1, false),
        new(Trait.Bookish, "书卷气", "认知", "书面语混进口语", 0, false),
        new(Trait.DownToEarth, "务实", "认知", "抽象话题拉回现实", 0, false),
        new(Trait.Dreamer, "爱幻想", "认知", "『如果……的话』高频", 1, true),
        new(Trait.Meticulous, "较真", "认知", "对数字与事实纠错", 1, false),
        new(Trait.Superstitious, "迷信", "认知", "吉凶宜忌挂嘴边", 1, false),
        // 独特组
        new(Trait.Foodie, "吃货", "独特", "话题绕到吃的", 1, false),
        new(Trait.NightFearful, "怕鬼", "独特", "夜间自保句", 1, false),
        new(Trait.GrouchyMornings, "起床气", "独特", "清晨语料变体", 1, false),
        new(Trait.Frugal, "守财", "独特", "价格敏感，心疼物件", 1, false),
        new(Trait.Generous, "大方", "独特", "出手阔绰，爱分享", 1, false),
        new(Trait.DeftHands, "手巧", "独特", "以物传情", 0, false),
        new(Trait.Directionless, "路痴", "独特", "外出认不得路", 1, false),
        new(Trait.NeatFreak, "爱干净", "独特", "环境评价高频", 1, false),
        new(Trait.Sentimental, "旧物情结", "独特", "念旧，舍不得扔", 0, false),
        new(Trait.NightOwl, "夜猫子", "独特", "深夜精神，白天蔫", 0, false),
        new(Trait.VerbalTic, "口癖尾", "独特", "自造句尾", 0, false),
        // 原机制素质折入光谱（2026-09-29 全都进光谱）
        new(Trait.Lazy, "懒散", "做事", "能躺着绝不坐着", 0, false),
        new(Trait.Artisan, "工匠", "做事", "三句话不离手艺", 1, false),
        new(Trait.Curious, "好奇", "认知", "这也要看、那也要摸", 1, false),
        new(Trait.Cold, "冷淡", "待人", "话少热度低", 0, false),
        new(Trait.Defiant, "桀骜", "待人", "不服管，爱顶嘴", 1, false),
        new(Trait.FastLearner, "聪慧", "认知", "一点就透", 0, false),
        new(Trait.SlowLearner, "迟钝", "认知", "学得慢但踏实", 0, false),
        new(Trait.FearPain, "怕痛", "独特", "疼得直咧嘴", 1, false),
        new(Trait.IgnorePain, "不觉痛", "独特", "感觉不到疼", 0, false),
        new(Trait.QuickRecovery, "恢复快", "独特", "睡一觉就满血", 0, false),
        new(Trait.SlowRecovery, "恢复慢", "独特", "累一回缓好几天", 0, false),
        new(Trait.Alchemist, "炼金", "独特", "配方张口就来", 1, false),
        // 身份标识（不入池，随身份授予）与已并入别名（保留枚举以兼容旧存档）
        new(Trait.Maid, "女仆", "身份", "免工资；无视好感接受邀请", 0, false),
        new(Trait.Mage, "魔法师", "身份", "魔法师身份标识（法术位）", 0, false),
        new(Trait.Timid, "胆小", "已并入", "并入易紧张", 0, false),
        new(Trait.Honest, "诚实", "已并入", "并入直率", 0, false),
        new(Trait.Prideful, "自负", "已并入", "并入傲慢", 0, false),
    };

    private static readonly Dictionary<Trait, Def> ByTrait = All.ToDictionary(d => d.Trait);

    public static Def? Of(Trait trait) => ByTrait.GetValueOrDefault(trait);

    /// <summary>互斥对（特质名）。同选视为性格自相矛盾，生成器据此排重。</summary>
    public static readonly (string A, string B)[] Exclusions =
    {
        ("健谈", "寡言"), ("情绪外露", "静水深流"), ("乐观", "忧郁"),
        ("讨好", "傲慢"), ("尽责", "拖沓"), ("完美主义", "随性"), ("务实", "爱幻想"),
        ("懒散", "尽责"), ("迟钝", "聪慧"), ("怕痛", "不觉痛"), ("恢复快", "恢复慢"),
    };

    private static readonly string[] NonPoolGroups = { "身份", "已并入" };

    /// <summary>生成器的抽取池：光谱六组 52 条（排除身份标识与已并入别名）。组合空间 C(52,2..4) 约 29 万。</summary>
    public static readonly Def[] Pool = All.Where(d => !NonPoolGroups.Contains(d.Group)).ToArray();

    /// <summary>全量目录：52 条光谱特质 + 2 身份标识 + 3 已并入别名。</summary>
    public static readonly Def[] Catalog = All;

    // ---------- 游戏性效果 ----------

    /// <summary>工作进度百分比（100 为基准）。hour 用于作息类特质的时段修正。</summary>
    public static int WorkProgressPercent(CharacterState c, ActionKind action, int hour)
    {
        var p = 100;
        if (c.Has(Trait.Dutiful)) p += 10;
        if (c.Has(Trait.Perfectionist)) p += 5;
        if (c.Has(Trait.Procrastinator)) p -= 15;
        if (c.Has(Trait.Methodical)) p += 5;
        if (c.Has(Trait.FreeSpirit)) p -= 5;
        if (c.Has(Trait.HotTempered)) p += 10;
        if (c.Has(Trait.Dreamer)) p -= 5;
        if (c.Has(Trait.Directionless)) p -= 5;
        if (c.Has(Trait.Defiant)) p -= 5;
        if ((c.Has(Trait.Nervous) || c.Has(Trait.Timid)) && action is ActionKind.Tinker or ActionKind.Sew) p -= 5;
        if (c.Has(Trait.Artisan) && action is ActionKind.Woodwork or ActionKind.Sew or ActionKind.Forge) p += 10;
        if (c.Has(Trait.Alchemist) && action == ActionKind.Brew) p += 15;

        // 认知/手艺向的任务对口加成
        if (c.Has(Trait.DownToEarth) && action is ActionKind.Till or ActionKind.Mine or ActionKind.Fell) p += 10;
        if (c.Has(Trait.Bookish) && action == ActionKind.Brew) p += 15;
        if (c.Has(Trait.Meticulous) && action is ActionKind.Tinker or ActionKind.Sew) p += 10;
        if (c.Has(Trait.DeftHands) && action is ActionKind.Tinker or ActionKind.Sew or ActionKind.Woodwork) p += 15;
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

    /// <summary>疲劳积累百分比（100 为基准）。</summary>
    public static int FatiguePercent(CharacterState c)
    {
        var p = 100;
        if (c.Has(Trait.Dutiful)) p += 10;
        if (c.Has(Trait.Perfectionist)) p += 25;
        if (c.Has(Trait.HotTempered)) p += 20;
        if (c.Has(Trait.Unflappable)) p -= 20;
        if (c.Has(Trait.StillWaters)) p -= 10;
        if (c.Has(Trait.Optimist)) p -= 10;
        if (c.Has(Trait.IgnorePain)) p -= 15;
        return System.Math.Clamp(p, 20, 200);
    }

    /// <summary>疲劳值换算。调用方把原始疲劳增量传进来。</summary>
    public static int ScaledFatigue(CharacterState c, int fatigue) =>
        System.Math.Max(0, fatigue * FatiguePercent(c) / 100);

    /// <summary>心情增益百分比（100 为基准）。只用于正数心情。</summary>
    public static int MoodGainPercent(CharacterState c)
    {
        var p = 100;
        if (c.Has(Trait.Expressive)) p += 20;
        if (c.Has(Trait.Optimist)) p += 10;
        if (c.Has(Trait.Forthright) || c.Has(Trait.Honest)) p += 10;
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
                if (c.Has(Trait.Generous)) p += 10;
                if (c.Has(Trait.VerbalTic)) p += 5;
                if (c.Has(Trait.PeoplePleaser)) p += 20;
                break;
            case SocialKind.Gift:
                if (c.Has(Trait.PeoplePleaser)) p += 20;
                if (c.Has(Trait.Frugal)) p += 20;
                if (c.Has(Trait.SocialChameleon)) p += 15;
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

    /// <summary>进餐精神恢复加成。</summary>
    public static int MealSpirit(CharacterState c, int spirit) =>
        c.Has(Trait.Worrier) ? spirit + 10 : spirit;

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

    /// <summary>节拍疲劳恢复增量（快/慢恢复）。</summary>
    public static int FatigueRecoveryDelta(CharacterState c)
    {
        if (c.Has(Trait.QuickRecovery)) return -1;
        if (c.Has(Trait.SlowRecovery) && c.Condition.Fatigue < Vitals.TiredAt) return 1;
        return 0;
    }

    /// <summary>找人欲增速加成（好奇）。</summary>
    public static int ChatDesireBonus(CharacterState c) =>
        c.Has(Trait.Curious) ? 1 : 0;

    /// <summary>起床钟点（懒散晚起）。</summary>
    public static int WakeHourFor(CharacterState c) =>
        c.Has(Trait.Lazy) ? TerritoryClock.WakeHourLazy : TerritoryClock.WakeHourDefault;

    /// <summary>就寝钟点（好奇晚睡）。</summary>
    public static int BedHourFor(CharacterState c) =>
        c.Has(Trait.Curious) ? TerritoryClock.BedHourCurious : TerritoryClock.BedHourDefault;

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
        if (c.Has(Trait.Cold) || c.Has(Trait.Prideful)) chance -= 15;
        if (c.Has(Trait.Lazy)) chance -= 25;
        return System.Math.Clamp(chance, 5, 90);
    }
}
