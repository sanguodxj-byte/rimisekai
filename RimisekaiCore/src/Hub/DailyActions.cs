using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Housing;

namespace Rimisekai.Hub;

/// <summary>
/// 房间级行动。站在屋里没坐上设施时能做的只有观察——
/// 具体的日常（睡觉/吃饭/洗澡…）都要坐到对应设施上，见 <see cref="HubSession.ActAtFixture"/>。
/// </summary>
public enum PlaceAction
{
    /// <summary>观察四周。房间级唯一的行动。</summary>
    Observe,
}

public sealed partial class HubSession
{
    public const int RestMinutes = 60;

    /// <summary>设施级行动的默认耗时（格，1 格 = 5 分钟）。</summary>
    public const int FacilityActionTicks = 1;

    public bool Act(PlaceAction action, int minutes = 0)
    {
        if (PlayerRoomId < 0 || action != PlaceAction.Observe)
            return false;
        PassTime(CostObserve * TerritoryClock.StepMinutes);
        var room = Room(PlayerRoomId);
        Write(room == null ? "你环顾四周。" : $"你打量着{room.Name}。");
        return true;
    }

    /// <summary>
    /// 设施级行动：坐在某件设施上时，做它支持的一项日常。
    /// 必须已经在用这件设施（<see cref="UsingFixtureId"/>），且该设施确实支持这个行动，
    /// 否则拒绝。设施支持哪些行动由内容包声明，见 Facility.Actions。
    /// </summary>
    public bool ActAtFixture(ActionKind action, int minutes = 0)
    {
        var fixture = Fixture(UsingFixtureId ?? -1);
        if (fixture == null || !fixture.Supports(action))
            return false;

        // 干活：行动就是设施用途推导出的那项工作行动，产出/扣料与 NPC 同一条实现。
        if (ActionKindMap.IsWork(action))
        {
            PassTime(ActionKindMap.Ticks(action) * TerritoryClock.StepMinutes);
            WorkAt(fixture, action);
            return true;
        }

        // 存取/管理存货：打开该设施的交互界面，不消耗时间。
        if (action == ActionKind.Store)
        {
            if (!fixture.CanStore)
                return false;
            Write($"你打开了{fixture.Name}。");
            return OpenStorage(fixture.Id);
        }

        // 吃饭要先确认有东西可吃——没有就不算做了这个动作，也不推进时间。
        if (action == ActionKind.Meal && PickFood() == null)
        {
            Write("没有可以吃的东西。");
            return false;
        }

        PassTime(minutes > 0 ? minutes : FacilityActionTicks * TerritoryClock.StepMinutes);
        var master = State.Roster.Master;
        switch (action)
        {
            case ActionKind.Sleep:
                master?.Condition.Recover(80, 80, clearFatigue: true);
                master?.Condition.RecoverMana(50);
                Write($"你在{fixture.Name}上睡了一觉。");
                break;
            case ActionKind.Rest:
                master?.Condition.Recover(50, 50, clearFatigue: true);
                master?.Condition.RecoverMana(50);
                Write($"你在{fixture.Name}上休息了一会儿。");
                break;
            case ActionKind.Bathe:
                master?.Condition.Recover(30, 40, clearFatigue: false);
                Write($"你在{fixture.Name}洗了澡。");
                break;
            case ActionKind.Meal:
                EatAt(fixture);
                break;
            default:
                Write($"你在{fixture.Name}{InkVerb(action)}。");
                break;
        }
        return true;
    }

    /// <summary>
    /// 玩家在设施上干活。干什么由设施用途推导：
    /// 采集类（挖矿/伐木/取水/耕作/饲养）直接出产，制作类（锻造/木工/缝纫/工艺/炼金/烹饪/表演）走配方。
    /// 与 NPC 的工作路径共用同一套行动定义，只是玩家亲手做一份。
    /// </summary>
    private void WorkAt(Facility fixture, ActionKind act)
    {
        var master = State.Roster.Master;
        if (master == null)
            return;
        if (ActionKindMap.IsExtractive(act))
        {
            // 采集：产量按技能算，与 NPC 同口径。
            var amount = System.Math.Clamp(System.Math.Max(1, master.Life(ActionKindMap.SkillOf(act)!.Value)) / 40, 1, 4);
            if (fixture.YieldItemId.Length > 0)
                State.Territory.Produce(master, fixture.YieldItemId, amount);
            master.GainLifeExp(ActionKindMap.SkillOf(act)!.Value, Territory.GatherExp);
            master.Condition.Spend(0, 0, Traits.ScaledFatigue(master, 5));
            master.Condition.Apply(master);
            Write($"你在{fixture.Name}{ActionKindMap.LabelOf(act)}，得到{fixture.YieldItemId}×{amount}。");
            return;
        }

        // 制作：找这件设施上的配方，扣料出成品。
        var recipe = State.Territory.Recipes.Find(r => r.Station == act);
        if (recipe == null || !State.Territory.CanPayWith(master, recipe.Costs))
        {
            Write($"{fixture.Name}上暂时没有能做的活。");
            return;
        }
        State.Territory.PayWith(master, recipe.Costs);
        master.Bag.Add(recipe.ItemId, recipe.OutputCount);
        master.GainLifeExp(ActionKindMap.SkillOf(act)!.Value, Territory.CraftExp);
        Write($"你在{fixture.Name}{ActionKindMap.LabelOf(act)}，做成{recipe.ItemId}×{recipe.OutputCount}。");
    }

    /// <summary>
    /// 在设施上吃饭。调用前已确认有食物（见 ActAtFixture 的前置检查），
    /// 这里只管扣料与结算，不再有“没食物也吃”的退路。
    /// </summary>
    private void EatAt(Facility fixture)
    {
        var master = State.Roster.Master;
        if (master == null)
            return;
        var food = State.Territory.ConsumeFood(master, fixture.RoomId)!;
        master.Condition.Recover(80, 60, false);
        // 坐在没有桌子的房间里吃，等于将就一顿。
        var table = State.Territory.Facilities.Exists(f => f.Built && f.RoomId == fixture.RoomId && f.IsTable);
        if (!table)
            master.Affect.AddMood(-3);
        Write($"你在{fixture.Name}吃了{food}。");
    }

    /// <summary>玩家当前够得着的食物：背包里，或所在房间设施的存货里。没有返回 null。</summary>
    private string? PickFood()
    {
        var master = State.Roster.Master;
        if (master == null || PlayerRoomId < 0)
            return null;
        return State.Territory.FindFoodIn(master, PlayerRoomId);
    }

    /// <summary>设施行动的动词，用于日志文案。</summary>
    private static string InkVerb(ActionKind action) => action switch
    {
        ActionKind.Drink => "喝了一杯",
        ActionKind.Read => "读了会儿书",
        ActionKind.Pray => "祈祷了片刻",
        ActionKind.Train => "操练了一阵",
        ActionKind.Meditate => "静坐冥想",
        ActionKind.Watch => "看了会儿戏",
        ActionKind.Stargaze => "看了一会儿星星",
        ActionKind.Lookout => "眺望远方",
        ActionKind.Trade => "摆起了摊",
        ActionKind.Store => "整理了东西",
        ActionKind.Tend => "照看了牲口",
        _ => "待了一会儿",
    };
}
