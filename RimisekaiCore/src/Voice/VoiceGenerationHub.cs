using System.Collections.Generic;
using System.Text;
using Rimisekai.Character;

namespace Rimisekai.Voice;

/// <summary>
/// LLM 生成层的调度与缓存。夹在 <see cref="VoiceDirector"/> 和
/// <see cref="IVoiceGenerator"/> 之间，负责三件事：
/// 拼请求、查/写缓存、把结果记账。
///
/// 没有接入生成器时整层旁路：挂 Generation 的句子直接作废，
/// 宿主走自己的默认文案，游戏不受影响。
/// </summary>
public sealed class VoiceGenerationHub
{
    /// <summary>接入的生成器。为 null 即整层旁路。</summary>
    public IVoiceGenerator? Generator { get; set; }

    /// <summary>角色的固定设定，按名字索引。由内容包填。</summary>
    public Dictionary<string, string> Personas { get; } = new();

    /// <summary>本层是否可用。生成器缺失或自报不可用都为假。</summary>
    public bool Enabled => Generator != null && Generator.Available;

    /// <summary>生成失败或未接入时的回调，给宿主记日志用。</summary>
    public System.Action<string>? OnError { get; set; }

    /// <summary>累计生成次数与命中缓存的次数，供调参与成本观察。</summary>
    public int Generated { get; private set; }
    public int CacheHits { get; private set; }

    /// <summary>
    /// 取一句生成结果。命中缓存直接返回，否则调生成器。
    /// 生成器返回空则返回 null（调用方应放弃该句）。
    /// </summary>
    public async System.Threading.Tasks.Task<IReadOnlyList<string>?> ResolveAsync(
        VoiceLine line, VoiceContext ctx)
    {
        var spec = line.Generation;
        if (spec == null)
            return null;
        if (!Enabled)
        {
            OnError?.Invoke("未接入 LLM 生成器，跳过生成句。");
            return null;
        }

        var character = ctx.Character;
        var key = CacheKey(line, ctx);

        if (spec.CacheMinutes > 0
            && character.Voice.GeneratedCache.TryGetValue(key, out var cached)
            && ctx.NowTotal - cached.AtTotal < spec.CacheMinutes)
        {
            CacheHits++;
            return cached.Lines;
        }

        var request = Build(line, ctx);
        var lines = await Generator!.GenerateAsync(request).ConfigureAwait(false);
        if (lines == null || lines.Count == 0)
        {
            OnError?.Invoke($"生成失败：{character.Name} 在 {line.Trigger} 的台词未产出。");
            return null;
        }

        Generated++;
        var trimmed = Trim(lines, spec.LineCount);
        if (spec.CacheMinutes > 0)
        {
            character.Voice.GeneratedCache[key] = new CachedLines
            {
                Lines = new List<string>(trimmed),
                AtTotal = ctx.NowTotal,
            };
        }
        return trimmed;
    }

    /// <summary>把一次生成请求拼好。世界状态与记忆由引擎自动填。</summary>
    public VoiceRequest Build(VoiceLine line, VoiceContext ctx) =>
        BuildFor(line.Generation!, line.Trigger, line.Kind, ctx);

    /// <summary>
    /// 直接按一份生成规格拼请求。场景里的一行没有 <see cref="VoiceLine"/> 外壳，
    /// 但挂的生成规格是同一个类型，因此共用这一条装配路径。
    /// </summary>
    public VoiceRequest BuildFor(VoiceGeneration spec, VoiceTrigger trigger, VoiceKind kind,
        VoiceContext ctx)
    {
        var character = ctx.Character;
        Personas.TryGetValue(character.Name, out var persona);

        return new VoiceRequest
        {
            CharacterName = character.Name,
            Persona = persona ?? "",
            Instruction = spec.Instruction,
            Style = spec.Style,
            LineCount = spec.LineCount,
            MaxCharsPerLine = spec.MaxCharsPerLine,
            Situation = Situation(character, ctx),
            Memory = spec.UseMemory
                ? new List<string>(character.Voice.Memories)
                : System.Array.Empty<string>(),
            RecentDialogue = spec.UseRecentDialogue
                ? new List<string>(character.Voice.RecentDialogue)
                : System.Array.Empty<string>(),
            Options = spec.Options,
            Trigger = trigger,
            Kind = kind,
        };
    }

