using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

/// <summary>
/// 定时事件的预生成。
///
/// 事件表里声明了 <see cref="HubEventTrigger.Scheduled"/> 的事件，在开局就被排进后台队列：
/// 该有的演员提前掷出来（暂存，不入名册），他每一行的台词交给生成器写。
/// 时间到了且该场内容全部就绪，才真正开演——**不在到点那一刻现生成**，
/// 因此演出不会卡在等待上。
///
/// 演员暂存的理由：人是"从外面来"的，没到点就不该出现在名册与队伍面板里，
/// 但生成台词又必须先知道他叫什么、什么身份，所以掷在前、入册在后。
///
/// 成品文本按**事件 Id** 存（不是场景 Id）——同一条场景可以被多条事件引用，
/// 各事件的演员不同，正文必须分开存。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>定时事件的暂存演员：事件 Id → 已掷好但还没登场的角色。</summary>
    private readonly Dictionary<string, CharacterState> _staged = new();

    /// <summary>后台生成队列。成品落在世界的成品表上，读档后已生成的行自动跳过。</summary>
    private GenerationQueue? _generation;

    private GenerationQueue Generation => _generation ??= new GenerationQueue(State.SceneTexts);

    /// <summary>
    /// 开局排班：把还没演过的定时事件各掷一名演员（需要的话）、
    /// 编译生成任务入队、登记演员人设。重复调用无害。
    /// </summary>
    public void InitializeScheduledEvents()
    {
        foreach (var def in Events.All)
        {
            if (def.Trigger != HubEventTrigger.Scheduled)
                continue;
            if (def.Once && State.FiredEvents.Contains(def.Id))
                continue;

            var scene = State.Voice.Scenes.Find(def.SceneId);
            if (scene == null)
                continue;

            // 演员：还没掷就掷一个；读档时已由快照放回，不重掷（重掷会换人）。
            if (scene.Spawn && !_staged.ContainsKey(def.Id))
                _staged[def.Id] = RollStagedActor();

            // 无论演员是新掷的还是读档放回的，都补排还没生成的行——
            // 否则存档落在生成途中时，剩下的行永远不会再排。
            CompileTasks(def, scene);
        }
    }

    /// <summary>掷一名暂存演员：全套履历照掷，但不入名册，Id 先预约。</summary>
    private CharacterState RollStagedActor()
    {
        var generated = new CharacterGenerator(Day.Rng).Roll(State.Roster);
        var actor = generated.State;
        // 台词库：场景里写的占位说话人与 {名} 由播放侧换成他本人；
        // 人设交给生成层，供后台按他的身份与特质写台词。
        State.Voice.Generation.Personas[actor.Name] = generated.Persona;
        return actor;
    }

    /// <summary>把一条定时事件涉及的每个生成槽编译成后台任务。</summary>
    private void CompileTasks(HubEventDef def, SceneEvent scene)
    {
        // 演员：现掷的取暂存表；不掷人的取名册里点名的第一个角色。
        CharacterState? actor;
        if (scene.Spawn)
        {
            if (!_staged.TryGetValue(def.Id, out actor))
                return;
        }
        else
        {
            actor = PickScheduledActor(scene);
            if (actor == null)
                return;
        }

        var ctx = CreateVoiceContext(actor, VoiceTrigger.Scene);
        for (var s = 0; s < scene.Steps.Count; s++)
        {
            var step = scene.Steps[s];
            for (var l = 0; l < step.Lines.Count; l++)
            {
                var line = step.Lines[l];
                if (!line.NeedsGeneration)
                    continue;
                if (State.SceneTexts.Has(def.Id, s, l))
                    continue;
                Generation.Enqueue(new GenerationTask
                {
                    Key = def.Id,
                    Step = s,
                    Line = l,
                    ActorName = actor.Name,
                    Request = State.Voice.Generation.BuildFor(line.Generation!, VoiceTrigger.Scene,
                        line.Kind, ctx),
                });
            }
        }
    }

    /// <summary>不掷人的定时事件：演员取自在场的指名角色。</summary>
    private CharacterState? PickScheduledActor(SceneEvent scene)
    {
        CharacterState? fallback = null;
        foreach (var name in scene.Characters)
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

    /// <summary>该事件的暂存演员（没有则 null）。</summary>
    public CharacterState? StagedActorOf(string eventId) =>
        _staged.TryGetValue(eventId, out var actor) ? actor : null;

    /// <summary>暂存演员快照（存档用）。</summary>
    public IReadOnlyDictionary<string, CharacterState> StagedActors => _staged;

    /// <summary>演员登场：入名册、就位，暂存表随之清掉。</summary>
    public void AdmitStagedActor(string eventId, CharacterState actor, string logLine)
    {
        State.Roster.Attach(actor);
        _staged.Remove(eventId);
        Place(actor.Id, ArrivalRoom());
        Write(logLine);
    }

    /// <summary>演员退回：开不了演时不能留一个没有去留决定的来客在名册里。</summary>
    public void ReturnStagedActor(string eventId, CharacterState actor)
    {
        _presence.Remove(actor.Id);
        Day.EndRoutineOf(actor.Id);
        State.Roster.Remove(actor.Id);
        _staged[eventId] = actor;
    }

    /// <summary>来客该站哪：玩家在公开房间就站玩家跟前，否则退回第一间开着的公开房间。</summary>
    private int ArrivalRoom()
    {
        var here = Room(PlayerRoomId);
        if (here != null && !State.Territory.IsLocked(here))
            return PlayerRoomId;
        foreach (var room in State.Territory.Rooms)
        {
            if (!room.Open || room.RegionId >= Territory.MaxTerritoryRegions)
                continue;
            if (!State.Territory.IsLocked(room))
                return room.Id;
        }
        return PlayerRoomId;
    }

    /// <summary>
    /// 后台泵取一个待办生成任务；没有则返回 null。
    /// 执行在宿主（前端），本方法只出队并标记在飞。
    /// </summary>
    public GenerationTask? TakeGenerationTask() => Generation.TakeNext();

    /// <summary>生成层此刻是否可用（接了生成器且它自报可用）。不可用就别取任务，免得空转。</summary>
    public bool GenerationAvailable => State.Voice.Generation.Enabled;

    /// <summary>接入的生成器。宿主泵拿它发请求。</summary>
    public Voice.IVoiceGenerator? VoiceGenerator => State.Voice.Generation.Generator;

    /// <summary>后台泵回报一次成功。</summary>
    public void CompleteGeneration(GenerationTask task, IReadOnlyList<string> lines) =>
        Generation.Complete(task, lines);

    /// <summary>后台泵回报一次失败。任务留在队列里等重排。</summary>
    public void FailGeneration(GenerationTask task, string error) =>
        Generation.Fail(task, error);

    /// <summary>把在飞任务交回队列（换会话/停泵时用），不记一次失败。</summary>
    public void ReleaseGeneration(GenerationTask task) => Generation.Release(task);

    /// <summary>某条事件的场景内容是否已全部就绪（供事件板与测试查询）。</summary>
    public bool SceneContentReady(SceneEvent scene, string key) => Generation.SceneReady(scene, key);

    /// <summary>排队中的后台生成任务数。</summary>
    public int PendingGenerationCount => Generation.Count;
}
