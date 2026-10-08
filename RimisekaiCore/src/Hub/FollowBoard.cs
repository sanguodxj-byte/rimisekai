using System;
using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;
using Rimisekai.Housing.StateMachine;
using Rimisekai.Housing.StateMachine.States;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

/// <summary>
/// 跟随系统的会话侧挂点：同床的好感判定、跟随者带来的交易加成。
/// 跟随本体（移动、共坐、等待）在 <see cref="Housing.StateMachine.States.FollowingState"/>
/// 与 <see cref="TerritoryClock"/> 的跟随节律里。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>同床的好感门槛。比亲吻（600）更高：一起过夜是更亲近的事。</summary>
    public const int FavorCoSleep = 800;

    /// <summary>退出跟随：收掉当前活动，人回自己的日子。日志由调用方写。</summary>
    private void EndFollow(Worker worker)
    {
        worker.FollowsPlayer = false;
        Day.EndRoutineOf(worker.CharacterId);
    }

    /// <summary>某人此刻是否正跟着你。交谈面板的“邀请”位据此换成“分开”。</summary>
    public bool IsFollowing(int characterId)
    {
        foreach (var worker in Day.Workers)
        {
            if (worker.CharacterId == characterId && worker.FollowsPlayer)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 跟随的持续资格：接受邀请的条件没了（非女仆的好感跌出“好感”档），
    /// 跟随就到此为止。时间每推一格都查一遍；同床被拒这类即时退出在各自动作里结算，不走这里。
    /// </summary>
    private void SweepBrokenFollows()
    {
        foreach (var worker in Day.Workers)
        {
            if (!worker.FollowsPlayer)
                continue;
            var who = State.Roster.Find(worker.CharacterId);
            if (who == null || who.AcceptsInvite() || OnQuestParty(who.Id))
                continue;
            EndFollow(worker);
            Write($"{who.Name}不再跟着你了。");
        }
    }

    /// <summary>
    /// 玩家在床上睡下时，同房的跟随者能否跟着躺上来：
    /// 双人床（容量 ≥ 2）且好感判定通过。单人床没有位置，只会在旁边等着；
    /// 判定不过就是躲开了——跟随是“愿不愿意亲近”的事，躲开即不再跟着。
    /// </summary>
    private void CoSleep(Facility bed)
    {
        foreach (var worker in Day.Workers)
        {
            if (!worker.FollowsPlayer || worker.Goal == ActionKind.Sleep)
                continue;
            var who = State.Roster.Find(worker.CharacterId);
            if (who == null || worker.RoomId != PlayerRoomId)
                continue;
            // 床位：玩家自己占一个，数一数已经躺下的跟随者，满了就不再上人。
            var taken = 1;
            foreach (var other in Day.Workers)
            {
                if (other.CharacterId != worker.CharacterId
                    && other.FacilityId == bed.Id && other.Goal == ActionKind.Sleep)
                    taken++;
            }
            if (bed.Capacity <= 1 || taken >= bed.Capacity)
                continue;
            if (!RequireFavor(who, FavorCoSleep))
            {
                Write($"{who.Name}在床边守候着你。");
                continue;
            }
            worker.FacilityId = bed.Id;
            worker.Goal = ActionKind.Sleep;
            worker.Task = ActionKind.None;
            worker.Progress = 0;
            worker.Path.Clear();
            worker.Phase = WorkPhase.Working;
            who.Condition.Recover(80, 80);
            who.Condition.ChangeIntoDryClothes();
            worker.StateMachine.TransitionTo(new SleepingState(), new WorkerContext
            {
                Territory = State.Territory,
                Character = who,
                Worker = worker,
                UsedFacilities = new HashSet<int>(),
                Rng = Day.Rng,
            });
            Write($"{who.Name}和你一起睡下了。");
            TryPlayCoSleepScene(who, bed);
        }
    }

    /// <summary>
    /// 同床共寝特定语义场景：床铺双人、满好感、满级、从战斗胜利归来等严苛条件全部满足时精确触发。
    /// 全存档终身仅演一次。
    /// </summary>
    private void TryPlayCoSleepScene(CharacterState who, Facility bed)
    {
        if (ScenePlaying)
            return;

        var ctx = CreateVoiceContext(who, VoiceTrigger.Sleep, "", VoiceActivity.Sleeping, VoiceRole.Actor, VoicePlace.Before, null, PlayerRoomId, bed.Id);
        var run = new SceneRunner(State.Voice.Scenes, State.Territory).Begin(who, ctx);
        if (run != null)
        {
            CloseOverlay();
            State.FiredEvents.Add(run.Event.Id);
            Scene = run;
            SceneActor = who;
            _sceneRunner = new SceneRunner(State.Voice.Scenes, State.Territory);
            PresentScene();
        }
    }

    /// <summary>跟随者交易加成的上限（百分比）。</summary>
    public const int TradeBonusCap = 15;

    /// <summary>
    /// 眼下跟着的同伴里最好的还价本领（百分比）：
    /// 魅力与社交本领折半取一。交易技能没有单列，社交（表演、招待同一项）就是这项本领。
    /// 没人跟随就是 0，报价原样。
    /// </summary>
    public int TradeBonusPercent()
    {
        var best = 0;
        foreach (var worker in Day.Workers)
        {
            if (!worker.FollowsPlayer)
                continue;
            var who = State.Roster.Find(worker.CharacterId);
            if (who == null)
                continue;
            var percent = (who[CoreStat.Charm] + who.Life(LifeSkill.Social)) / 2;
            best = Math.Max(best, Math.Clamp(percent, 0, TradeBonusCap));
        }
        return best;
    }

    /// <summary>
    /// 集市行情的跟随加成：买入往下磨，卖出往上抬。
    /// 界面显示与成交结算共用这一个口径，画出来的价就是付的价。
    /// </summary>
    public int TradePrices(Territory.MarketListing listing, bool selling)
    {
        var p = TradeBonusPercent();
        return selling
            ? Math.Max(1, (listing.SellPrice * (100 + p) + 50) / 100)
            : Math.Max(1, listing.BuyPrice * (100 - p) / 100);
    }
}
