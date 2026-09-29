using Godot;
using CoreSession = Rimisekai.Session.Session;

namespace Rimisekai.SessionHost;

/// <summary>
/// 会话在场景树上的壳。逻辑在 Core 的 Session，这里只负责挂上和卸下。
/// </summary>
public partial class SessionHost : Node
{
    public CoreSession? Current { get; private set; }

    [Signal]
    public delegate void SessionEndedEventHandler(int kind);

    public void Open(CoreSession session)
    {
        Current = session;
    }

    public void Close()
    {
        if (Current == null)
            return;
        var kind = Current.Kind;
        Current.Exit();
        Current = null;
        EmitSignal(SignalName.SessionEnded, (int)kind);
    }
}
