using Godot;
using Rimisekai.Save;

namespace Rimisekai.Flow;

/// <summary>
/// 顶层阶段。对应 Emuera 的 BEGIN SHOP / BEGIN TRAIN 切换，但收成显式状态。
/// </summary>
public enum FlowPhase
{
    Title,
    Hub,
    Quest,
    Combat,
}

/// <summary>
/// 游戏流程节点。只持有 GameState 和当前阶段，不创建 UI。
/// 具体画面以后挂到 PhaseChanged 上。
/// </summary>
public partial class GameFlow : Node
{
    public GameState State { get; private set; } = new();
    public FlowPhase Phase { get; private set; } = FlowPhase.Title;

    [Signal]
    public delegate void PhaseChangedEventHandler(int phase);

    public void Enter(FlowPhase phase)
    {
        Phase = phase;
        EmitSignal(SignalName.PhaseChanged, (int)phase);
    }

    /// <summary>从一份已经建好的世界开局。给带内容数据的入口用。</summary>
    public void Start(GameState state)
    {
        State = state;
        Enter(FlowPhase.Hub);
    }

    public void CloseDay()
    {
        if (Phase != FlowPhase.Hub)
            return;
        State.EndDay();
        Enter(FlowPhase.Hub);
    }
}
