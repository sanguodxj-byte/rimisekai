using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    public const int TalkFails = 1;

    public const int CostTalk = 6;
    public const int CostObserve = 2;
    public const int CostGift = 1;
    public const int CostTouch = 2;
    public const int CostPat = 2;
    public const int CostContact = 4;
    public const int CostHug = 4;
    public const int CostKiss = 6;

    public const int FavorPatHead = 100;
    public const int FavorBodyContact = 300;
    public const int FavorHug = 500;
    public const int FavorKiss = 600;
    public const int CostAct = 1;

    /// <summary>邀请同行。与赠物同为最轻的一次社交动作。</summary>
    public const int CostInvite = 1;

    private bool IsLiked(CharacterState who)
    {
        var master = State.Roster.Master;
        return master != null && (who.Relations.Has(master.Id, RelationFlag.Trusted)
            || who.Relations.Has(master.Id, RelationFlag.Sworn)
            || who.Condition.Bond >= Bond.Fond);
    }

    private bool RequireFavor(CharacterState who, int favor)
    {
        if (who.Condition.Favor >= favor)
            return true;
        Write($"{who.Name}躲开了。");
        return false;
    }

    public bool Social(SocialAction action, string giftItemId = "")
    {
        var who = State.Roster.Find(SelectedCharacterId);
        if (who == null || who.IsMaster)
            return false;
        if (_presence.GetValueOrDefault(who.Id, -1) != PlayerRoomId)
            return false;
        if (action == SocialAction.Gift && (giftItemId.Length == 0
            || (State.Roster.Master?.Bag.Get(giftItemId) ?? 0) < 1))
            return false;
        PassTime((action switch
        {
            SocialAction.Talk => CostTalk,
            SocialAction.Observe => CostObserve,
            SocialAction.Gift => CostGift,
            SocialAction.Touch => CostTouch,
            SocialAction.PatHead => CostPat,
            SocialAction.BodyContact => CostContact,
            SocialAction.Hug => CostHug,
            SocialAction.Kiss => CostKiss,
            SocialAction.Invite => CostInvite,
            _ => CostAct,
        }) * TerritoryClock.StepMinutes);
        var bondBefore = who.Condition.Bond;
        switch (action)
        {
            case SocialAction.Talk:
                if (who.Condition.Bond <= Bond.Hostile)
                {
                    if (!Say(who, VoiceTrigger.TalkRefused))
                        Write($"{who.Name}不想理你。");
                    return true;
                }
                if (who.TalkDifficulty() > 0 && who.Get(who.Flags, TalkFails) == 0)
                {
                    who.Set(who.Flags, TalkFails, 1);
                    if (!Say(who, VoiceTrigger.TalkRefused))
                        Write($"{who.Name}没有接话。");
                    return true;
                }
                Write($"你与{who.Name}交谈。");
                who.GainLifeExp(LifeSkill.Social, 1);
                who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Talk, 5));
                if (IsLiked(who))
                    who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 3));
                who.Affect.AddDesire(-who.Affect.ChatDesire);
                who.Affect.LastTalkAt = (State.Clock.Day - 1) * GameClock.MinutesPerDay + State.Clock.Minutes;
                var seeker = Day.Track(who.Id, PlayerRoomId);
                seeker.Goal = ActionKind.None;
                seeker.Task = ActionKind.None;
                seeker.Phase = WorkPhase.Idle;
                seeker.Path.Clear();
                seeker.WaitTicks = 0;
                seeker.WantsChat = false;
                seeker.SeekWaiting = false;
                seeker.ChatRoom = -1;
                var master = State.Roster.Master;
                if (master != null)
                {
                    master.Relations.Add(who.Id, RelationFlag.Acquainted);
                    who.Relations.Add(master.Id, RelationFlag.Acquainted);
                }
                if (!Say(who, VoiceTrigger.Talk))
                    Show(MapOverlay.Dialogue(who.Name, new[] { "……" }));
                break;
            case SocialAction.Observe:
                if (!Say(who, VoiceTrigger.Observe))
                    Write($"你观察{who.Name}。");
                break;
            case SocialAction.Gift:
                State.Roster.Master?.Bag.Add(giftItemId, -1);
                who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Gift, 20));
                if (!Say(who, VoiceTrigger.Gift, giftItemId: giftItemId))
                    Write($"你把{giftItemId}送给了{who.Name}。");
                break;
            case SocialAction.Touch:
                if (IsLiked(who))
                    who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 5));
                if (!Say(who, VoiceTrigger.Touch))
                    Write($"你接触了{who.Name}。");
                break;
            case SocialAction.PatHead:
                if (!RequireFavor(who, FavorPatHead))
                    break;
                if (who.Affect.TakeReward(State.Clock.Day, 0, Affect.PatDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Care, 5));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 4));
                }
                if (!Say(who, VoiceTrigger.PatHead))
                    Write($"你摸了摸{who.Name}的头。");
                break;
            case SocialAction.BodyContact:
                if (!RequireFavor(who, FavorBodyContact))
                    break;
                if (who.Affect.TakeReward(State.Clock.Day, 1, Affect.ContactDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Care, 8));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 6));
                }
                if (!Say(who, VoiceTrigger.BodyContact))
                    Write($"你与{who.Name}有了身体接触。");
                break;
            case SocialAction.Hug:
                if (!RequireFavor(who, FavorHug))
                    break;
                if (who.Affect.TakeReward(State.Clock.Day, 2, Affect.HugDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Intimate, 12));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 8));
                }
                if (!Say(who, VoiceTrigger.Hug))
                    Write($"你抱住了{who.Name}。");
                break;
            case SocialAction.Kiss:
                if (!RequireFavor(who, FavorKiss))
                    break;
                if (who.Affect.TakeReward(State.Clock.Day, 3, Affect.KissDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Intimate, 15));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 10));
                }
                if (!Say(who, VoiceTrigger.Kiss))
                    Write($"你亲吻了{who.Name}。");
                break;
            case SocialAction.Invite:
                if (who.AcceptsInvite())
                {
                    if (!Say(who, VoiceTrigger.Invited))
                        Write($"{who.Name}答应了你的邀请。");
                }
                else if (!Say(who, VoiceTrigger.InviteRefused))
                {
                    Write($"{who.Name}没有答应。");
                }
                break;
        }
        if (who.Condition.Bond > bondBefore)
            Say(who, VoiceTrigger.BondUp);
        return true;
    }

    /// <summary>动作 → 说话时机。两者一一对应，名字也保持一致。</summary>
    public static VoiceTrigger TriggerOf(SocialAction action) => action switch
    {
        SocialAction.Talk => VoiceTrigger.Talk,
        SocialAction.Observe => VoiceTrigger.Observe,
        SocialAction.Gift => VoiceTrigger.Gift,
        SocialAction.Touch => VoiceTrigger.Touch,
        SocialAction.PatHead => VoiceTrigger.PatHead,
        SocialAction.BodyContact => VoiceTrigger.BodyContact,
        SocialAction.Hug => VoiceTrigger.Hug,
        SocialAction.Kiss => VoiceTrigger.Kiss,
        SocialAction.Invite => VoiceTrigger.Invited,
        _ => VoiceTrigger.Idle,
    };
}
