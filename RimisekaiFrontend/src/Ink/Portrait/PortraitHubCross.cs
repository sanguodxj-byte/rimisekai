using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 过界（2026-10-08 主人要求）：主角站在有对外通道的房间（Core <c>CrossDir</c> 非空：领地连接点、兴趣点 / 地城的块间边界通道）时，
/// 网格外缘对着该房朝外那条边开一道缺口，缺口里一枚实心三角箭头指向外侧，透明度 1.8 秒一拍淡入淡出；
/// 箭头只画在网格与哥特框之间的边框带及其外侧空白；命中块外沿到最近的邻件、再往门房格内伸凑足 118（只压门房格）。离开该房即不再画。
///
/// 点箭头＝<c>CrossTo</c>（状态当场改好），随后镜头朝箭头方向平移一整屏：旧网格向反方向滑出、新网格自箭头一侧滑入，
/// 两块首尾相接，像同一张大地图上推过去（0.4 秒三次缓出，与抽屉 / 行走同一套缓动）。平移只动网格里的格与门，
/// 用 <see cref="PortraitFrame.SetLayer"/> 叠在当前图层位移上，画完恢复到 <see cref="PortraitFrame.LayerOffset"/>；
/// 滑出网格的部分用底图与边框带原样遮掉。命中块按终态登记，平移期间不收输入；核对模式直接到终态。
/// </summary>
public partial class PortraitHubScreen
{
    private PortraitTransition _crossPan = new(PortraitLayout.CrossPan);
    private Territory.RegionDir _crossDir;
    private int _crossFromRegion;
    private List<Room> _crossFromRooms = new();
    private float _crossBreath;
    private bool _crossArrowShown;

    private bool Crossing => _crossPan.Running;

    /// <summary>核对用：边框通道箭头此刻是否画着。</summary>
    public bool DebugCrossArrow => _crossArrowShown;

    /// <summary>边框缺口＋呼吸箭头＋命中块（只在本区网格、主角不在走动时）。</summary>
    private void DrawCrossArrow()
    {
        var hub = _vm.Hub;
        if (Walking || hub.CrossDir(hub.PlayerRoomId) is not { } dir)
            return;
        var room = hub.State.Territory.Room(hub.PlayerRoomId)!;
        var cell = PortraitLayout.Cell(room.X, room.Y);
        var gap = PortraitLayout.CrossGap(dir, cell);
        var hit = PortraitLayout.CrossHit(dir, cell);
        var outward = PortraitLayout.CrossOutward(dir);
        var along = new Vector2(-outward.Y, outward.X);

        if (PortraitFrame.IsPressed(hit))
            PortraitFrame.PressMark(this, hit);
        // 缺口：抹掉框线两道线，两端各一道骨白门槛（与房间之间的门洞同一画法）。
        DrawRect(gap, InkStyle.Bg);
        var mouth = PortraitLayout.CrossMouth(dir, cell);
        var near = mouth + outward * 2f;
        var far = mouth + outward * (PortraitLayout.MapGrid.Position.X - PortraitLayout.MapFrame.Position.X + 3f);
        foreach (var side in new[] { -1f, 1f })
        {
            var shift = along * (side * PortraitLayout.CrossGapSpan / 2f);
            DrawLine(near + shift, far + shift, InkStyle.Line, 3f);
        }

        var alpha = PortraitMotion.Instant ? 1f
            : 0.3f + 0.7f * (0.5f + 0.5f * Mathf.Cos(Mathf.Tau * _crossBreath / PortraitLayout.CrossBreath));
        var foot = mouth + outward * PortraitLayout.CrossArrowInset;
        DrawColoredPolygon(new[]
        {
            foot + along * (PortraitLayout.CrossArrowBase / 2f),
            foot + outward * PortraitLayout.CrossArrowDepth,
            foot - along * (PortraitLayout.CrossArrowBase / 2f),
        }, new Color(InkStyle.Line, alpha));

        var target = hub.CrossTargetRegion(hub.PlayerRoomId);
        _widgets.Add(new PortraitWidget(hit, PortraitAction.CrossGate, target, true, "过界"));
        _crossArrowShown = true;
    }

