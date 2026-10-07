using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

/// <summary>
/// 场景演出：插画铺满左上、右下锁成"继续 + 选项"，
/// 完整走完场景的每一步（含分支）才交还据点。
/// 触发由场景库按情境（时段/天气/好感/冷却）挑，演出本身只管播与锁。
/// </summary>
public sealed partial class HubSession
{
    public SceneRun? Scene { get; private set; }
    public CharacterState? SceneActor { get; private set; }
    public bool ScenePlaying => Scene is { Finished: false };

    /// <summary>现掷演员的 Id（访客这类"人从外面来"的事件）；-1 = 本场没有现掷演员。</summary>
    private int _spawnActorId = -1;

    /// <summary>玩家在这次演出里选了送客。收演时才真的把人送走。</summary>
    private bool _spawnDismissed;

    /// <summary>本场演出对应的事件 Id（成品文本按它取）；空 = 不走事件。</summary>
    private string _sceneEventId = "";

    /// <summary>
    /// 场景插画是否该盖住领地网格——闸门是一条链状判断，任何一环不成立就交还网格：
    /// 1. 领地层：世界层在赶路，兴趣点不可遮；
    /// 2. 开演了：有场景在跑；
    /// 3. 没收演：场景未结束；
    /// 4. 有演员：演出有着力点；
    /// 5. 演员仍在同房：人走了演出就不成立；
    /// 6. 当前步有内容：还有台词没放完，或分支正等选。
    /// 插画纹理是否在盘上由前端补最后一环。
    /// </summary>
    public bool SceneIllustrationCovers()
    {
        if (Layer != MapLayer.Territory)
            return false;
        if (Scene == null)
            return false;
        if (Scene.Finished)
            return false;
        if (SceneActor == null)
            return false;
        if (_presence.GetValueOrDefault(SceneActor.Id, -1) != PlayerRoomId)
            return false;
        if (SceneLines.Count == 0 && SceneChoices.Count == 0)
            return false;
        return true;
    }

    /// <summary>当前能否点继续——分支等待时只能先选。</summary>
    public bool SceneCanContinue() => Scene != null && !Scene.Finished && !Scene.Waiting;

    /// <summary>当前步骤要呈现的行（说话人 + 文本）。</summary>
    public IReadOnlyList<SceneText> SceneLines { get; private set; } =
        System.Array.Empty<SceneText>();

    /// <summary>当前等待的分支选项。为空表示这步没有分支，点继续即可。</summary>
    public IReadOnlyList<SceneChoice> SceneChoices { get; private set; } =
        System.Array.Empty<SceneChoice>();

    /// <summary>场景标题（事件名，如"夜里的对话"）。</summary>
    public string SceneTitle { get; private set; } = "";

    private SceneRunner? _sceneRunner;

    /// <summary>
    /// 开演：给一个角色开一段场景。挑不出（没内容/门槛不过/当日冷却中）
    /// 返回 false，据点照常。
    /// </summary>
    public bool PlayScene(CharacterState who)
    {
        if (ScenePlaying || who.IsMaster)
            return false;
        var run = BeginScene(who);
        if (run == null)
            return false;

        CloseOverlay();
        State.FiredEvents.Add(run.Event.Id);
        Scene = run;
        SceneActor = who;
        _sceneRunner = new SceneRunner(State.Voice.Scenes, State.Territory);
        PresentScene();
        return true;
    }

    /// <summary>
    /// 继续：本步骤的行没放完就翻一行；放完了就收本步、施加效果进下一步。
    /// 步骤挂着分支时不能继续——得先选，选完才往下走。
    /// </summary>
    public bool SceneContinue()
    {
        var run = Scene;
        if (run == null || run.Waiting)
            return false;

        if (run.NextLine())
        {
            PresentScene();
            return true;
        }

        // 本步行完：有分支就停下来等选；没有就收步进下一步。
        if (run.Current is { Choices.Count: > 0 })
        {
            run.Wait();
            PresentScene();
            // 门槛把选项全拦了：不能停在死局里，当作没有分支收步。
            if (SceneChoices.Count == 0)
            {
                var stepCtx = SceneContextOf(SceneActor!);
                if (!ResolveSceneRunner().Advance(run, stepCtx))
                    EndScene();
                else
                    PresentScene();
            }
            return true;
        }

        var ctx = SceneContextOf(SceneActor!);
        if (!ResolveSceneRunner().Advance(run, ctx))
            EndScene();
        else
            PresentScene();
        return true;
    }

    /// <summary>
    /// 指名演出一段场景：事件板触发时走这里，
    /// 场景由事件的 SceneId 指定，不按库挑选。
    /// <paramref name="eventId"/> 是事件 Id，用于取该事件下已生成的正文；
    /// 留空表示不走事件（按场景自身的静态文本演）。
    /// </summary>
    public bool PlaySceneEvent(SceneEvent scene, CharacterState who, string eventId = "")
    {
        if (ScenePlaying || who.IsMaster || scene == null)
            return false;
        var run = ResolveSceneRunner().BeginEvent(scene, who, SceneContextOf(who));
        if (run == null)
            return false;

        CloseOverlay();
        // 场景级与事件级都记一次：场景级挡住随机挑选器重演，事件级挡住事件板重排。
        State.FiredEvents.Add(scene.Id);
        if (eventId.Length > 0)
            State.FiredEvents.Add(eventId);
        Scene = run;
        SceneActor = who;
        _sceneEventId = eventId.Length > 0 ? eventId : scene.Id;
        // 现掷演员：内容表的占位说话人与 {名} 由本场演员顶上；收演时按选择决定去留。
        _spawnActorId = scene.Spawn ? who.Id : -1;
        _spawnDismissed = false;
        PresentScene();
        return true;
    }

