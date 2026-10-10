using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Voice;

/// <summary>
/// 一个角色的全部台词。按 Id 索引，注册时同名覆盖，方便内容包分层覆盖默认文本。
/// </summary>
public sealed class VoicePack
{
    private readonly List<VoiceLine> _lines = new();
    private readonly Dictionary<string, VoiceLine> _byId = new();

    public IReadOnlyList<VoiceLine> Lines => _lines;

    public int Count => _lines.Count;

    public VoiceLine? Find(string lineId) =>
        lineId.Length > 0 && _byId.TryGetValue(lineId, out var line) ? line : null;

    /// <summary>注册一句。同 Id 覆盖旧的，便于内容包先铺默认再打补丁。</summary>
    public void Register(VoiceLine line)
    {
        if (line.Id.Length == 0)
            return;
        if (_byId.TryGetValue(line.Id, out var existing))
            _lines.Remove(existing);
        _byId[line.Id] = line;
        _lines.Add(line);
    }

    public void RegisterRange(IEnumerable<VoiceLine> lines)
    {
        foreach (var line in lines)
            Register(line);
    }

    /// <summary>
    /// 内容里的名字占位符。运行时才定名的角色（随机访客）用它把自己的名字写进台词与地文，
    /// 因为内容包写的时候还不知道这个人叫什么。
    /// </summary>
    public const string NameToken = "{名}";

    /// <summary>把文本里的名字占位符换成实际名字；没有占位符就原样返回。</summary>
    public static string Personalize(string text, string name) =>
        text.Length > 0 && text.Contains(NameToken) ? text.Replace(NameToken, name) : text;

    /// <summary>
    /// 复制一份台词库，把挂在本库说话人名下的句子改挂到 <paramref name="name"/> 上，
    /// 并把正文里的名字占位符换成 <paramref name="name"/>。
    /// 运行时才生成的角色（如随机访客）没有自己的内容条目，套用内容包里的模板台词时用这个：
    /// 模板里的正文、门槛、场景维度原样保留，只换说话人与名字。
    /// </summary>
    public VoicePack WithSpeaker(string name)
    {
        var copy = new VoicePack();
        foreach (var line in _lines)
        {
            var speaker = line.Speaker.Length == 0 ? name : line.Speaker;
            var lines = new List<string>(line.Lines.Count);
            foreach (var text in line.Lines)
                lines.Add(Personalize(text, name));
            copy.Register(new VoiceLine
            {
                Id = line.Id,
                Speaker = speaker,
                Kind = line.Kind,
                Trigger = line.Trigger,
                Lines = lines,
                Weight = line.Weight,
                Priority = line.Priority,
                Exclusive = line.Exclusive,
                Gate = line.Gate,
                IllustrationId = line.IllustrationId,
                Activities = line.Activities,
                Roles = line.Roles,
                Places = line.Places,
                Emotion = line.Emotion,
                CutNarration = line.CutNarration,
                Generation = line.Generation,
            });
        }
        return copy;
    }

    /// <summary>
    /// 按"谁、什么时机"挑一句能用的台词。挑不出返回 null，宿主应退回自己的默认文案。
    ///
    /// 顺序：先按说话人与时机筛，再逐句过门槛与掷骰，
    /// 然后跑排他（高优先级排他句压掉所有更低优先级），最后按权重随机取一。
    /// 掷骰用 ctx.Rng，因此同一份上下文可复现。
    ///
    /// allowGeneration 为假时排除"没有静态正文、只能靠 LLM 生成"的句子，
    /// 让同步调用方永远拿得到现成文本。
    /// </summary>
    public VoiceLine? Select(CharacterState character, VoiceTrigger trigger, VoiceContext ctx,
        bool allowGeneration = true, VoiceKind? kind = null, VoicePlace? place = null)
    {
        var candidates = new List<VoiceLine>();
        var weights = new List<int>();

        foreach (var line in _lines)
        {
            if (line.Trigger != trigger)
                continue;
            if (kind.HasValue && line.Kind != kind.Value)
                continue;
            if (place.HasValue && line.Places.Count > 0 && !line.Places.Contains(place.Value))
                continue;
            // 说话人留空即通用，任何角色都能用（世界旁白）。
            if (line.Speaker.Length > 0 && line.Speaker != character.Name)
                continue;
            // 没有静态正文的句子只能走异步生成路径。
            if (line.Lines.Count == 0 && (line.Generation == null || !allowGeneration))
                continue;
            // 优先级低于 1 视为关掉。
            if (line.Priority < 1)
                continue;
            // 本句自己声明的场景维度。
            if (!line.MatchesScene(ctx))
                continue;
            if (!line.Gate.Allows(ctx, line.Id))
                continue;
            if (line.Gate.Chance.HasValue && ctx.Rng.Next(100) >= line.Gate.Chance.Value)
                continue;

            candidates.Add(line);
            weights.Add(line.Weight < 1 ? 1 : line.Weight);
        }

        if (candidates.Count == 0)
            return null;

        // 排他：取通过门槛的排他句里的最高优先级，把低于它的候选全部淘汰。
        var threshold = 0;
        foreach (var line in candidates)
        {
            if (line.Exclusive && line.Priority > threshold)
                threshold = line.Priority;
        }
        if (threshold > 0)
        {
            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                if (candidates[i].Priority < threshold)
                {
                    candidates.RemoveAt(i);
                    weights.RemoveAt(i);
                }
            }
            if (candidates.Count == 0)
                return null;
        }

        if (candidates.Count == 1)
            return candidates[0];

        var total = 0;
        foreach (var weight in weights)
            total += weight;

        var roll = ctx.Rng.Next(total);
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return candidates[i];
        }
        return candidates[candidates.Count - 1];
    }
}