    /// <summary>点箭头：先记下旧网格（地城迷雾下的格名照旧），过界，再起镜头平移。</summary>
    private void StartCross(int target)
    {
        var hub = _vm.Hub;
        var dir = hub.CrossDir(hub.PlayerRoomId)!.Value;
        var fromRegion = hub.RegionId;
        var fromRooms = _vm.Rooms().ToList();
        if (!hub.CrossTo(target))
            return;
        _crossDir = dir;
        _crossFromRegion = fromRegion;
        _crossFromRooms = fromRooms;
        _crossPan.Start();
    }

    /// <summary>
    /// 平移中的网格：哥特框照常（底与饰件），旧区在 −方向 × 进度、新区在 +方向 × (1−进度) 各画一遍，
    /// 新区的格照常登记命中块（落在终态位置）；再把网格外的部分用底图、边框带与框线原样盖回去。
    /// </summary>
    private void DrawCrossPan()
    {
        var frame = PortraitLayout.MapFrame;
        var grid = PortraitLayout.MapGrid;
        PortraitFrame.GothicFrame(this, frame, InkStyle.Bg);

        var outward = PortraitLayout.CrossOutward(_crossDir);
        var span = outward.X != 0f ? grid.Size.X : grid.Size.Y;
        var e = _crossPan.Eased;
        var layer = PortraitFrame.LayerOffset;

        PortraitFrame.SetLayer(this, layer - outward * span * e);
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
            {
                var cx = x;
                var cy = y;
                DrawCell(x, y, _crossFromRooms.Find(r => r.X == cx && r.Y == cy), false);
            }
        DrawDoors(PortraitLayout.Cell, _crossFromRegion);

        PortraitFrame.SetLayer(this, layer + outward * span * (1f - e));
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
                DrawCell(x, y, _vm.RoomAt(x, y), true);
        DrawDoors(PortraitLayout.Cell, _vm.Hub.RegionId);
        PortraitFrame.SetLayer(this, layer);

        // 遮边：网格四周的画布原样重铺底图，框内边框带补回黑底，再描两道框线与上下框珠。
        var frameShape = PortraitFrame.ChamferPoints(frame, 22f);
        foreach (var part in new[]
        {
            new Rect2(0f, 0f, PortraitLayout.CanvasWidth, grid.Position.Y),
            new Rect2(0f, grid.End.Y, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - grid.End.Y),
            new Rect2(0f, grid.Position.Y, grid.Position.X, grid.Size.Y),
            new Rect2(grid.End.X, grid.Position.Y, PortraitLayout.CanvasWidth - grid.End.X, grid.Size.Y),
        })
        {
            GothicArt.BackdropPart(this, part);
            var corners = new[] { part.Position, new Vector2(part.End.X, part.Position.Y), part.End, new Vector2(part.Position.X, part.End.Y) };
            foreach (var band in Geometry2D.IntersectPolygons(frameShape, corners))
                DrawColoredPolygon(band, InkStyle.Bg);
        }
        PortraitFrame.GothicFrameLines(this, frame);
        PortraitFrame.FrameJewel(this, new Vector2(frame.GetCenter().X, frame.Position.Y));
        PortraitFrame.FrameJewel(this, new Vector2(frame.GetCenter().X, frame.End.Y));
    }

    /// <summary>推进平移；箭头画着时推进呼吸（核对模式定格在最亮）。本帧需要重画时返回 true。</summary>
    private bool StepCross(float delta)
    {
        var running = _crossPan.Running;
        var redraw = _crossPan.Step(delta) || running;
        if (_crossArrowShown && !PortraitMotion.Instant)
        {
            _crossBreath = (_crossBreath + delta) % PortraitLayout.CrossBreath;
            redraw = true;
        }
        return redraw;
    }
}