    /// <summary>选分支：施加选项效果、按需跳步；跳出场了就收演。</summary>
    public bool SceneChoose(int index)
    {
        var run = Scene;
        if (run == null || !run.Waiting || index < 0 || index >= SceneChoices.Count)
            return false;

        var ctx = SceneContextOf(SceneActor!);
        var picked = SceneChoices[index];
        if (!ResolveSceneRunner().Choose(run, picked.Id, ctx))
            return false;

        // 现掷演员的去留：内容表点名的那一项被选中就记为"送走"，等收演时落账。
        if (_spawnActorId >= 0 && run.Event.DismissChoice.Length > 0
            && picked.Id == run.Event.DismissChoice)
            _spawnDismissed = true;

        if (!ScenePlaying)
        {
            EndScene();
            PlayNextEvent();
        }
        else
            PresentScene();
        return true;
    }

    private void EndScene()
    {
        SettleSpawnDecision();
        Scene = null;
        SceneActor = null;
        SceneLines = System.Array.Empty<SceneText>();
        SceneChoices = System.Array.Empty<SceneChoice>();
        SceneTitle = "";
        _sceneRunner = null;
        CloseOverlay();
    }

    /// <summary>现掷演员的去留落账：选了送客就把他移出名册，否则原样留下。</summary>
    private void SettleSpawnDecision()
    {
        var actorId = _spawnActorId;
        _spawnActorId = -1;
        if (actorId < 0 || !_spawnDismissed)
        {
            _spawnDismissed = false;
            return;
        }
        _spawnDismissed = false;

        var actor = State.Roster.Find(actorId);
        if (actor == null)
            return;
        _presence.Remove(actorId);
        Day.EndRoutineOf(actorId);
        State.Roster.Remove(actorId);
        Write($"{actor.Name}告辞了。");
    }

    private SceneRunner ResolveSceneRunner() =>
        _sceneRunner ??= new SceneRunner(State.Voice.Scenes, State.Territory);

    /// <summary>把当前步骤摊成演出状态：当前行、分支（门槛过滤后）与标题。</summary>
    private void PresentScene()
    {
        var run = Scene!;
        var who = SceneActor!;
        var ctx = SceneContextOf(who);
        SceneTitle = run.Event.Title;

        // 只呈现当前这一行：继续逐行推进，不把整步的字一次性倒出来。
        var lines = run.Lines();
        SceneLines = run.LineIndex < lines.Count
            ? new[] { ResolveLine(run, run.StepIndex, run.LineIndex, lines[run.LineIndex]) }
            : System.Array.Empty<SceneText>();

        // 分支只在等待时呈现；过不了门槛的选项直接不出现。
        var choices = new List<SceneChoice>();
        if (run.Waiting)
        {
            foreach (var choice in run.Choices)
            {
                if (choice.Gate == null || choice.Gate.Allows(ctx, choice.Id))
                    choices.Add(choice);
            }
        }
        SceneChoices = choices;
    }

    /// <summary>场景事件的上下文，与 BeginScene 同一口径。</summary>
    private VoiceContext SceneContextOf(CharacterState who) =>
        CreateVoiceContext(who, VoiceTrigger.Scene);

    /// <summary>
    /// 把演出文本落成实际要显示的一行：正文取生成成品（有的话），
    /// 占位说话人与 <c>{名}</c> 换成本场演员。
    /// 现掷演员的场景，内容表只能写占位说话人（如"访客"）与 <c>{名}</c>，
    /// 真正说话的人由本场演出的 <see cref="SceneActor"/> 决定。
    /// </summary>
    private SceneText ResolveLine(SceneRun run, int step, int line, SceneText text)
    {
        var actor = SceneActor;
        if (actor == null)
            return text;

        // 正文：生成槽有成品就用成品；没有则退回内容表写的静态文本。
        var body = text.Text;
        if (text.NeedsGeneration)
        {
            var produced = State.SceneTexts.Get(_sceneEventId, step, line);
            if (produced != null)
                body = produced;
        }

        // 占位说话人：现掷演员的场景里，内容表第一角色名就是占位符。
        var speaker = text.Speaker;
        if (run.Event.Spawn && run.Event.Characters.Count > 0
            && speaker == run.Event.Characters[0])
            speaker = actor.Name;

        return text with { Speaker = speaker, Text = VoicePack.Personalize(body, actor.Name) };
    }

    /// <summary>
    /// 尝试触发当前房间的情境场景演出（进入房间等唯一情境挂点调用）。
    /// 所有场景终身仅演一次（FiredEvents 终身去重）。
    /// </summary>
    public bool TryPlayScene()
    {
        // 只在领地生效：世界层在赶路，插画不该打断行程；演出中不重复触发。
        if (Layer != MapLayer.Territory || ScenePlaying)
            return false;

        var present = new List<CharacterState>();
        foreach (var m in State.Roster.Members)
        {
            if (!m.IsMaster && _presence.GetValueOrDefault(m.Id, -1) == PlayerRoomId)
                present.Add(m);
        }
        if (present.Count == 0)
            return false;

        // 洗一下起点，免得每次都是同一个人先开演。
        var start = Day.Rng.Next(present.Count);
        for (var i = 0; i < present.Count; i++)
        {
            if (PlayScene(present[(start + i) % present.Count]))
                return true;
        }
        return false;
    }
}
