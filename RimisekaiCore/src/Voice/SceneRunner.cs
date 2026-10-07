using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Voice;

/// <summary>
/// 场景事件的执行器。负责施加效果、推进步骤、跑状态机。
///
/// 与 <see cref="VoiceDirector"/> 的分工：Director 管"说哪句话"，
/// SceneRunner 管"演哪一段、这一段改变什么"。两者共用门槛与上下文。
/// </summary>
public sealed class SceneRunner
{
    private readonly SceneLibrary _library;
    private readonly Territory _territory;

    public SceneRunner(SceneLibrary library, Territory territory)
    {
        _library = library;
        _territory = territory;
    }

    public SceneLibrary Library => _library;

    /// <summary>
    /// 试试给这个角色开一段场景事件。挑不出可触发的事件就返回 null，
    /// 宿主什么也不做（不发事件、不扣时间）。
    ///
    /// 一旦开演就立刻记冷却，避免同一天里反复触发同一段；
    /// 状态机标志留到跑完再置，这样中途中断的事件下次还能接着讲。
    /// </summary>
    /// <summary>
    /// 指名开演：事件板等调用方直接指定要演哪段，
    /// 不走库挑选。效果与冷却口径与 Begin 完全一致。
    /// </summary>
    public SceneRun? BeginEvent(SceneEvent scene, CharacterState character, VoiceContext ctx)
    {
        Apply(character, scene.Effects, ctx);
        character.Voice.SceneLastDay[scene.Id] = ctx.Day;

        var run = new SceneRun { Event = scene, Character = character };
        Apply(character, run.Current?.Effects, ctx);
        return run;
    }

    public SceneRun? Begin(CharacterState character, VoiceContext ctx)
    {
        var scene = _library.Pick(character, ctx);
        if (scene == null)
            return null;

        Apply(character, scene.Effects, ctx);
        character.Voice.SceneLastDay[scene.Id] = ctx.Day;

        var run = new SceneRun { Event = scene, Character = character };
        Apply(character, run.Current?.Effects, ctx);
        return run;
    }

    /// <summary>
    /// 跑完一步：施加该步效果，然后推进到下一步。
    /// 返回 false 表示事件已结束，宿主应做收尾结算。
    /// </summary>
    public bool Advance(SceneRun run, VoiceContext ctx)
    {
        var current = run.Current;
        if (current != null)
            Apply(run.Character, current.Effects, ctx);

        if (run.NextStep())
        {
            Apply(run.Character, run.Current?.Effects, ctx);
            return true;
        }

        Finish(run, ctx);
        return false;
    }

    /// <summary>
    /// 玩家选了一个分支。返回是否接受。
    /// 接受后施加选项效果并按需跳步；跳转后事件可能直接结束。
    /// </summary>
    public bool Choose(SceneRun run, string choiceId, VoiceContext ctx)
    {
        var current = run.Current;
        if (current == null || !run.Waiting)
            return false;

        SceneChoice? picked = null;
        foreach (var choice in current.Choices)
        {
            if (choice.Id == choiceId)
                picked = choice;
        }
        if (picked == null)
            return false;
        // 门槛不过的选项不该出现，也不该被选中。
        if (picked.Gate != null && !picked.Gate.Allows(ctx, picked.Id))
            return false;

        run.Choose(choiceId);
        Apply(run.Character, picked.Effects, ctx);

        if (picked.GotoStep >= 0)
        {
            // 跳转：直接换步，不走 Advance 的自然推进。
            if (!run.JumpTo(picked.GotoStep))
                Finish(run, ctx);
        }
        return true;
    }

    /// <summary>事件收尾：跑状态机，记录冷却。</summary>
    private void Finish(SceneRun run, VoiceContext ctx)
    {
        var scene = run.Event;
        var character = run.Character;
        if (scene.Flag.Length > 0)
            character.Set(character.Flags, SceneLibrary.FlagKey(scene.Flag), scene.DoneValue);
        character.Voice.SceneLastDay[scene.Id] = ctx.Day;
    }

    /// <summary>施加一组效果。</summary>
    public void Apply(CharacterState character, List<SceneEffect>? effects, VoiceContext ctx)
    {
        if (effects == null || effects.Count == 0)
            return;

        foreach (var effect in effects)
        {
            switch (effect.Kind)
            {
                case SceneEffectKind.Favor:
                    character.Condition.AddFavor(effect.Amount);
                    break;
                case SceneEffectKind.Mood:
                    character.Affect.AddMood(effect.Amount);
                    break;
                case SceneEffectKind.Stamina:
                    if (effect.Amount >= 0)
                        character.Condition.Recover(effect.Amount, 0);
                    else
                        character.Condition.Spend(-effect.Amount, 0);
                    break;
                case SceneEffectKind.Spirit:
                    if (effect.Amount >= 0)
                        character.Condition.Recover(0, effect.Amount);
                    else
                        character.Condition.Spend(0, -effect.Amount);
                    break;
                case SceneEffectKind.GiveItem:
                    if (effect.ItemId.Length > 0)
                        character.Bag.Add(effect.ItemId, effect.Amount <= 0 ? 1 : effect.Amount);
                    break;
                case SceneEffectKind.TakeItem:
                    if (effect.ItemId.Length > 0)
                        character.Bag.Add(effect.ItemId, -(effect.Amount <= 0 ? 1 : effect.Amount));
                    break;
                case SceneEffectKind.SetFlag:
                    character.Set(character.Flags, SceneLibrary.FlagKey(effect.Flag), 1);
                    break;
                case SceneEffectKind.ClearFlag:
                    character.Set(character.Flags, SceneLibrary.FlagKey(effect.Flag), 0);
                    break;
                case SceneEffectKind.SetCounter:
                    character.Set(character.Flags, SceneLibrary.FlagKey(effect.Flag), effect.Amount);
                    break;
                case SceneEffectKind.GrantTrait:
                    if (System.Enum.TryParse<Trait>(effect.Trait, ignoreCase: true, out var grant))
                        character.Grant(grant);
                    break;
                case SceneEffectKind.RemoveTrait:
                    if (System.Enum.TryParse<Trait>(effect.Trait, ignoreCase: true, out var drop))
                        character.Talents.Remove((int)drop);
                    break;
                case SceneEffectKind.AddRelation:
                    if (ctx.MasterId >= 0
                        && System.Enum.TryParse<RelationFlag>(effect.Relation, ignoreCase: true, out var add))
                    {
                        character.Relations.Add(ctx.MasterId, add);
                    }
                    break;
                case SceneEffectKind.RemoveRelation:
                    if (ctx.MasterId >= 0
                        && System.Enum.TryParse<RelationFlag>(effect.Relation, ignoreCase: true, out var remove))
                    {
                        character.Relations.Remove(ctx.MasterId, remove);
                    }
                    break;
                case SceneEffectKind.Log:
                    // 日志由宿主写（它持有日志缓冲），这里只把文本留在效果里。
                    break;
            }
        }
    }
}
