using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

public sealed partial class HubSession
{
    public const int TalkFails = 1;

    /// <summary>接触阶梯旗标键：存进角色 Flags（随存档持久），成功做过一步才解锁下一步。</summary>
    public const int FlagPatHeadDone = 10;
    public const int FlagBodyContactDone = 11;
    public const int FlagHugDone = 12;

    /// <summary>
    /// 接触阶梯第 step 步（0=摸头）对当前选中角色是否已解锁：
    /// 首步恒可，其余需存档里成功做过上一步。跟随存档，不随会话清零。
    /// </summary>
    public bool TouchStepUnlocked(int step)
    {
        if (step <= 0)
            return true;
        var who = State.Roster.Find(SelectedCharacterId);
        if (who == null)
            return false;
        // 常识：恋人或誓约关系的同伴，所有亲密交互自然全解锁，无需从摸头重新打卡
        if (who.Condition.Bond == Bond.Lover || (State.Roster.Master != null && who.Relations.Has(State.Roster.Master.Id, RelationFlag.Sworn)))
            return true;

        var key = step switch
        {
            1 => FlagPatHeadDone,
            2 => FlagBodyContactDone,
            3 => FlagHugDone,
            _ => -1,
        };
        return key >= 0 && who.Get(who.Flags, key) > 0;
    }

    public readonly record struct SocialCheckResult(
        bool Success,
        int Favor,
        int Gate,
        string Summary);

    public static int DoneFlagKey(SocialAction action) => action switch
    {
        SocialAction.PatHead => FlagPatHeadDone,
        SocialAction.BodyContact => FlagBodyContactDone,
        SocialAction.Hug => FlagHugDone,
        _ => SceneLibrary.FlagKey($"social.{action}.done"),
    };

    public bool HasEverSucceeded(CharacterState who, SocialAction action)
    {
        var key = DoneFlagKey(action);
        return who.Get(who.Flags, key) > 0;
    }

    public void MarkEverSucceeded(CharacterState who, SocialAction action)
    {
        var key = DoneFlagKey(action);
        who.Set(who.Flags, key, 1);
    }

    /// <summary>
    /// 综合动作成功判定：除了好感，还综合考量性格特质、即时心情与环境私密性。
    /// 未成功过的行动在第一行用暗色字体写出判定过程与结果，后续不再显示。
    /// 严禁冒号，用空格或点号分隔。
    /// </summary>
    public SocialCheckResult EvaluateSocialCheck(CharacterState who, SocialAction action)
    {
        var baseFavor = action switch
        {
            SocialAction.PatHead => FavorPatHead,
            SocialAction.BodyContact => FavorBodyContact,
            SocialAction.Hug => FavorHug,
            SocialAction.Kiss => FavorKiss,
            SocialAction.Invite => 100,
            _ => 0,
        };

        if (baseFavor == 0)
        {
            return new SocialCheckResult(true, who.Condition.Favor, 0, "");
        }

        // 1. 性格特质修正百分比（100 为基准）
        var traitPercent = PersonalityTraits.TouchGatePercent(who);

        // 2. 心情修正（以 50 为基准）
        var moodBonus = (int)((who.Affect.Mood - 50) * 0.4f);

        // 3. 环境私密性修正
        var envMod = 0;
        var room = State.Territory.Rooms.Find(r => r.Id == PlayerRoomId);
        var bystanders = 0;
        foreach (var w in Day.Workers)
        {
            if (w.RoomId == PlayerRoomId && w.CharacterId != who.Id)
                bystanders++;
        }
        if (action is SocialAction.BodyContact or SocialAction.Hug or SocialAction.Kiss)
        {
            if (bystanders > 1)
                envMod += 30;
            else if (room != null && (room.HasTag("私人空间") || room.HasTag("卧室")))
                envMod -= 15;
        }

        // 综合门槛
        var gate = System.Math.Max(0, (baseFavor * traitPercent / 100) - moodBonus + envMod);
        var success = who.Condition.Favor >= gate;

        // 组装无冒号的判定过程文本
        var sb = new System.Text.StringBuilder("［判定 好感 ");
        sb.Append(who.Condition.Favor).Append('/').Append(baseFavor);
        sb.Append(" · 心情 ").Append(who.Affect.Mood);
        if (traitPercent != 100)
            sb.Append(traitPercent > 100 ? $" · 矜持(+{traitPercent - 100}%)" : $" · 依从({traitPercent - 100}%)");
        if (envMod > 0)
            sb.Append(" · 旁人在场(+30)");
        else if (envMod < 0)
            sb.Append(" · 私密独处(-15)");
        sb.Append(" ＝ 门槛 ").Append(gate);
        sb.Append(success ? " · 判定成功］" : " · 判定失败］");

        return new SocialCheckResult(success, who.Condition.Favor, gate, sb.ToString());
    }

