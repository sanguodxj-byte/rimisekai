using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Command;

/// <summary>
/// 指令在哪个会话里可用。对应 コマンド_日常 / クエスト / 居住区 等。
/// </summary>
public enum CommandContext
{
    Daily = 1,
    Housing = 4,
    Quest = 5,
    Combat = 6,
}

/// <summary>
/// 一条可执行指令的定义。效果由 EffectId 指向，不在这里写具体内容。
/// </summary>
public sealed class CommandDef
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CommandContext Context { get; init; }
    public int CostMinutes { get; init; }
    public string EffectId { get; init; } = "";
}

public sealed class CommandRequest
{
    public int CommandId { get; init; }
    public int ActorId { get; init; }
    public List<int> TargetIds { get; init; } = new();
    public int Option { get; init; }
}

public enum CommandResult
{
    Rejected,
    Executed,
    ExitSession,
}

public interface ICommandEffect
{
    string EffectId { get; }
    CommandResult Apply(CommandRequest request, ICommandHost host);
}

/// <summary>指令执行时能碰到的最小宿主。具体会话去实现。</summary>
public interface ICommandHost
{
    Roster Roster { get; }
    void SpendMinutes(int minutes);
}
