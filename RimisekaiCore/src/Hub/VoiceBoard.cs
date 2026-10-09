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
        VoicePlace place = VoicePlace.Before,
        VoiceKind? kind = null)
    {
        var utterance = PickVoice(who, trigger, giftItemId, role, place, kind);
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
        string giftItemId = "",
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before,
        VoiceKind? kind = null)
    {
        var utterance = await PickVoiceAsync(who, trigger, giftItemId, role, place, kind).ConfigureAwait(false);
        if (utterance == null)
            return false;
        Show(VoiceDirector.ToOverlay(utterance.Value));
        return true;
    }

    /// <summary>同步挑一句台词或地文（不直接弹层），供复合对话/地文队列装配。</summary>
    public VoiceUtterance? PickVoice(
        CharacterState who,
        VoiceTrigger trigger,
        string giftItemId = "",
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before,
        VoiceKind? kind = null)
    {
        var ctx = CreateVoiceContext(who, trigger, giftItemId, null, role, place);
        return State.Voice.Speak(who, trigger, ctx, kind, place);
    }

    /// <summary>异步挑一句台词或地文（支持 LLM 动态生成，不直接弹层），供复合对话/地文队列装配。</summary>
    public async System.Threading.Tasks.Task<VoiceUtterance?> PickVoiceAsync(
        CharacterState who,
        VoiceTrigger trigger,
        string giftItemId = "",
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before,
        VoiceKind? kind = null)
    {
        var ctx = CreateVoiceContext(who, trigger, giftItemId, null, role, place);
        return await State.Voice.SpeakAsync(who, trigger, ctx, kind, place).ConfigureAwait(false);
    }

    /// <summary>
    /// 角色主动找玩家搭话的出口（自动节律里唯一的弹层入口）。
    /// 挑得出台词就弹对话框并返回 true；挑不出返回 false，
    /// 节律那边退回自己的行为叙述。
    /// </summary>
    bool IVoiceSink.Dialogue(CharacterState who, VoiceTrigger trigger)
    {
        var ctx = CreateVoiceContext(who, trigger);
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
        var ctx = CreateVoiceContext(who, VoiceTrigger.State, "", activity);
        var utterance = State.Voice.Speak(who, VoiceTrigger.State, ctx);
        if (utterance == null)
            return null;
        return VoiceDirector.ToLog(utterance.Value);
    }

    public IReadOnlyList<string> RoomTagsOf(int roomId)
    {
        var room = Room(roomId);
        if (room == null || room.Tags.Count == 0)
            return System.Array.Empty<string>();
        return new List<string>(room.Tags);
    }

    public bool CheckAllMembersMaxLevel()
    {
        if (State.Roster.Members.Count == 0)
            return false;
        foreach (var m in State.Roster.Members)
        {
            if (m.Level < Character.XpTable.MaxLevel)
                return false;
        }
        return true;
    }

    public VoiceContext CreateVoiceContext(
        CharacterState who,
        VoiceTrigger trigger,
        string giftItemId = "",
        VoiceActivity? activity = null,
        VoiceRole role = VoiceRole.Actor,
        VoicePlace place = VoicePlace.Before,
        VoiceEmotion? emotion = null,
        int characterRoomId = -1,
        int facilityId = -1)
    {
        var charRoom = characterRoomId >= 0 ? characterRoomId : _presence.GetValueOrDefault(who.Id, -1);
        return VoiceContext.For(
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
            activity ?? ActivityOf(who.Id),
            role,
            place,
            emotion ?? VoiceContext.EmotionOf(who),
            charRoom,
            facilityId,
            State.FiredEvents,
            CheckAllMembersMaxLevel(),
            RoomTagsOf(charRoom),
            State.ReturnedFromCombat,
            Setting);
    }

    /// <summary>
    /// 此刻所在的场景：领地；大地图上＝野外；地城（遗迹与委托地城）；其余兴趣点＝聚落。
    /// 台词与场景剧情只在自己写明的场景里出现（见 <see cref="VoiceSetting"/>）。
    /// </summary>
    public VoiceSetting Setting => Layer switch
    {
        MapLayer.Territory => VoiceSetting.Territory,
        MapLayer.World => VoiceSetting.Wilds,
        _ => InDungeon ? VoiceSetting.Dungeon : VoiceSetting.Settlement,
    };

    // ---------- 场景内主动对话 ----------

    /// <summary>同伴主动开口的一段话：说话人与各句，<see cref="Index"/> 是此刻显示到第几句。</summary>
    public sealed class Chatter
    {
        public required CharacterState Speaker { get; init; }
        public required IReadOnlyList<string> Lines { get; init; }

        /// <summary>开口时所在的场景。换了场景这段话就作废，不带进别处。</summary>
        public required VoiceSetting Setting { get; init; }

        /// <summary>挑中的那句台词（供核对它属于哪些场景）。</summary>
        public required VoiceLine Line { get; init; }
        public int Index { get; set; }
        public string Text => Lines[Index];
    }

    /// <summary>此刻正在说的主动对话；没有为 null。前端画成指向说话人头像的气泡。</summary>
    public Chatter? PendingChatter
    {
        get
        {
            // 台词分场景，禁止跨越：换了场景、或说话的人已不在眼前，话头就此作废。
            if (_chatter != null && (_chatter.Setting != Setting || !CompanionsHere().Contains(_chatter.Speaker)))
                _chatter = null;
            return _chatter;
        }
        private set => _chatter = value;
    }

    private Chatter? _chatter;

    /// <summary>点气泡：推进一句，说完收起。</summary>
    public void AdvanceChatter()
    {
        if (PendingChatter == null)
            return;
        PendingChatter.Index++;
        if (PendingChatter.Index >= PendingChatter.Lines.Count)
            PendingChatter = null;
    }

    /// <summary>
    /// 此刻与主角同处一地、在「此刻」里看得见的同伴：大地图上是跟着走的人，别处是同一间房里的人。
    /// </summary>
    public List<CharacterState> CompanionsHere()
    {
        var list = new List<CharacterState>();
        var party = Layer == MapLayer.World ? WorldPartyIds() : null;
        foreach (var who in State.Roster.Members)
        {
            if (who.IsMaster)
                continue;
            if (party != null ? party.Contains(who.Id) : PlayerRoomId >= 0 && _presence.GetValueOrDefault(who.Id, -1) == PlayerRoomId)
                list.Add(who);
        }
        return list;
    }

    /// <summary>
    /// 时间流过、落脚之后问一圈：同处一地的同伴有没有当下场景的主动对话（chance、冷却都在台词门槛里）。
    /// 已有气泡、弹层、剧情或遭遇挡着时不起；一次只一人开口。
    /// </summary>
    private void RollChatter()
    {
        if (PendingChatter != null || Overlay != null || PendingEncounter != null || !State.Voice.Enabled)
            return;
        foreach (var who in CompanionsHere())
        {
            var utterance = PickVoice(who, VoiceTrigger.Chatter, kind: VoiceKind.Speech);
            if (utterance == null || utterance.Value.Lines.Count == 0)
                continue;
            PendingChatter = new Chatter { Speaker = who, Lines = utterance.Value.Lines, Setting = Setting, Line = utterance.Value.Line };
            return;
        }
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
