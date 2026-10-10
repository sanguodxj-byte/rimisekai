using Rimisekai.Defs;

namespace Rimisekai.Housing.StateMachine;

/// <summary>
/// 角色行为状态机节点接口。
/// 每个状态独立负责自身进入、每步更新（Tick）、退出清理、快照文字生成与打断判定。
/// </summary>
public interface IWorkerState
{
    /// <summary>对应的 ActionKind 目标枚举，与原有目标体系对齐。</summary>
    ActionKind Goal { get; }

    /// <summary>状态对应的 Def 定义（若有）。</summary>
    JobDef? JobDef { get; }

    /// <summary>进入状态时的初始化（如占座、规划路径、重置计时器）。</summary>
    void Enter(WorkerContext ctx);

    /// <summary>每步时间推进时的逻辑执行。返回是否已完成当前状态。</summary>
    bool Tick(WorkerContext ctx);

    /// <summary>退出状态时的清理工作（如释放设施占用、结算进度）。</summary>
    void Exit(WorkerContext ctx);

    /// <summary>生成角色当前行为的一行快照描述，直接服务于每步覆盖式日志。</summary>
    string Describe(WorkerContext ctx);

    /// <summary>当前状态是否允许被更高优先级的新行为（如突发求聊、火灾、疲惫）打断。</summary>
    bool CanInterrupt(WorkerContext ctx);
}
