using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Command;

namespace Rimisekai.Session;

/// <summary>
/// era 的 TRAIN 循环：显示 → 输入指令 → 前置 → 执行 → 结算 → 直到退出。
/// 据点、任务、战斗都是这个循环的不同宿主，不共用一套指令表。
/// </summary>
public abstract class Session : ICommandHost
{
    public abstract SessionKind Kind { get; }
    public Roster Roster { get; }
    public bool Exited { get; private set; }
    public List<int> CommandLog { get; } = new();

    protected readonly Dictionary<int, CommandDef> Commands = new();
    protected readonly Dictionary<string, ICommandEffect> Effects = new();

    protected Session(Roster roster) => Roster = roster;

    public void Register(CommandDef def) => Commands[def.Id] = def;

    public void Register(ICommandEffect effect) => Effects[effect.EffectId] = effect;

    public IReadOnlyCollection<CommandDef> Available() => Commands.Values;

    public CommandResult Submit(CommandRequest request)
    {
        if (Exited)
            return CommandResult.Rejected;
        if (!Commands.TryGetValue(request.CommandId, out var def))
            return CommandResult.Rejected;
        if (def.Context != Context || !CanRun(def, request))
            return CommandResult.Rejected;
        if (!Effects.TryGetValue(def.EffectId, out var effect))
            return CommandResult.Rejected;

        var before = OnBefore(def, request);
        if (before == CommandResult.Rejected)
            return CommandResult.Rejected;

        var result = effect.Apply(request, this);
        if (result == CommandResult.Rejected)
            return CommandResult.Rejected;

        CommandLog.Add(def.Id);
        SpendMinutes(def.CostMinutes);
        OnAfter(def, request);

        if (result == CommandResult.ExitSession)
            Exit();
        return result;
    }

    public void SpendMinutes(int minutes) => OnSpend(minutes);

    public void Exit() => Exited = true;

    protected abstract CommandContext Context { get; }

    protected virtual bool CanRun(CommandDef def, CommandRequest request) => true;

    protected virtual CommandResult OnBefore(CommandDef def, CommandRequest request) =>
        CommandResult.Executed;

    protected virtual void OnAfter(CommandDef def, CommandRequest request) { }

    protected virtual void OnSpend(int minutes) { }
}

public enum SessionKind
{
    Hub,
    Quest,
    Combat,
}
