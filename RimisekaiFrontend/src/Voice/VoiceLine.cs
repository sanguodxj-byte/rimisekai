using System.Collections.Generic;

namespace Rimisekai.Voice;

/// <summary>
/// 一句口上/地文。Id 是它在本角色台词库里的唯一名字，
/// 供 RequireSaid / ForbidSaid 引用，也是“说过一次”的记账键。
/// </summary>
public sealed class VoiceLine
{
    public string Id { get; init; } = "";

    /// <summary>说话人。留空即通用台词，任何角色都能说（世界旁白用空）。</summary>
    public string Speaker { get; init; } = "";

    public VoiceKind Kind { get; init; } = VoiceKind.Speech;
    public VoiceTrigger Trigger { get; init; }

    /// <summary>正文。地文可以有多行，按顺序推进。</summary>
    public List<string> Lines { get; init; } = new();

    /// <summary>
    /// 出现权重，越大越容易被选中。默认 1。
    /// 与 <see cref="Priority"/> 不同：Weight 是同级之间的相对频率，
    /// Priority 是跨级的准入门槛（高优先级的排他句会把低优先级全压掉）。
    /// </summary>
    public int Weight { get; init; } = 1;

    /// <summary>
    /// 优先级。默认 1。低于 1 的句子永不选中（可用作"关掉这句"）。
    /// 配合 <see cref="Exclusive"/> 使用：排他句会把它之下的候选全部压掉。
    /// </summary>
    public int Priority { get; init; } = 1;

    /// <summary>
    /// 排他。为真且本句通过门槛时，所有优先级更低的候选本轮出局。
    /// 用来表达"这个情境下必须说这句，别的都闭嘴"。
    /// </summary>
    public bool Exclusive { get; init; }

    public VoiceGate Gate { get; init; } = new();

    /// <summary>地文用的插画 Id，留空则纯文本。</summary>
    public string IllustrationId { get; init; } = "";

    // ---------- 场景维度 ----------

    /// <summary>本句属于哪些场景（领地/野外/聚落/地城）。只在这些场景出现，禁止跨越；读表时必填。</summary>
    public List<VoiceSetting> Settings { get; init; } = new();

    /// <summary>
    /// 本句只在说话人处于这些活动时出现。留空表示不限。
    /// 填了多项即"其中任一"。
    /// </summary>
    public List<VoiceActivity> Activities { get; init; } = new();

    /// <summary>本句只在说话人扮演这些角色时出现。留空表示不限。</summary>
    public List<VoiceRole> Roles { get; init; } = new();

    /// <summary>本句只在动作的这几个阶段出现。留空表示不限。</summary>
    public List<VoicePlace> Places { get; init; } = new();

    /// <summary>
    /// 本句要求的情绪。留空或 <see cref="VoiceEmotion.Any"/> 表示不限。
    /// </summary>
    public VoiceEmotion Emotion { get; init; } = VoiceEmotion.Any;

    /// <summary>
    /// 说完本句后是否切掉同动作的通用地文。对应 eraFL 的 cutDescription：
    /// 口上已经把场面描述清楚了，再叠一层旁白就重复了。
    /// </summary>
    public bool CutNarration { get; init; }

    /// <summary>
    /// 本句由 LLM 现场生成而不是取自台词库。
    /// 生成失败时整句作废（宿主走自己的默认文案），不会退回静态文本。
    /// 见 <see cref="VoiceGeneration"/>。
    /// </summary>
    public VoiceGeneration? Generation { get; init; }

    /// <summary>
    /// 本句自己声明的场景维度是否与当前情境相符。
    /// 与 <see cref="Gate"/> 里的同类字段是两处独立入口：写在本句上更直观，
    /// 写在 Gate 里便于整组复用。两边都会判，任一不过即不出现。
    /// </summary>
    public bool MatchesScene(VoiceContext ctx)
    {
        if (!Settings.Contains(ctx.Setting))
            return false;
        if (Activities.Count > 0 && !Activities.Contains(ctx.Activity))
            return false;
        if (Roles.Count > 0 && !Roles.Contains(ctx.Role))
            return false;
        if (Places.Count > 0 && !Places.Contains(ctx.Place))
            return false;
        if (Emotion != VoiceEmotion.Any && Emotion != ctx.Emotion)
            return false;
        return true;
    }
}
