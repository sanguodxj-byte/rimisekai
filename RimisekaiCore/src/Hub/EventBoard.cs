using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

/// <summary>
/// 事件板：事件的登记、被动触发与排队演出。
/// 玩家的操作只管推进时间/改变条件；到点的条件由这里捕收，
/// 排进队列逐段演出——触发点不是玩家的直接点击。
///
/// 事件管"什么时候演、谁来演"，场景管"演什么"：事件指向场景 Id。
/// 定时事件（<see cref="HubEventTrigger.Scheduled"/>）还要求该场内容已由后台生成就绪，
/// 未就绪就不演（下一次推进再查），绝不阻塞、也不拿静态文本凑数。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>事件表（内容层持有，随台词包灌入）。空表则整条事件链路旁路。</summary>
    public EventLibrary Events => State.Voice.Events;

    /// <summary>待演出的队列。</summary>
    private readonly Queue<HubEventDef> _pendingEvents = new();

    /// <summary>事件的登记。重复 Id 后到覆盖先到。</summary>
    public void RegisterEvent(HubEventDef def) => Events.Register(def);

    /// <summary>该事件是否已经演过（Once 类）。</summary>
    public bool EventFired(string eventId) => State.FiredEvents.Contains(eventId);

    /// <summary>
    /// 事件巡检：把到点的事件排进演出队列。只在领地层巡检。
    /// </summary>
    public void CollectEvents(bool seasonChanged, bool weatherChanged, bool characterJoined)
    {
        if (Layer != MapLayer.Territory)
            return;

        var matched = new List<HubEventDef>();
        foreach (var def in Events.All)
        {
            if (def.Once && State.FiredEvents.Contains(def.Id))
                continue;
            if (_pendingEvents.Contains(def))
                continue;

            var hit = def.Trigger switch
            {
                HubEventTrigger.Season => seasonChanged,
                HubEventTrigger.Weather => weatherChanged,
                HubEventTrigger.Join => characterJoined,
                HubEventTrigger.PersonalStory or HubEventTrigger.WorldStory
                    => StoryGatePasses(def),
                HubEventTrigger.Scheduled => ScheduledReached(def),
                _ => false,
            };
            if (!hit)
                continue;

            matched.Add(def);
        }

        // 按 Priority 升序排队（小的先演，同级保序）
        matched.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        foreach (var def in matched)
            _pendingEvents.Enqueue(def);
    }

    /// <summary>
    /// 定时事件是否到点：日期与小时都走到位才算。
    /// 定时事件还要求该场内容已由后台生成就绪——没就绪就先不入队，
    /// 下一次推进再查。绝不阻塞，也不拿静态文本凑数。
    /// </summary>
    private bool ScheduledReached(HubEventDef def)
    {
        if (def.Day.HasValue && State.Clock.Day < def.Day.Value)
            return false;
        if (def.Hour.HasValue && State.Clock.Hour < def.Hour.Value)
            return false;
        var scene = State.Voice.Scenes.Find(def.SceneId);
        if (scene == null)
            return false;
        return SceneContentReady(scene, def.Id);
    }

    /// <summary>演出队列里还有多少段。</summary>
    public int PendingEventCount => _pendingEvents.Count;

    /// <summary>
    /// 演下一段：队列有货且当前没在演时，开演队首。
    /// 演出者不在场或场景缺失就跳过该段，看下一段。
    ///
    /// 现掷演员的事件（访客这类）：演员此刻才入册就位，开不了演则退回暂存表。
    /// </summary>
    public bool PlayNextEvent()
    {
        if (ScenePlaying || _pendingEvents.Count == 0)
            return false;

        var def = _pendingEvents.Dequeue();
        // 出队时再查一次终身去重：入队到演出之间可能已被别的路径演掉。
        if (def.Once && State.FiredEvents.Contains(def.Id))
            return PlayNextEvent();
        var scene = State.Voice.Scenes.Find(def.SceneId);
        if (scene == null)
            return PlayNextEvent();

        CharacterState? who;
        if (scene.Spawn)
        {
            who = StagedActorOf(def.Id);
            if (who == null)
                return PlayNextEvent();
        }
        else
        {
            who = PickActor(def);
            if (who == null)
                return PlayNextEvent();
        }

        // 现掷演员：登场（入名册 + 就位），演不成再退回。
        if (scene.Spawn)
            AdmitStagedActor(def.Id, who, $"{who.Name}登门求见。");

        if (PlaySceneEvent(scene, who, def.Id))
        {
            if (def.Once)
                State.FiredEvents.Add(def.Id);
            return true;
        }

        if (scene.Spawn)
            ReturnStagedActor(def.Id, who);
        return false;
    }

    /// <summary>按事件指名的角色挑演出者：优先在场；都不在场取第一个指名者。</summary>
    private CharacterState? PickActor(HubEventDef def)
    {
        CharacterState? fallback = null;
        foreach (var name in def.Characters)
        {
            var c = State.Roster.Members.Find(m => m.Name == name);
            if (c == null || c.IsMaster)
                continue;
            if (_presence.GetValueOrDefault(c.Id, -1) == PlayerRoomId)
                return c;
            fallback ??= c;
        }
        return fallback;
    }

    /// <summary>剧情门槛按演出角色评估；过不了的段不开。</summary>
    private bool StoryGatePasses(HubEventDef def)
    {
        var candidates = new List<CharacterState>();
        foreach (var name in def.Characters)
        {
            var c = State.Roster.Members.Find(m => m.Name == name);
            if (c != null)
                candidates.Add(c);
        }
        if (candidates.Count == 0)
            return false;

        foreach (var c in candidates)
        {
            if (def.Gate == null || def.Gate.Allows(SceneContextOf(c), def.Id))
                return true;
        }
        return false;
    }
}
