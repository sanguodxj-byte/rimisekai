using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 领地页签的行走动画：主角前往别房时，格里的王棋沿连通的门一格一格走过去（每格 0.28 秒，落格时轻跳一下）。
/// 状态当场已改好，只是走动期间目标格不画主角那枚棋子，由这枚走动的棋子代替；走完落进目标格的棋位。
/// 移动不盖过渡遮罩（2026-10-08 主人定）。核对模式直接到终态。
/// </summary>
public sealed partial class PortraitHubScreen
{
    private const float WalkStep = 0.28f;

    private PortraitTransition _walk = new();
    private readonly List<Vector2> _walkPoints = new();

    private bool Walking => _walk.Running && _walkPoints.Count >= 2;

    /// <summary>核对用：王棋是否正在走。</summary>
    public bool DebugWalking => Walking;

    /// <summary>主角 from → 沿 path（不含 from，含终点）走动。只在领地页签的本区网格里演。</summary>
    private void StartWalk(int fromRoom, IReadOnlyList<int> path)
    {
        _walkPoints.Clear();
        if (path.Count == 0 || WorldLayer)
            return;
        var from = PlayerSlot(fromRoom, leaving: true);
        if (from == null)
            return;
        _walkPoints.Add(from.Value);
        foreach (var id in path)
        {
            var p = PlayerSlot(id, leaving: false);
            if (p == null)
            {
                _walkPoints.Clear();
                return;
            }
            _walkPoints.Add(p.Value);
        }
        _walk = new PortraitTransition(WalkStep * path.Count);
        _walk.Start();
    }

    /// <summary>主角在某格里的王棋底点：离开的格按出发时人数排（主角居首），途经 / 终点格按到达后人数排。</summary>
    private Vector2? PlayerSlot(int roomId, bool leaving)
    {
        var room = _vm.Rooms().FirstOrDefault(r => r.Id == roomId);
        if (room == null)
            return null;
        var area = PortraitLayout.CellPieces(PortraitLayout.Cell(room.X, room.Y));
        var others = _vm.Cards().Count(c => !c.IsPlayer && c.RoomId == roomId);
        var count = others + 1;
        var (shown, overflow) = PieceSlots(count);
        var slots = overflow ? shown + 1 : shown;
        var start = area.GetCenter().X - (slots - 1) * PortraitLayout.PieceStep / 2f;
        return new Vector2(start, area.End.Y);
    }

    /// <summary>走动中的王棋：段内缓出滑行，每段中点抬起一点。</summary>
    private void DrawWalker()
    {
        if (!Walking)
            return;
        var segs = _walkPoints.Count - 1;
        var t = _walk.T * segs;
        var i = Math.Min(segs - 1, (int)t);
        var u = PortraitMotion.EaseOut(t - i);
        var at = _walkPoints[i].Lerp(_walkPoints[i + 1], u);
        at.Y -= Mathf.Sin(u * Mathf.Pi) * 18f;
        var player = _vm.Cards().FirstOrDefault(c => c.IsPlayer);
        if (player != null)
            InkDraw.Chess(this, at, PortraitLayout.PieceHeight, InkDraw.PieceFor(player));
    }

    private bool StepWalk(float delta)
    {
        var running = _walk.Running;
        return _walk.Step(delta) || running;
    }
}
