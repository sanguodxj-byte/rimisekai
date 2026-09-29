using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Hub;

namespace Rimisekai.Voice;

/// <summary>挑出来的一句台词，宿主拿它去写日志或弹遮盖层。</summary>
public readonly record struct VoiceUtterance(
    CharacterState Character,
    VoiceLine Line,
    VoiceKind Kind,
    string Speaker,
    IReadOnlyList<string> Lines);

/// <summary>
/// 口上/地文的调度。所有挂点都走 Speak：
/// 挑一句 → 记账 → 交给宿主的回调呈现。
/// 挑不出就返回 false，宿主退回自己的默认文案，因此不写台词也能跑。
/// </summary>
public sealed class VoiceDirector
{
    private readonly Dictionary<string, VoicePack> _packs = new();

    /// <summary>台词表为空时整条链路自动旁路，宿主行为与接入前一致。</summary>
    public bool Enabled => _packs.Count > 0 || World.Count > 0;

    /// <summary>世界级通用台词（说话人留空的那种）。</summary>
    public VoicePack World { get; } = new();

    /// <summary>LLM 生成层。没接入时挂 Generation 的句子自动作废。</summary>
    public VoiceGenerationHub Generation { get; } = new();

    /// <summary>场景事件表。没有事件时场景链路旁路。</summary>
    public SceneLibrary Scenes { get; } = new();

    public void Register(string characterName, VoicePack pack)
    {
        if (characterName.Length == 0)
            return;
        _packs[characterName] = pack;
    }

    public VoicePack? PackOf(string characterName) =>
        characterName.Length > 0 && _packs.TryGetValue(characterName, out var pack) ? pack : null;

    public IReadOnlyCollection<string> RegisteredNames => _packs.Keys;

    /// <summary>某个时机该角色有没有可说的话。宿主据此决定要不要弹遮盖层。</summary>
    public bool Has(CharacterState character, VoiceTrigger trigger, VoiceContext ctx) =>
        Pick(character, trigger, ctx, allowGeneration: false) != null;

    /// <summary>
    /// 挑一句并记账。挑不出返回 null，且不改任何状态。
    /// 记账在挑选成功时立刻发生：同一步里重复触发不会把同一句一次性的台词说两遍。
    ///
    /// 只取静态台词。挂 Generation 而没有静态正文的句子会被跳过，
    /// 因为同步路径没法等模型；要那种句子请走 <see cref="SpeakAsync"/>。
    /// </summary>
    public VoiceUtterance? Speak(CharacterState character, VoiceTrigger trigger, VoiceContext ctx)
    {
        var line = Pick(character, trigger, ctx, allowGeneration: false);
        if (line == null)
            return null;
        return Utter(character, trigger, ctx, line, line.Lines);
    }

    /// <summary>
    /// 异步路径：允许挑到由 LLM 现场生成的句子。
    ///
    /// 挑中的句子若带 Generation 就走生成层；生成失败则整句作废返回 null，
    /// 宿主走默认文案。生成成功的正文会写进记忆与近期对话，
    /// 供后续生成接上话头。
    /// </summary>
    public async System.Threading.Tasks.Task<VoiceUtterance?> SpeakAsync(
        CharacterState character, VoiceTrigger trigger, VoiceContext ctx)
    {
        var line = Pick(character, trigger, ctx, allowGeneration: true);
        if (line == null)
            return null;

        if (line.Generation == null)
            return Utter(character, trigger, ctx, line, line.Lines);

        var produced = await Generation.ResolveAsync(line, ctx).ConfigureAwait(false);
        if (produced == null || produced.Count == 0)
            return null;

        return Utter(character, trigger, ctx, line, produced);
    }

    /// <summary>记账并组装成一句可呈现的话。</summary>
    private VoiceUtterance Utter(
        CharacterState character, VoiceTrigger trigger, VoiceContext ctx,
        VoiceLine line, IReadOnlyList<string> lines)
    {
        ctx.Memory.MarkSaid(line.Id, ctx.NowTotal);
        ctx.Memory.MarkSpoke(trigger, ctx.NowTotal);

        var speaker = line.Speaker.Length > 0 ? line.Speaker : character.Name;
        // 生成出来的内容也进记忆，否则模型每次都从零开始，接不上话头。
        foreach (var text in lines)
            ctx.Memory.AddDialogue(speaker, text);

        return new VoiceUtterance(character, line, line.Kind, speaker, lines);
    }

    /// <summary>
    /// 挑一句。allowGeneration 为假时排除"只有 Generation、没有静态正文"的句子，
    /// 保证同步路径永远拿得到正文。
    /// </summary>
    private VoiceLine? Pick(CharacterState character, VoiceTrigger trigger, VoiceContext ctx,
        bool allowGeneration)
    {
        if (!Enabled)
            return null;

        // 先问角色自己的台词库，再问世界通用库。角色专属优先，便于覆盖。
        var own = PackOf(character.Name)?.Select(character, trigger, ctx, allowGeneration);
        if (own != null)
            return own;
        return World.Select(character, trigger, ctx, allowGeneration);
    }

    /// <summary>把一句台词落成遮盖层。地文带插画 Id 时走插画层。</summary>
    public static MapOverlay ToOverlay(VoiceUtterance utterance)
    {
        if (utterance.Kind == VoiceKind.Narration && utterance.Line.IllustrationId.Length > 0)
            return MapOverlay.Illustration(utterance.Line.IllustrationId, utterance.Lines[0]);

        if (utterance.Kind == VoiceKind.Narration)
        {
            var rows = new List<OverlayLine>();
            foreach (var text in utterance.Lines)
                rows.Add(new OverlayLine("", text));
            return MapOverlay.Story(rows);
        }

        return MapOverlay.Dialogue(utterance.Speaker, utterance.Lines);
    }

    /// <summary>把一句台词压成一行日志。多行时按换行接起来，日志面板会自己折行。</summary>
    public static string ToLog(VoiceUtterance utterance)
    {
        if (utterance.Kind == VoiceKind.Narration)
            return string.Join(" ", utterance.Lines);
        if (utterance.Lines.Count == 1)
            return $"{utterance.Speaker}：{utterance.Lines[0]}";
        return $"{utterance.Speaker}：{utterance.Lines[0]}";
    }
}