    /// <summary>
    /// 把世界与关系状态摊成一段可读文字。模型不必认识游戏内部字段名，
    /// 因此这里全部转成人话。
    /// </summary>
    public static string Situation(CharacterState character, VoiceContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("时间：第").Append(ctx.Day).Append("天 ").Append(ctx.Hour).Append("时");
        sb.Append("，").Append(SeasonName(ctx.Season)).Append('，').Append(WeatherName(ctx.Weather));
        sb.Append("。\n");

        sb.Append("对玩家的好感：").Append(character.Condition.Favor);
        sb.Append("（").Append(BondName(character.Condition.Bond)).Append("）");
        sb.Append("，心情：").Append(character.Affect.Mood);
        sb.Append("。\n");

        sb.Append("此刻的状态：").Append(ActivityName(ctx.Activity));
        if (ctx.Role == VoiceRole.Partner)
            sb.Append("（是这次互动的承受方）");
        else if (ctx.Role == VoiceRole.Observer)
            sb.Append("（只是旁观）");
        sb.Append("。\n");

        if (ctx.PlayerRoomId >= 0)
        {
            sb.Append(ctx.CharacterRoomId == ctx.PlayerRoomId
                ? "玩家就在旁边。\n"
                : "玩家不在这里。\n");
        }

        var traits = new List<string>();
        foreach (var trait in character.Talents)
        {
            if (System.Enum.IsDefined(typeof(Trait), trait))
                traits.Add(TraitName((Trait)trait));
        }
        if (traits.Count > 0)
            sb.Append("性格/素质：").Append(string.Join("、", traits)).Append("。\n");

        return sb.ToString();
    }

    /// <summary>
    /// 缓存键。同一角色、同一时机、同一场景维度共用一条缓存，
    /// 因此玩家连点同一个按钮不会反复烧 token。
    /// 好感与心情按百位归并，避免数值微动就换一条缓存。
    /// </summary>
    public static string CacheKey(VoiceLine line, VoiceContext ctx)
    {
        var c = ctx.Character;
        return $"{c.Id}:{line.Id}:{line.Trigger}:{ctx.Activity}:{ctx.Role}:{ctx.Place}"
             + $":{ctx.Emotion}:{ctx.Day / 1}:{c.Condition.Favor / 100}:{c.Affect.Mood / 20}";
    }

    private static List<string> Trim(IReadOnlyList<string> lines, int count)
    {
        var list = new List<string>();
        var want = count <= 0 ? lines.Count : count;
        for (var i = 0; i < lines.Count && list.Count < want; i++)
        {
            var text = lines[i]?.Trim() ?? "";
            if (text.Length > 0)
                list.Add(text);
        }
        return list;
    }

    private static string SeasonName(Clock.Season season) => season switch
    {
        Clock.Season.Spring => "春",
        Clock.Season.Summer => "夏",
        Clock.Season.Autumn => "秋",
        Clock.Season.Winter => "冬",
        _ => "?",
    };

    private static string WeatherName(Clock.Weather weather) => weather switch
    {
        Clock.Weather.Clear => "晴",
        Clock.Weather.Cloud => "多云",
        Clock.Weather.Rain => "雨",
        Clock.Weather.Snow => "雪",
        _ => "?",
    };

    private static string BondName(Bond bond) => bond switch
    {
        Bond.Hatred => "憎恨",
        Bond.Hostile => "敌意",
        Bond.Dislike => "反感",
        Bond.None => "普通",
        Bond.Fond => "有好感",
        Bond.Close => "亲近",
        Bond.Lover => "爱慕",
        _ => "?",
    };

    private static string ActivityName(VoiceActivity activity) => activity switch
    {
        VoiceActivity.Idle => "闲着",
        VoiceActivity.Moving => "在赶路",
        VoiceActivity.Working => "在干活",
        VoiceActivity.Cooking => "在做饭",
        VoiceActivity.Mining => "在采矿",
        VoiceActivity.Farming => "在种植",
        VoiceActivity.Crafting => "在做手工",
        VoiceActivity.Training => "在锻炼",
        VoiceActivity.Resting => "在休息",
        VoiceActivity.Eating => "在吃饭",
        VoiceActivity.Sleeping => "在睡觉",
        VoiceActivity.Seeking => "正要找玩家说话",
        VoiceActivity.Following => "跟着玩家同行",
        VoiceActivity.Playing => "在玩乐",
        _ => "?",
    };

    private static string TraitName(Trait trait) => trait switch
    {
        Trait.Timid => "胆小",
        Trait.Defiant => "倔强",
        Trait.Honest => "坦率",
        Trait.Prideful => "高傲",
        Trait.Lazy => "懒散",
        Trait.FastLearner => "学得快",
        Trait.SlowLearner => "学得慢",
        Trait.Cold => "冷淡",
        Trait.Curious => "好奇",
        Trait.FearPain => "怕痛",
        Trait.IgnorePain => "不怕痛",
        Trait.QuickRecovery => "恢复快",
        Trait.SlowRecovery => "恢复慢",
        Trait.Mage => "术士",
        Trait.Artisan => "工匠",
        Trait.Alchemist => "炼金",
        Trait.Maid => "女仆",
        _ => trait.ToString(),
    };
}