    /// <summary>
    /// 观察四周的最短链：玩家所在房间的插画路径。
    /// 支持房间实体、DefDatabase 按 Id/Name 查询，以及卧室夜景昼夜差分。
    /// 房间定义未声明插画时返回空串，前端退回智能匹配与占位插画。
    /// </summary>
    public string ObservedRoomIllustration()
    {
        var room = State.Territory.Rooms.Find(r => r.Id == PlayerRoomId);
        if (room == null)
            return "";

        // 卧室昼夜差分：夜间（20:00~6:00）优先返回夜景插画
        if ((room.Name.Contains("卧") || room.HasTag("卧室")) && (State.Clock.Hour < 6 || State.Clock.Hour >= 20))
            return "res://assets/room_bedroom_night.png";

        if (!string.IsNullOrEmpty(room.Illustration))
            return room.Illustration;

        // 只按名字权威检索（DefName/Label），禁止按数值 Id 撞库——运行时自增 Id 会撞上无关定义。
        var def = DefDatabase<RoomDef>.Get(room.Name)
               ?? DefDatabase<RoomDef>.All.FirstOrDefault(d => d.Name == room.Name || d.Label == room.Name);
        if (def != null && !string.IsNullOrEmpty(def.Illustration))
            return def.Illustration;

        return "";
    }

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

