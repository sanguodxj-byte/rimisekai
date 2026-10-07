using System.Collections.Generic;
using Rimisekai.Housing;
using Rimisekai.Save;
using Rimisekai.Voice;

namespace Rimisekai.Hub;

/// <summary>
/// 主界面后端。按界面语义拆成多个 partial 文件，本文件只留核心状态。
/// 只改 GameState，不创建控件。
/// </summary>
public sealed partial class HubSession : IVoiceSink
{
    public GameState State { get; }
    public int PlayerRoomId { get; private set; } = -1;
    public Room? PlayerRoom() => Room(PlayerRoomId);
    public int Hour => State.Clock.Hour;

    private readonly Dictionary<int, int> _presence = new();

    public HubSession(GameState state) => State = state;

    public TerritoryClock Day { get; } = new();

    private Room? Room(int id)
    {
        if (Layer == MapLayer.World)
            return (_worldRooms ??= State.World.ExportTo5x5WorldRooms()).Find(r => r.Id == id);
        return State.Territory.Rooms.Find(r => r.Id == id);
    }

    private Facility? Fixture(int id) => State.Territory.Facilities.Find(f => f.Id == id);

    /// <summary>
    /// 设施是不是干活的地方：声明支持任一工作行动才算，床、浴池这类不算。
    /// 排班页据此决定点它排的是工作还是娱乐；查不到返回 false。
    /// </summary>
    public bool FacilityIsWorkbench(int facilityId)
    {
        var facility = Fixture(facilityId);
        if (facility == null)
            return false;
        foreach (var task in ActionKindMap.WorkOrdered)
        {
            if (facility.Supports(task))
                return true;
        }
        return false;
    }
}
