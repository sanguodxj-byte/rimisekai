using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    /// <summary>
    /// 让某个角色在此时机开口。台词唯一的呈现是对话框；挑不出台词返回 false，
    /// 调用方走自己的行为叙述文案（叙述落日志，台词不落日志）。
    ///
    /// 上下文里的活动由角色当前状态推导，因此内容包能写“在做饭时说的话”。
    /// 互动类时机（摸头、拥抱等）由调用方给出 role 与 place。
    /// </summary>
    public bool Say(
        CharacterState who,
        VoiceTrigger trigger,
        string giftItemId = "",
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before)
    {
        var ctx = VoiceContext.For(
            who,
            trigger,
            State.Roster.Master?.Id ?? -1,
            State.Clock.Season,
            State.Weather,
            State.Clock.Day,
            State.Clock.Minutes,
            PlayerRoomId,
            giftItemId,
            Day.Rng,
            ActivityOf(who.Id),
            role,
            place,
            VoiceContext.EmotionOf(who),
            _presence.GetValueOrDefault(who.Id, -1));
        var utterance = State.Voice.Speak(who, trigger, ctx);
        if (utterance == null)
            return false;
        Show(VoiceDirector.ToOverlay(utterance.Value));
        return true;
    }

    /// <summary>
    /// 让某个角色在此时机开口，允许台词由 LLM 现场生成。
    /// 与同步版共用同一条挑选与呈现链路，只是能等到模型。
    /// 没有接入生成器时行为与 <see cref="Say"/> 一致。
    /// </summary>
    public async System.Threading.Tasks.Task<bool> SayAsync(
        CharacterState who,
        VoiceTrigger trigger,
        string giftItemId = "")
    {
        var ctx = VoiceContext.For(
            who,
            trigger,
            State.Roster.Master?.Id ?? -1,
            State.Clock.Season,
            State.Weather,
            State.Clock.Day,
            State.Clock.Minutes,
            PlayerRoomId,
            giftItemId,
            Day.Rng,
            ActivityOf(who.Id),
            VoiceRole.Actor,
            VoicePlace.Before,
            VoiceContext.EmotionOf(who),
            _presence.GetValueOrDefault(who.Id, -1));
        var utterance = await State.Voice.SpeakAsync(who, trigger, ctx).ConfigureAwait(false);
        if (utterance == null)
            return false;
        Show(VoiceDirector.ToOverlay(utterance.Value));
        return true;
    }

    /// <summary>
    /// 给某个角色开一段场景事件（多步剧情）。挑不出可触发的事件就返回 null。
    /// 事件表为空时整条链路旁路。
    /// </summary>
    public SceneRun? BeginScene(CharacterState who)
    {
        if (State.Voice.Scenes.Count == 0)
            return null;
        var ctx = VoiceContext.For(
            who,
            VoiceTrigger.Scene,
            State.Roster.Master?.Id ?? -1,
            State.Clock.Season,
            State.Weather,
            State.Clock.Day,
            State.Clock.Minutes,
            PlayerRoomId,
            "",
            Day.Rng,
            ActivityOf(who.Id),
            VoiceRole.Actor,
            VoicePlace.Before,
            VoiceContext.EmotionOf(who),
            _presence.GetValueOrDefault(who.Id, -1));
        return new SceneRunner(State.Voice.Scenes, State.Territory).Begin(who, ctx);
    }

    /// <summary>
    /// 角色主动找玩家搭话的出口（自动节律里唯一的弹层入口）。
    /// 挑得出台词就弹对话框并返回 true；挑不出返回 false，
    /// 节律那边退回自己的行为叙述。
    /// </summary>
    bool IVoiceSink.Dialogue(CharacterState who, VoiceTrigger trigger)
    {
        var ctx = VoiceContext.For(
            who,
            trigger,
            State.Roster.Master?.Id ?? -1,
            State.Clock.Season,
            State.Weather,
            State.Clock.Day,
            State.Clock.Minutes,
            PlayerRoomId,
            "",
            Day.Rng,
            ActivityOf(who.Id),
            VoiceRole.Actor,
            VoicePlace.Before,
            VoiceContext.EmotionOf(who),
            _presence.GetValueOrDefault(who.Id, -1));
        var utterance = State.Voice.Speak(who, trigger, ctx);
        if (utterance == null)
            return false;
        Show(VoiceDirector.ToOverlay(utterance.Value));
        return true;
    }

    /// <summary>
    /// 状态地文出口。按角色当前活动取一句"她在做什么"的地文；
    /// 挑不出返回 null——调用方什么都不显示，引擎不提供通用兜底句。
    /// 地文多行时压成一行（状态描述是日志/快照，不是对话框）。
    /// </summary>
    string? IVoiceSink.StateLine(CharacterState who, VoiceActivity activity, string facilityName)
    {
        var ctx = VoiceContext.For(
            who,
            VoiceTrigger.State,
            State.Roster.Master?.Id ?? -1,
            State.Clock.Season,
            State.Weather,
            State.Clock.Day,
            State.Clock.Minutes,
            PlayerRoomId,
            "",
            Day.Rng,
            activity,
            VoiceRole.Actor,
            VoicePlace.Before,
            VoiceContext.EmotionOf(who),
            _presence.GetValueOrDefault(who.Id, -1));
        var utterance = State.Voice.Speak(who, VoiceTrigger.State, ctx);
        if (utterance == null)
            return null;
        return VoiceDirector.ToLog(utterance.Value);
    }

    /// <summary>
    /// 角色当前活动的单一来源在 Workday（引擎事实），这里只负责查 worker。
    /// </summary>
    private VoiceActivity ActivityOf(int characterId)
    {
        var worker = FindWorker(characterId);
        return worker == null ? VoiceActivity.Idle : Worker.ActivityOf(worker);
    }

    private Worker? FindWorker(int characterId)
    {
        foreach (var worker in Day.Workers)
        {
            if (worker.CharacterId == characterId)
                return worker;
        }
        return null;
    }
}
