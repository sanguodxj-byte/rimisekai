using System.Collections.Generic;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

/// <summary>事件的触发时机。共同点：触发都不是玩家的直接点击。</summary>
public enum HubEventTrigger
{
    /// <summary>换季：进入新季节当天。</summary>
    Season = 0,

    /// <summary>天气变化：新一天的天气与昨日不同。</summary>
    Weather = 1,

    /// <summary>角色加入据点。</summary>
    Join = 2,

    /// <summary>个人剧情：围绕指定角色的故事。</summary>
    PersonalStory = 3,

    /// <summary>世界剧情：不绑定角色的故事。</summary>
    WorldStory = 4,

    /// <summary>
    /// 固定时间点：不早于声明的第几天 / 第几小时，到点且内容就绪即开演。
    /// 与其它触发不同——它不看状态变化，只看时钟走到没走到。
    /// </summary>
    Scheduled = 5,
}

/// <summary>
/// 据点事件：被动触发的一段剧情演出。
/// 玩家做别的操作时到点触发，演完才交还据点；
/// 演出内容复用场景库的 <see cref="SceneEvent"/>（行与分支），插画位共通。
///
/// 事件管"什么时候演、谁来演"，场景管"演什么"——事件只指向一个场景 Id。
/// 因此同一条场景可被多条事件引用，同一条事件换场景也只改一个字段。
/// 事件是内容行（台词包里的 events 数组），不是代码里写死的。
/// </summary>
public sealed class HubEventDef
{
    /// <summary>事件 Id，演出时也作去重键与 <c>FiredEvents</c> 的记名。</summary>
    public string Id { get; init; } = "";

    /// <summary>事件名，演出时作面板标题。</summary>
    public string Title { get; init; } = "";

    public HubEventTrigger Trigger { get; init; }

    /// <summary>演出哪段场景（场景库 Id）。</summary>
    public string SceneId { get; init; } = "";

    /// <summary>能演这段的角色名；空 = 任意在场角色。个人剧情在此指名。</summary>
    public List<string> Characters { get; init; } = new();

    /// <summary>触发门槛（时段/天气/好感等），按演出角色评估。</summary>
    public VoiceGate? Gate { get; init; }

    /// <summary>整局只发生一次（剧情类）。冷却类事件不设。</summary>
    public bool Once { get; init; }

    /// <summary>同一天多事件同时到点时的先后，小的先演。</summary>
    public int Priority { get; init; }

    /// <summary>定时事件（<see cref="HubEventTrigger.Scheduled"/>）：不早于第几天。</summary>
    public int? Day { get; init; }

    /// <summary>定时事件：不早于当天的第几小时。</summary>
    public int? Hour { get; init; }
}

/// <summary>
/// 事件表。事件是内容行，随台词包一起灌进来（与场景表同一来源）。
/// 按 Id 索引，重复注册后到覆盖先到。
/// </summary>
public sealed class EventLibrary
{
    private readonly List<HubEventDef> _events = new();

    public IReadOnlyList<HubEventDef> All => _events;

    public int Count => _events.Count;

    /// <summary>注册一条事件。同 Id 覆盖旧的。</summary>
    public void Register(HubEventDef def)
    {
        if (def.Id.Length == 0)
            return;
        _events.RemoveAll(d => d.Id == def.Id);
        _events.Add(def);
    }

    public void RegisterRange(IEnumerable<HubEventDef> defs)
    {
        foreach (var def in defs)
            Register(def);
    }

    public HubEventDef? Find(string id)
    {
        foreach (var def in _events)
        {
            if (def.Id == id)
                return def;
        }
        return null;
    }
}
