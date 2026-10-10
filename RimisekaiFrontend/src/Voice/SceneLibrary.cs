using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Voice;

/// <summary>
/// 一个场景事件。相当于 eraFL 的日常事件（DAILY），但整条做成数据：
/// 概率、额外判定、状态机、多步文本、分支选项、效果全写在内容包里，不需要写代码。
///
/// 与单句台词的区别：台词是"说一句就走"，场景事件是"演一段"——
/// 有推进、有分支、会改变角色状态、跑过之后留下状态机痕迹。
/// </summary>
public sealed class SceneEvent
{
    public string Id { get; init; } = "";

    /// <summary>事件标题，给事件回顾界面用。</summary>
    public string Title { get; init; } = "";

    /// <summary>分类，供配置界面按类过滤与开关。</summary>
    public string Genre { get; init; } = "";

    /// <summary>这段剧情属于哪些场景（领地/野外/聚落/地城），只在这些场景开演，禁止跨越；读表时必填。</summary>
    public List<VoiceSetting> Settings { get; init; } = new();

    /// <summary>
    /// 基础发生率，千分率（1000 = 每次检查必发，100 = 10%）。
    /// 与 eraFL 的 _RATE 同一口径，便于照搬既有数值。
    /// </summary>
    public int Rate { get; init; } = 1000;

    /// <summary>
    /// 参与角色。留空表示任何角色都可能触发（由宿主决定检查谁）。
    /// 填了名字就只有这些角色会触发。
    /// </summary>
    public List<string> Characters { get; init; } = new();

    /// <summary>触发与否的额外判定。不满足则本次不触发。</summary>
    public VoiceGate? Gate { get; init; }

    /// <summary>
    /// 状态机。填了 <see cref="Flag"/> 后，事件跑完会把该标志推到 <see cref="DoneValue"/>；
    /// <see cref="RequireFlagValue"/> 不为空时，只在标志等于该值时才允许触发。
    /// 这样"只在初见时发生""第二次见面才发生"都能表达。
    /// </summary>
    public string Flag { get; init; } = "";

    /// <summary>触发要求标志等于此值。为 null 表示不检查。</summary>
    public int? RequireFlagValue { get; init; }

    /// <summary>跑完后把标志设为此值。</summary>
    public int DoneValue { get; init; } = 1;

    /// <summary>
    /// 同一事件两次触发之间至少隔几天。0 表示不限。
    /// 与 <see cref="Flag"/> 互补：一次性用 Flag，可重复但有间隔用这个。
    /// </summary>
    public int CooldownDays { get; init; }

    /// <summary>
    /// 到点时现掷一名新角色当演员（访客这类"人从外面来"的事件）。
    /// 掷出的角色不在名册里，内容表只需写占位说话人与 <c>{名}</c>，
    /// 真名与身份由生成器给出。false 表示演员取自在场角色。
    ///
    /// Spawn 场景的 <see cref="Characters"/> 是**占位说话人**（正文里那个写法），
    /// 不是名册里真有的人。
    /// </summary>
    public bool Spawn { get; init; }

    /// <summary>
    /// 选了这一项就把现掷的演员送走（移出名册）。留空表示怎么选都留人。
    /// 只对 <see cref="Spawn"/> 场景有意义——去留写在内容表里，代码不认识"送客"这个词。
    /// </summary>
    public string DismissChoice { get; init; } = "";

    /// <summary>事件步骤。按顺序执行。</summary>
    public List<SceneStep> Steps { get; init; } = new();

    /// <summary>事件开跑前施加的效果。</summary>
    public List<SceneEffect> Effects { get; init; } = new();

    /// <summary>
    /// 事件菜单里的概要，最多几行。供回顾界面显示"发生了什么"。
    /// </summary>
    public List<string> Summary { get; init; } = new();
}

/// <summary>
/// 场景事件表。按角色名索引，外加一批任何角色都能触发的事件。
/// </summary>
public sealed class SceneLibrary
{
    private readonly List<SceneEvent> _events = new();

    public IReadOnlyList<SceneEvent> Events => _events;

    public int Count => _events.Count;

    public void Register(SceneEvent scene)
    {
        if (scene.Id.Length == 0 || scene.Steps.Count == 0)
            return;
        _events.RemoveAll(e => e.Id == scene.Id);
        _events.Add(scene);
    }

    public void RegisterRange(IEnumerable<SceneEvent> scenes)
    {
        foreach (var scene in scenes)
            Register(scene);
    }

    public SceneEvent? Find(string id)
    {
        foreach (var scene in _events)
        {
            if (scene.Id == id)
                return scene;
        }
        return null;
    }

    /// <summary>
    /// 这个角色此刻能不能触发这个事件。
    /// 判定顺序：角色匹配 → 门槛 → 状态机 → 冷却 → 掷骰。
    /// 掷骰放最后，免得前面都不过还白掷一次消耗随机数。
    /// </summary>
    public bool CanTrigger(SceneEvent scene, CharacterState character, VoiceContext ctx)
    {
        // 所有场景演出全存档终身仅演一次：已演过绝不再触发。
        if (ctx.FiredEvents.Contains(scene.Id))
            return false;

        if (!scene.Settings.Contains(ctx.Setting))
            return false;

        if (scene.Characters.Count > 0 && !scene.Characters.Contains(character.Name))
            return false;

        if (scene.Gate != null && !scene.Gate.Allows(ctx, scene.Id))
            return false;

        if (scene.RequireFlagValue.HasValue)
        {
            if (character.Get(character.Flags, FlagKey(scene.Flag)) != scene.RequireFlagValue.Value)
                return false;
        }

        if (scene.CooldownDays > 0)
        {
            if (character.Voice.SceneLastDay.TryGetValue(scene.Id, out var lastDay)
                && ctx.Day - lastDay < scene.CooldownDays)
            {
                return false;
            }
        }

        if (scene.Rate < 1000 && ctx.Rng.Next(1000) >= scene.Rate)
            return false;

        return true;
    }

    /// <summary>
    /// 从该角色可触发的事件里随机挑一个。挑不出返回 null。
    /// </summary>
    public SceneEvent? Pick(CharacterState character, VoiceContext ctx)
    {
        var pool = new List<SceneEvent>();
        foreach (var scene in _events)
        {
            if (CanTrigger(scene, character, ctx))
                pool.Add(scene);
        }
        if (pool.Count == 0)
            return null;
        return pool[ctx.Rng.Next(pool.Count)];
    }

    /// <summary>标志位在角色 Flags 里的键。用稳定哈希，避免名字变来变去。</summary>
    public static int FlagKey(string flag)
    {
        if (flag.Length == 0)
            return 0;
        var hash = 17;
        foreach (var ch in flag)
            hash = hash * 31 + ch;
        // 挪到负数区，避免和既有数值型旗标（0/1 之类）撞车。
        return hash | int.MinValue;
    }
}