    /// <summary>
    /// 一项社交行动按钮上要显示的**分钟耗时**，供界面画在按钮右侧。
    /// 与 <see cref="Social"/> 里的 switch 同源，所以界面画得出来的时间就是实际扣掉的时间。
    /// 领点（会话）不耗时的动作返回 0，界面据此不显示右侧数值。
    /// </summary>
    public int ActionMinutes(SocialAction action) => (action switch
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
        // 分开不耗时。
        SocialAction.Part => 0,
        _ => CostAct,
    }) * TerritoryClock.StepMinutes;

    private bool IsLiked(CharacterState who)
    {
        var master = State.Roster.Master;
        return master != null && (who.Relations.Has(master.Id, RelationFlag.Trusted)
            || who.Relations.Has(master.Id, RelationFlag.Sworn)
            || who.Condition.Bond >= Bond.Fond);
    }

    private bool RequireFavor(CharacterState who, int favor, string refusedText = "")
    {
        // 接受判定：基础好感档 × 特质百分比，再叠心情修正
        //（心情 50 为中点，每 10 点心情折 4 档；特质倍率见 PersonalityTraits.TouchGatePercent）。
        var gate = favor * PersonalityTraits.TouchGatePercent(who) / 100
            - (int)((who.Affect.Mood - 50) * 0.4f);
        if (who.Condition.Favor >= System.Math.Max(0, gate))
            return true;
        Write(refusedText.Length > 0 ? refusedText : $"{who.Name}躲开了。");
        return false;
    }

    public bool Social(SocialAction action, string giftItemId = "")
    {
        // 演出中不交互，彻底避免场景与交谈等动作撞车。
        if (ScenePlaying)
            return false;

        var who = State.Roster.Find(SelectedCharacterId);
        if (who == null || who.IsMaster)
            return false;
        // 分开不用同房：人可能还落在后一间房里，照样能请回。
        if (action == SocialAction.Part)
        {
            var leaving = Day.Track(who.Id, PlayerRoomId);
            if (leaving.FollowsPlayer)
            {
                EndFollow(leaving);
                Write($"{who.Name}不再跟着你了。");
            }
            else
            {
                Write($"{who.Name}没有跟着你。");
            }
            return true;
        }
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
            // 分开不耗时。
            SocialAction.Part => 0,
            _ => CostAct,
        }) * TerritoryClock.StepMinutes);
        var bondBefore = who.Condition.Bond;
        var check = EvaluateSocialCheck(who, action);
        var showCheckHeader = !HasEverSucceeded(who, action);

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
                PresentInteraction(who, SocialAction.Talk, check, showCheckHeader);
                break;
            case SocialAction.Observe:
                PresentInteraction(who, SocialAction.Observe, check, showCheckHeader);
                break;
            case SocialAction.Gift:
                if (who.Condition.Bond <= Bond.Hostile)
                {
                    Show(MapOverlay.Dialogue(who.Name, new[] { $"{who.Name}冷淡地别过头，没有收下你递过去的东西。" }));
                    return true;
                }
                var baseGiftGain = GiftFavorGain(who, giftItemId);
                State.Roster.Master?.Bag.Add(giftItemId, -1);
                who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Gift, baseGiftGain));
                PresentInteraction(who, SocialAction.Gift, check, showCheckHeader, giftItemId: giftItemId);
                break;
            case SocialAction.Touch:
                if (IsLiked(who))
                    who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 5));
                PresentInteraction(who, SocialAction.Touch, check, showCheckHeader);
                break;
            case SocialAction.PatHead:
                if (!check.Success)
                {
                    PresentInteraction(who, SocialAction.PatHead, check, showCheckHeader);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                who.Set(who.Flags, FlagPatHeadDone, 1);
                if (who.Affect.TakeReward(State.Clock.Day, 0, Affect.PatDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Care, 5));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 4));
                }
                PresentInteraction(who, SocialAction.PatHead, check, showCheckHeader);
                break;
            case SocialAction.BodyContact:
                if (!check.Success)
                {
                    PresentInteraction(who, SocialAction.BodyContact, check, showCheckHeader);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                who.Set(who.Flags, FlagBodyContactDone, 1);
                if (who.Affect.TakeReward(State.Clock.Day, 1, Affect.ContactDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Care, 8));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 6));
                }
                PresentInteraction(who, SocialAction.BodyContact, check, showCheckHeader);
                break;
            case SocialAction.Hug:
                if (!check.Success)
                {
                    PresentInteraction(who, SocialAction.Hug, check, showCheckHeader);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                who.Set(who.Flags, FlagHugDone, 1);
                if (who.Affect.TakeReward(State.Clock.Day, 2, Affect.HugDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Intimate, 12));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 8));
                }
                PresentInteraction(who, SocialAction.Hug, check, showCheckHeader);
                break;
            case SocialAction.Kiss:
                if (!check.Success)
                {
                    PresentInteraction(who, SocialAction.Kiss, check, showCheckHeader);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                if (who.Affect.TakeReward(State.Clock.Day, 3, Affect.KissDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Intimate, 15));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 10));
                }
                PresentInteraction(who, SocialAction.Kiss, check, showCheckHeader);
                break;
            case SocialAction.Invite:
            {
                var follower = Day.Track(who.Id, PlayerRoomId);
                if (follower.FollowsPlayer)
                {
                    // 已经跟着了：请回走“分开”，邀请不再当解雇用。
                    Write($"{who.Name}已经跟着你了。");
                }
                else if (who.AcceptsInvite())
                {
                    follower.FollowsPlayer = true;
                    if (!Say(who, VoiceTrigger.Invited))
                        Write($"{who.Name}答应了你的邀请，跟了上来。");
                }
                else if (!Say(who, VoiceTrigger.InviteRefused))
                {
                    Write($"{who.Name}没有答应。");
                }
                break;
            }
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

    public async System.Threading.Tasks.Task<bool> SocialAsync(SocialAction action, string giftItemId = "")
    {
        if (ScenePlaying)
            return false;

        var who = State.Roster.Find(SelectedCharacterId);
        if (who == null || who.IsMaster)
            return false;

        if (action == SocialAction.Part)
            return Social(action, giftItemId);

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
            SocialAction.Part => 0,
            _ => CostAct,
        }) * TerritoryClock.StepMinutes);

        var bondBefore = who.Condition.Bond;
        var check = EvaluateSocialCheck(who, action);
        var showCheckHeader = !HasEverSucceeded(who, action);

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
                await PresentInteractionAsync(who, SocialAction.Talk, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.Observe:
                await PresentInteractionAsync(who, SocialAction.Observe, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.Gift:
                if (who.Condition.Bond <= Bond.Hostile)
                {
                    Show(MapOverlay.Dialogue(who.Name, new[] { $"{who.Name}冷淡地别过头，没有收下你递过去的东西。" }));
                    return true;
                }
                var baseGiftGainAsync = GiftFavorGain(who, giftItemId);
                State.Roster.Master?.Bag.Add(giftItemId, -1);
                who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Gift, baseGiftGainAsync));
                await PresentInteractionAsync(who, SocialAction.Gift, check, showCheckHeader, giftItemId: giftItemId).ConfigureAwait(false);
                break;
            case SocialAction.Touch:
                if (IsLiked(who))
                    who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 5));
                await PresentInteractionAsync(who, SocialAction.Touch, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.PatHead:
                if (!check.Success)
                {
                    await PresentInteractionAsync(who, SocialAction.PatHead, check, showCheckHeader).ConfigureAwait(false);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                who.Set(who.Flags, FlagPatHeadDone, 1);
                if (who.Affect.TakeReward(State.Clock.Day, 0, Affect.PatDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Care, 5));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 4));
                }
                await PresentInteractionAsync(who, SocialAction.PatHead, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.BodyContact:
                if (!check.Success)
                {
                    await PresentInteractionAsync(who, SocialAction.BodyContact, check, showCheckHeader).ConfigureAwait(false);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                who.Set(who.Flags, FlagBodyContactDone, 1);
                if (who.Affect.TakeReward(State.Clock.Day, 1, Affect.ContactDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Care, 8));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 6));
                }
                await PresentInteractionAsync(who, SocialAction.BodyContact, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.Hug:
                if (!check.Success)
                {
                    await PresentInteractionAsync(who, SocialAction.Hug, check, showCheckHeader).ConfigureAwait(false);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                who.Set(who.Flags, FlagHugDone, 1);
                if (who.Affect.TakeReward(State.Clock.Day, 2, Affect.HugDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Intimate, 12));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 8));
                }
                await PresentInteractionAsync(who, SocialAction.Hug, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.Kiss:
                if (!check.Success)
                {
                    await PresentInteractionAsync(who, SocialAction.Kiss, check, showCheckHeader).ConfigureAwait(false);
                    Write($"{who.Name}躲开了。");
                    break;
                }
                if (who.Affect.TakeReward(State.Clock.Day, 3, Affect.KissDailyLimit))
                {
                    who.Condition.AddFavor(PersonalityTraits.ScaledFavor(who, SocialKind.Intimate, 15));
                    if (IsLiked(who))
                        who.Affect.AddMood(PersonalityTraits.ScaledMood(who, 10));
                }
                await PresentInteractionAsync(who, SocialAction.Kiss, check, showCheckHeader).ConfigureAwait(false);
                break;
            case SocialAction.Invite:
            {
                var follower = Day.Track(who.Id, PlayerRoomId);
                if (follower.FollowsPlayer)
                {
                    Write($"{who.Name}已经跟着你了。");
                }
                else if (who.AcceptsInvite())
                {
                    follower.FollowsPlayer = true;
                    if (!Say(who, VoiceTrigger.Invited))
                        Write($"{who.Name}答应了你的邀请，跟了上来。");
                }
                else if (!Say(who, VoiceTrigger.InviteRefused))
                {
                    Write($"{who.Name}没有答应。");
                }
                break;
            }
        }
        if (who.Condition.Bond > bondBefore)
            Say(who, VoiceTrigger.BondUp);
        return true;
    }

    private void PresentInteraction(
        CharacterState who,
        SocialAction action,
        SocialCheckResult check,
        bool showCheckHeader,
        string giftItemId = "")
    {
        if (action == SocialAction.Gift)
        {
            var speechUtter = PickVoice(who, VoiceTrigger.Gift, giftItemId, VoiceRole.Actor, VoicePlace.Before, VoiceKind.Speech);
            if (speechUtter != null && speechUtter.Value.Lines.Count > 0)
            {
                var giftLines = new List<OverlayLine>();
                foreach (var line in speechUtter.Value.Lines)
                    giftLines.Add(new OverlayLine(who.Name, line, false));
                Show(MapOverlay.Dialogue(who.Name, giftLines));
            }
            else
            {
                Write($"你把{giftItemId}送给了{who.Name}。");
            }
            return;
        }

        var trigger = TriggerOf(action);
        var lines = new List<OverlayLine>();

        // 1. 若该动作未曾成功过，第一行以暗色字体打印判定过程与结果
        if (showCheckHeader && check.Summary.Length > 0)
        {
            lines.Add(new OverlayLine("", check.Summary, Dim: true));
        }

        if (check.Success)
        {
            // 2. 玩家动作 A（地文 Narration, Place: Before）
            var actUtter = PickVoice(who, trigger, giftItemId, VoiceRole.Actor, VoicePlace.Before, VoiceKind.Narration);
            if (actUtter != null && actUtter.Value.Lines.Count > 0)
            {
                foreach (var line in actUtter.Value.Lines)
                    lines.Add(new OverlayLine("", line, Dim: false));
            }

            // 3. 角色反应 B（地文 Narration, Place: After）
            var reactUtter = PickVoice(who, trigger, giftItemId, VoiceRole.Partner, VoicePlace.After, VoiceKind.Narration);
            if (reactUtter != null && reactUtter.Value.Lines.Count > 0)
            {
                foreach (var line in reactUtter.Value.Lines)
                    lines.Add(new OverlayLine("", line, Dim: false));
            }

            // 4. 角色台词（Speech）
            var speechUtter = PickVoice(who, trigger, giftItemId, VoiceRole.Actor, VoicePlace.Before, VoiceKind.Speech);
            if (speechUtter != null && speechUtter.Value.Lines.Count > 0)
            {
                foreach (var line in speechUtter.Value.Lines)
                    lines.Add(new OverlayLine(who.Name, line, Dim: false));
            }

            if (lines.Count > 0)
            {
                Show(MapOverlay.Dialogue(who.Name, lines));
            }
            else
            {
                if (action == SocialAction.Talk)
                    Show(MapOverlay.Dialogue(who.Name, new[] { "……" }));
                else if (action == SocialAction.Observe)
                    Write($"你观察{who.Name}。");
                else if (action == SocialAction.Gift)
                    Write($"你把{giftItemId}送给了{who.Name}。");
                else if (action == SocialAction.Touch)
                    Write($"你接触了{who.Name}。");
                else if (action == SocialAction.PatHead)
                    Write($"你摸了摸{who.Name}的头。");
                else if (action == SocialAction.BodyContact)
                    Write($"你与{who.Name}有了身体接触。");
                else if (action == SocialAction.Hug)
                    Write($"你抱住了{who.Name}。");
                else if (action == SocialAction.Kiss)
                    Write($"你亲吻了{who.Name}。");
            }
            MarkEverSucceeded(who, action);
        }
        else
        {
            // 判定失败：交流和接触全部归入聊天界面呈现！
            var refuseText = action switch
            {
                SocialAction.PatHead => $"你伸手想要抚摸{who.Name}的头发，对方有些慌乱地偏过头躲开了。",
                SocialAction.BodyContact => $"你试图靠近{who.Name}的身侧，对方有些拘谨地退后了两步避开了。",
                SocialAction.Hug => $"你试图伸手拥抱，{who.Name}有些不知所措地后退了半步，躲开了。",
                SocialAction.Kiss => $"你俯下身想要亲吻，{who.Name}有些羞慌地捂住脸颊，避开了。",
                _ => $"{who.Name}有些不知所措地躲开了。",
            };
            lines.Add(new OverlayLine("", refuseText, Dim: false));
            Show(MapOverlay.Dialogue(who.Name, lines));
        }
    }

    private async System.Threading.Tasks.Task PresentInteractionAsync(
        CharacterState who,
        SocialAction action,
        SocialCheckResult check,
        bool showCheckHeader,
        string giftItemId = "")
    {
        if (action == SocialAction.Gift)
        {
            var speechUtter = await PickVoiceAsync(who, VoiceTrigger.Gift, giftItemId, VoiceRole.Actor, VoicePlace.Before, VoiceKind.Speech).ConfigureAwait(false);
            if (speechUtter != null && speechUtter.Value.Lines.Count > 0)
            {
                var giftLines = new List<OverlayLine>();
                foreach (var line in speechUtter.Value.Lines)
                    giftLines.Add(new OverlayLine(who.Name, line, false));
                Show(MapOverlay.Dialogue(who.Name, giftLines));
            }
            else
            {
                Write($"你把{giftItemId}送给了{who.Name}。");
            }
            return;
        }

        var trigger = TriggerOf(action);
        var lines = new List<OverlayLine>();

        // 1. 若该动作未曾成功过，第一行以暗色字体打印判定过程与结果
        if (showCheckHeader && check.Summary.Length > 0)
        {
            lines.Add(new OverlayLine("", check.Summary, Dim: true));
        }

        if (check.Success)
        {
            // 2. 玩家动作 A（地文 Narration, Place: Before）
            var actUtter = PickVoice(who, trigger, giftItemId, VoiceRole.Actor, VoicePlace.Before, VoiceKind.Narration);
            if (actUtter != null && actUtter.Value.Lines.Count > 0)
            {
                foreach (var line in actUtter.Value.Lines)
                    lines.Add(new OverlayLine("", line, Dim: false));
            }

            // 3. 角色反应 B（地文 Narration, Place: After，优先走 LLM 异步生成，失败回退静态兜底）
            var reactUtter = await PickVoiceAsync(who, trigger, giftItemId, VoiceRole.Partner, VoicePlace.After, VoiceKind.Narration).ConfigureAwait(false);
            if (reactUtter != null && reactUtter.Value.Lines.Count > 0)
            {
                foreach (var line in reactUtter.Value.Lines)
                    lines.Add(new OverlayLine("", line, Dim: false));
            }

            // 4. 角色台词（Speech，亦支持 LLM 异步生成与静态回退）
            var speechUtter = await PickVoiceAsync(who, trigger, giftItemId, VoiceRole.Actor, VoicePlace.Before, VoiceKind.Speech).ConfigureAwait(false);
            if (speechUtter != null && speechUtter.Value.Lines.Count > 0)
            {
                foreach (var line in speechUtter.Value.Lines)
                    lines.Add(new OverlayLine(who.Name, line, Dim: false));
            }

            if (lines.Count > 0)
            {
                Show(MapOverlay.Dialogue(who.Name, lines));
            }
            else
            {
                if (action == SocialAction.Talk)
                    Show(MapOverlay.Dialogue(who.Name, new[] { "……" }));
                else if (action == SocialAction.Observe)
                    Write($"你观察{who.Name}。");
                else if (action == SocialAction.Gift)
                    Write($"你把{giftItemId}送给了{who.Name}。");
                else if (action == SocialAction.Touch)
                    Write($"你接触了{who.Name}。");
                else if (action == SocialAction.PatHead)
                    Write($"你摸了摸{who.Name}的头。");
                else if (action == SocialAction.BodyContact)
                    Write($"你与{who.Name}有了身体接触。");
                else if (action == SocialAction.Hug)
                    Write($"你抱住了{who.Name}。");
                else if (action == SocialAction.Kiss)
                    Write($"你亲吻了{who.Name}。");
            }
            MarkEverSucceeded(who, action);
        }
        else
        {
            var refuseText = action switch
            {
                SocialAction.PatHead => $"你伸手想要抚摸{who.Name}的头发，对方有些慌乱地偏过头躲开了。",
                SocialAction.BodyContact => $"你试图靠近{who.Name}的身侧，对方有些拘谨地退后了两步避开了。",
                SocialAction.Hug => $"你试图伸手拥抱，{who.Name}有些不知所措地后退了半步，躲开了。",
                SocialAction.Kiss => $"你俯下身想要亲吻，{who.Name}有些羞慌地捂住脸颊，避开了。",
                _ => $"{who.Name}有些不知所措地躲开了。",
            };
            lines.Add(new OverlayLine("", refuseText, Dim: false));
            Show(MapOverlay.Dialogue(who.Name, lines));
        }
    }

    /// <summary>
    /// 赠礼投其所好：心仪礼物加 30 好感，普通日用品加 12，粗陋废料碎石仅加 2。
    /// </summary>
    public static int GiftFavorGain(CharacterState who, string giftItemId)
    {
        if (giftItemId.Length == 0)
            return 20;

        // 女仆（璐米埃尔）：喜欢花饰、香草茶、蜂蜜点心与针线布匹
        if (who.Name == "璐米埃尔" || who.IsMaid())
        {
            if (giftItemId.Contains("花") || giftItemId.Contains("草") || giftItemId.Contains("茶")
                || giftItemId.Contains("蜜") || giftItemId.Contains("布") || giftItemId.Contains("菲亚多内"))
                return 30;
        }

        // 法师学徒（赛琳）：喜欢药水、草药、墨水、羊皮纸、图谱与晶石
        if (who.Name == "赛琳" || who.IsMage())
        {
            if (giftItemId.Contains("药") || giftItemId.Contains("墨") || giftItemId.Contains("纸")
                || giftItemId.Contains("图谱") || giftItemId.Contains("晶石") || giftItemId.Contains("卷"))
                return 30;
        }

        // 粗陋碎石、矿渣、废铁等冷门废料
        if (giftItemId.Contains("石") || giftItemId.Contains("铁") || giftItemId.Contains("矿")
            || giftItemId.Contains("泥") || giftItemId.Contains("渣"))
            return 2;

        // 普遍讨喜礼物（花饰、甜食、佳酿等）
        if (giftItemId.Contains("花") || giftItemId.Contains("酒") || giftItemId.Contains("饰")
            || giftItemId.Contains("果") || giftItemId.Contains("糕") || giftItemId.Contains("糖"))
            return 25;

        return 20;
    }
}
