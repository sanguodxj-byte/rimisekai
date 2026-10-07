using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Voice;

/// <summary>
/// 场景事件的一次运行结果。宿主据此呈现与结算。
/// </summary>
public sealed class SceneRun
{
    /// <summary>正在跑的事件。</summary>
    public required SceneEvent Event { get; init; }

    /// <summary>参与的角色。</summary>
    public required CharacterState Character { get; init; }

    /// <summary>已推进到的步骤下标。</summary>
    public int StepIndex { get; private set; }

    /// <summary>当前步骤内已呈现的行下标（含台词与旁白）。</summary>
    public int LineIndex { get; private set; }

    /// <summary>本次运行里玩家选过的选项，按步骤顺序。</summary>
    public List<string> Chosen { get; } = new();

    /// <summary>当前正在等玩家选。为真时 <see cref="SceneEvent.Choices"/> 才是有效选项。</summary>
    public bool Waiting { get; private set; }

    public bool Finished { get; private set; }

    /// <summary>已经跑过的事件步骤。</summary>
    public SceneStep? Current =>
        StepIndex >= 0 && StepIndex < Event.Steps.Count ? Event.Steps[StepIndex] : null;

    /// <summary>当前步骤的可选分支。不在等待时为空。</summary>
    public IReadOnlyList<SceneChoice> Choices =>
        Waiting && Current != null ? Current.Choices : System.Array.Empty<SceneChoice>();

    public void Wait() => Waiting = true;

    public void Choose(string choiceId)
    {
        Chosen.Add(choiceId);
        Waiting = false;
    }

    /// <summary>推进一行。返回 false 表示本步骤的行已放完。</summary>
    public bool NextLine()
    {
        var step = Current;
        if (step == null)
            return false;
        if (LineIndex + 1 >= step.Lines.Count)
            return false;
        LineIndex++;
        return true;
    }

    /// <summary>进入下一步。返回 false 表示事件已结束。</summary>
    public bool NextStep()
    {
        // 收束步：本步行完即收演，不顺流到下一步（分支各自收尾）。
        if (Current is { End: true })
        {
            Finished = true;
            return false;
        }
        StepIndex++;
        LineIndex = 0;
        Waiting = false;
        if (StepIndex >= Event.Steps.Count)
        {
            Finished = true;
            return false;
        }
        return true;
    }

    /// <summary>
    /// 跳到指定步骤（分支选项用）。返回 false 表示目标越界，事件应结束。
    /// 与 NextStep 的区别：跳转不回绕，也不受"顺序"约束。
    /// </summary>
    public bool JumpTo(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= Event.Steps.Count)
        {
            Finished = true;
            return false;
        }
        StepIndex = stepIndex;
        LineIndex = 0;
        Waiting = false;
        return true;
    }

    /// <summary>把当前步骤的文本按顺序摊平，供宿主一次性呈现。</summary>
    public IReadOnlyList<SceneText> Lines()
    {
        var step = Current;
        if (step == null)
            return System.Array.Empty<SceneText>();
        return step.Lines;
    }
}

/// <summary>
/// 场景事件里的一行。要么是角色说的话，要么是旁白。
///
/// 正文有两种来源：<see cref="Text"/> 是内容表里写死的静态文本；
/// <see cref="Generation"/> 非空则表示这一行由 LLM 生成——静态文本可以留空
/// （纯生成行，没有兜底），也可以写上作内容表的参考。
/// </summary>
public readonly record struct SceneText(
    VoiceKind Kind,
    string Speaker,
    string Text,
    VoiceGeneration? Generation = null)
{
    /// <summary>本行是否要靠生成才有正文。</summary>
    public bool NeedsGeneration => Generation != null;
}

/// <summary>
/// 一个分支选项。选中后跳到 <see cref="GotoStep"/>，并施加 <see cref="Effects"/>。
/// GotoStep 为 -1 表示继续往下走。
/// </summary>
public sealed class SceneChoice
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public int GotoStep { get; init; } = -1;
    public List<SceneEffect> Effects { get; init; } = new();

    /// <summary>选中本项才为真的条件。为假时该选项不出现。</summary>
    public VoiceGate? Gate { get; init; }
}

/// <summary>
/// 一个步骤。先按顺序放完 <see cref="Lines"/>，
/// 有 <see cref="Choices"/> 就停下等玩家选，否则自动进入下一步。
/// </summary>
public sealed class SceneStep
{
    /// <summary>步骤标识，供选项跳转引用。</summary>
    public string Id { get; init; } = "";

    public List<SceneText> Lines { get; init; } = new();

    public List<SceneChoice> Choices { get; init; } = new();

    /// <summary>进入本步骤时施加的效果（选项效果之外的那部分）。</summary>
    public List<SceneEffect> Effects { get; init; } = new();

    /// <summary>本步骤只在满足条件时才会被执行；不满足则跳过。</summary>
    public VoiceGate? Gate { get; init; }

    /// <summary>
    /// 收束步：本步的行走完就收演，不再顺流到下一步。
    /// 分支各自收尾（接受/婉拒各一段）时用，避免落到隔壁分支的结尾。
    /// </summary>
    public bool End { get; init; }
}

/// <summary>
/// 场景事件能造成的一种变化。做成数据而不是回调，
/// 这样内容包写的事件不需要编译进代码。
/// </summary>
public sealed class SceneEffect
{
    public SceneEffectKind Kind { get; init; }

    /// <summary>数值：好感、心情、体力的增量；或标志位/计数器的目标值。</summary>
    public int Amount { get; init; }

    /// <summary>物品 Id（给物、扣物）。</summary>
    public string ItemId { get; init; } = "";

    /// <summary>标志名（置位、清位、计数器）。</summary>
    public string Flag { get; init; } = "";

    /// <summary>素质（授予、移除）。</summary>
    public string Trait { get; init; } = "";

    /// <summary>关系旗标（建立、解除）。</summary>
    public string Relation { get; init; } = "";

    /// <summary>写进日志的一行。留空则不写。</summary>
    public string Log { get; init; } = "";
}

/// <summary>场景事件能造成的效果种类。</summary>
public enum SceneEffectKind
{
    None = 0,

    /// <summary>好感增减。</summary>
    Favor,

    /// <summary>心情增减。</summary>
    Mood,

    /// <summary>体力增减。</summary>
    Stamina,

    /// <summary>气力增减。</summary>
    Spirit,


    /// <summary>给物品。</summary>
    GiveItem,

    /// <summary>扣物品。</summary>
    TakeItem,

    /// <summary>置标志位为 1。</summary>
    SetFlag,

    /// <summary>清标志位。</summary>
    ClearFlag,

    /// <summary>把标志位设成指定值（当计数器用）。</summary>
    SetCounter,

    /// <summary>给素质。</summary>
    GrantTrait,

    /// <summary>移除素质。</summary>
    RemoveTrait,

    /// <summary>建立关系。</summary>
    AddRelation,

    /// <summary>解除关系。</summary>
    RemoveRelation,

    /// <summary>写一行日志。</summary>
    Log,
}
