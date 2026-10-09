using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;
using Rimisekai.WorldMap;

namespace Rimisekai.Portrait;

/// <summary>
/// 世界层：领地页签的地图框里换成生成器的整张大世界（默认 128×128 格），可拖动平移、滚轮 / 按钮缩放。
/// 底图由 <see cref="PortraitWorldAtlas"/> 烘好；聚落、领地、队伍、选中格运行时矢量绘制。全图可见，没有迷雾。
/// 打开时视口以队伍为中心。点任一格弹出地点抽屉：走得过去的格可「前往」（沿最省时的路按地貌耗时逐格走过去），
/// 到聚落即进场、到领地即回家。
/// </summary>
public sealed partial class PortraitHubScreen
{
    private const float WorldZoomMin = 6.7f;
    private const float WorldZoomMax = 56f;
    private const float WorldZoomDefault = 26f;
    private const float WorldSheetTop = 1560f;

    private Vector2 _worldCenter = new(-1f, -1f);
    private float _worldZoom = WorldZoomDefault;
    private Vector2I _worldPick = new(-1, -1);
    private bool _worldPress;
    private bool _worldDrag;
    private Vector2 _worldPressPos;

    private WorldMapData World => _vm.Hub.State.World;

    private bool WorldMapActive => WorldLayer && _tab == 0 && _push == PushPage.None && _sheet == SheetKind.None
        && !_interactionOpen && !_vm.StorageOpen && !ConversationActive;

    /// <summary>核对用：当前视口中心（格坐标）与缩放（每格像素）。</summary>
    public Vector2 DebugWorldCenter => _worldCenter;

    public float DebugWorldZoom => _worldZoom;

    /// <summary>试玩用（只读）：格坐标在画面上的像素中心，供模拟手指去点；视口外返回 null。</summary>
    public Vector2? DebugWorldScreenOf(int x, int y)
    {
        if (!WorldMapActive)
            return null;
        var p = WorldToScreen(new Vector2(x + 0.5f, y + 0.5f));
        return PortraitLayout.MapGrid.Grow(-20f).HasPoint(p) ? p : null;
    }

    /// <summary>核对用：点世界地图上的某格（等同手指轻点）。</summary>
    public void DebugWorldTap(int x, int y)
    {
        PickWorldTile(new Vector2I(x, y));
        QueueRedraw();
    }

    private void CenterWorldOnHome()
    {
        var map = World;
        _worldCenter = map.HasHome ? new Vector2(map.HomeX + 0.5f, map.HomeY + 0.5f) : new Vector2(map.Width / 2f, map.Height / 2f);
        _worldZoom = WorldZoomDefault;
        _worldPick = new Vector2I(-1, -1);
        ClampWorldView();
    }

    /// <summary>视口移到队伍脚下（出行、走完一程后）。</summary>
    private void CenterWorldOnParty()
    {
        var (px, py) = _vm.Hub.WorldPartyPosition;
        _worldCenter = new Vector2(px + 0.5f, py + 0.5f);
        _worldZoom = WorldZoomDefault;
        _worldPick = new Vector2I(-1, -1);
        ClampWorldView();
    }

    private Vector2 WorldSpan => PortraitLayout.MapGrid.Size / _worldZoom;

    private void ClampWorldView()
    {
        var map = World;
        _worldZoom = Mathf.Clamp(_worldZoom, Math.Max(WorldZoomMin, PortraitLayout.MapGrid.Size.X / map.Width), WorldZoomMax);
        var half = WorldSpan / 2f;
        _worldCenter.X = half.X * 2f >= map.Width ? map.Width / 2f : Mathf.Clamp(_worldCenter.X, half.X, map.Width - half.X);
        _worldCenter.Y = half.Y * 2f >= map.Height ? map.Height / 2f : Mathf.Clamp(_worldCenter.Y, half.Y, map.Height - half.Y);
    }

    private Vector2 WorldToScreen(Vector2 tile) =>
        PortraitLayout.MapGrid.Position + (tile - (_worldCenter - WorldSpan / 2f)) * _worldZoom;

    private Vector2 ScreenToWorld(Vector2 screen) =>
        _worldCenter - WorldSpan / 2f + (screen - PortraitLayout.MapGrid.Position) / _worldZoom;

    private void ZoomWorld(float factor, Vector2 anchor)
    {
        var before = ScreenToWorld(anchor);
        _worldZoom *= factor;
        ClampWorldView();
        var after = ScreenToWorld(anchor);
        _worldCenter += before - after;
        ClampWorldView();
    }

    // ---------- 绘制 ----------

    private void DrawWorldMap()
    {
        if (_worldCenter.X < 0f)
            CenterWorldOnParty();
        ClampWorldView();
        var map = World;
        var grid = PortraitLayout.MapGrid;
        var tex = PortraitWorldAtlas.Texture(map);
        var origin = _worldCenter - WorldSpan / 2f;
        var src = new Rect2(origin * PortraitWorldAtlas.TilePx, WorldSpan * PortraitWorldAtlas.TilePx);
        DrawTextureRectRegion(tex, grid, src);

        // 聚落：小的先画，王都最后画在上面；名字按缩放分级出现。地名一律收齐了最后画（压在所有标记之上、不被队伍棋子挡住）。
        _worldLabels.Clear();
        foreach (var poi in map.Pois.OrderBy(p => -(int)p.Type))
        {
            var c = WorldToScreen(new Vector2(poi.X + 0.5f, poi.Y + 0.5f));
            if (!grid.Grow(-6f).HasPoint(c))
                continue;
            DrawPoiMark(poi.Type, c);
            var major = poi.Type is WorldPoiType.Capital or WorldPoiType.Town;
            if (_worldZoom >= (major ? 12f : 22f))
                WorldLabel(c + new Vector2(0f, PoiRadius(poi.Type) + 26f), poi.NameZh, major ? InkStyle.Line : InkStyle.Wood);
        }

        // 领地：双环＋名字常显；队伍在家时环里立王棋，出门在外时环里画城堡。
        var (partyX, partyY) = _vm.Hub.WorldPartyPosition;
        var partyHome = map.HasHome && partyX == map.HomeX && partyY == map.HomeY;
        if (map.HasHome)
        {
            var hc = WorldToScreen(new Vector2(map.HomeX + 0.5f, map.HomeY + 0.5f));
            if (grid.Grow(-6f).HasPoint(hc))
            {
                var r = Mathf.Clamp(_worldZoom * 0.75f, 18f, 40f);
                DrawCircle(hc, r, new Color(0f, 0f, 0f, 0.75f));
                DrawArc(hc, r, 0f, Mathf.Tau, 48, InkStyle.Line, 3f, true);
                DrawArc(hc, r + 7f, 0f, Mathf.Tau, 48, InkStyle.Dim, 1.5f, true);
                if (partyHome)
                    InkDraw.Chess(this, hc + new Vector2(0f, r * 0.62f), r * 1.3f, InkDraw.ChessPiece.King);
                else
                    PortraitGlyph.Castle(this, hc.X, hc.Y, r * 0.7f, InkStyle.Line);
                WorldLabel(hc + new Vector2(0f, r + 30f), TerritoryName(), InkStyle.Line);
            }
        }

        // 队伍：出门在外时在脚下那格立王棋（单环）。
        Rect2? partyMark = null;
        if (!partyHome)
        {
            var pc = WorldToScreen(new Vector2(partyX + 0.5f, partyY + 0.5f));
            if (grid.Grow(-6f).HasPoint(pc))
            {
                var r = Mathf.Clamp(_worldZoom * 0.6f, 16f, 34f);
                DrawCircle(pc, r, new Color(0f, 0f, 0f, 0.75f));
                DrawArc(pc, r, 0f, Mathf.Tau, 48, InkStyle.Line, 3f, true);
                InkDraw.Chess(this, pc + new Vector2(0f, r * 0.62f), r * 1.3f, InkDraw.ChessPiece.King);
                partyMark = new Rect2(pc - Vector2.One * (r + 4f), Vector2.One * (r + 4f) * 2f);
            }
        }
        DrawWorldLabels(partyMark);

        // 选中格：骨白方框＋四角菱。
        if (_worldPick.X >= 0)
        {
            var a = WorldToScreen(new Vector2(_worldPick.X, _worldPick.Y));
            var cell = new Rect2(a, Vector2.One * _worldZoom);
            if (grid.Intersects(cell))
            {
                cell = cell.Intersection(grid);
                InkDraw.Ink(this, RectLoop(cell), InkStyle.Line, 3f);
                foreach (var corner in new[] { cell.Position, new Vector2(cell.End.X, cell.Position.Y), new Vector2(cell.Position.X, cell.End.Y), cell.End })
                    InkDraw.Jewel(this, corner, 7f, InkStyle.Line);
            }
        }

        // 右上：放大 / 缩小；右下：回到领地。
        var zin = new Rect2(grid.End.X - 128f, grid.Position.Y + 8f, 120f, 120f);
        var zout = new Rect2(grid.End.X - 128f, grid.Position.Y + 136f, 120f, 120f);
        var home = new Rect2(grid.End.X - 128f, grid.End.Y - 128f, 120f, 120f);
        WorldButton(zin, PortraitAction.WorldZoomIn, "放大", _worldZoom < WorldZoomMax - 0.01f, (x, y) => PortraitGlyph.Plus(this, x, y, 22f, InkStyle.Line));
        WorldButton(zout, PortraitAction.WorldZoomOut, "缩小", _worldZoom > Math.Max(WorldZoomMin, grid.Size.X / map.Width) + 0.01f,
            (x, y) => PortraitGlyph.Minus(this, x, y, 22f, InkStyle.Line));
        WorldButton(home, PortraitAction.WorldHome, "领地", map.HasHome, (x, y) => PortraitGlyph.Castle(this, x, y, 26f, InkStyle.Line));
        DrawWorldSteps();

    }

    /// <summary>
    /// 方向键（2026-10-09 主人要求；同日改大、再改回融进地图的画法）：与领地过界箭头同一套语汇——
    /// 哥特框四边正中各开一道缺口（沿边 <see cref="StepGap"/>），缺口两端各一道骨白门槛、门槛外端各收一枚小菱；
    /// 缺口里一枚大实心三角朝外，三角底边压进地图 <see cref="StepArrowInset"/>，尖越过框线，读作「从这道门出去」。
    /// 三角先描一圈黑边再填骨白，压在任何地貌上都看得清。没有底板，命中块（<see cref="StepRect"/>）是看不见的大块。
    /// 那边走不过去或遭遇未了结时三角与门槛换暗色、不可点。按下时缺口里亮一层按压色。
    /// </summary>
    private void DrawWorldSteps()
    {
        var hub = _vm.Hub;
        var map = World;
        var grid = PortraitLayout.MapGrid;
        var band = grid.Position.X - PortraitLayout.MapFrame.Position.X + 3f;
        var (px, py) = hub.WorldPartyPosition;
        var busy = hub.PendingEncounter != null;
        foreach (var dir in new[] { Territory.RegionDir.North, Territory.RegionDir.East, Territory.RegionDir.South, Territory.RegionDir.West })
        {
            var (dx, dy) = StepDelta(dir);
            var enabled = !busy && map.IsPassable(px + dx, py + dy);
            var hit = StepRect(dir);
            var outward = PortraitLayout.CrossOutward(dir);
            var along = new Vector2(-outward.Y, outward.X);
            var mouth = grid.GetCenter() + outward * (outward.X != 0f ? grid.Size.X : grid.Size.Y) / 2f;
            var ink = enabled ? InkStyle.Line : InkStyle.WoodDark;

            // 缺口：抹掉框带（底色），按下时叠一层按压色。
            var a = mouth + outward * 2f - along * StepGap / 2f;
            var b = mouth + outward * band + along * StepGap / 2f;
            var gap = new Rect2(new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)), (b - a).Abs());
            DrawRect(gap, InkStyle.Bg);
            if (enabled && PortraitFrame.IsPressed(hit))
                DrawRect(gap, PortraitFrame.PressFill);
            foreach (var side in new[] { -1f, 1f })
            {
                var shift = along * (side * StepGap / 2f);
                DrawLine(mouth + outward * 2f + shift, mouth + outward * band + shift, ink, 3f);
                InkDraw.Jewel(this, mouth + outward * band + shift, 6f, ink);
            }

            var foot = mouth - outward * StepArrowInset;
            var tri = new[]
            {
                foot + along * (StepArrowBase / 2f),
                foot + outward * StepArrowDepth,
                foot - along * (StepArrowBase / 2f),
            };
            var pressed = enabled && PortraitFrame.IsPressed(hit);
            DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0], tri[1] }, new Color(0f, 0f, 0f, 0.85f), 10f, true);
            DrawColoredPolygon(tri, pressed ? InkStyle.Dim : ink);
            _widgets.Add(new PortraitWidget(hit, PortraitAction.WorldStep, (int)dir, enabled, StepName(dir)));
        }
    }

    /// <summary>缺口沿边长、三角底宽 / 高、三角底边压进地图的深度。</summary>
    private const float StepGap = 180f;
    private const float StepArrowBase = 112f;
    private const float StepArrowDepth = 62f;
    private const float StepArrowInset = 26f;

    private const float StepLong = 260f;
    private const float StepDeep = 130f;

    private static Rect2 StepRect(Territory.RegionDir dir)
    {
        var grid = PortraitLayout.MapGrid;
        var cx = grid.GetCenter().X;
        var cy = grid.GetCenter().Y;
        return dir switch
        {
            Territory.RegionDir.North => new Rect2(cx - StepLong / 2f, PortraitLayout.LogPanel.End.Y + 6f, StepLong, StepDeep),
            Territory.RegionDir.South => new Rect2(cx - StepLong / 2f, grid.End.Y - 70f, StepLong, StepDeep),
            Territory.RegionDir.East => new Rect2(PortraitLayout.CanvasWidth - 8f - StepDeep, cy - StepLong / 2f, StepDeep, StepLong),
            _ => new Rect2(8f, cy - StepLong / 2f, StepDeep, StepLong),
        };
    }

    private static (int Dx, int Dy) StepDelta(Territory.RegionDir dir) => dir switch
    {
        Territory.RegionDir.North => (0, -1),
        Territory.RegionDir.East => (1, 0),
        Territory.RegionDir.South => (0, 1),
        _ => (-1, 0),
    };

    private static string StepName(Territory.RegionDir dir) => dir switch
    {
        Territory.RegionDir.North => "向北",
        Territory.RegionDir.East => "向东",
        Territory.RegionDir.South => "向南",
        _ => "向西",
    };

    /// <summary>
    /// 走一格：队伍挪过去，视口跟着队伍走（缩放不变）。踩上聚落或领地就把那格的地点抽屉拉出来，
    /// 「进入」「回到领地」一按即到；撞上遭遇由遭遇弹窗接手。
    /// </summary>
    private void StepWorld(Territory.RegionDir dir)
    {
        var hub = _vm.Hub;
        var (dx, dy) = StepDelta(dir);
        if (!hub.StepWorld(dx, dy))
        {
            SetNotice("那边走不过去。");
            return;
        }
        var (px, py) = hub.WorldPartyPosition;
        _worldCenter = new Vector2(px + 0.5f, py + 0.5f);
        _worldPick = new Vector2I(-1, -1);
        ClampWorldView();
        var map = World;
        var atHome = map.HasHome && px == map.HomeX && py == map.HomeY;
        if (hub.PendingEncounter == null && (atHome || map.PoiAt(px, py) != null))
            PickWorldTile(new Vector2I(px, py));
    }

    /// <summary>键盘也能走：方向键 / WASD，只在世界地图开着、没有抽屉弹窗时生效。</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key || !WorldMapActive || InputLocked)
            return;
        Territory.RegionDir? dir = key.Keycode switch
        {
            Key.Up or Key.W => Territory.RegionDir.North,
            Key.Right or Key.D => Territory.RegionDir.East,
            Key.Down or Key.S => Territory.RegionDir.South,
            Key.Left or Key.A => Territory.RegionDir.West,
            _ => null,
        };
        if (dir == null)
            return;
        StepWorld(dir.Value);
        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private void WorldButton(Rect2 r, PortraitAction action, string label, bool enabled, Action<float, float> glyph)
    {
        var pressed = PortraitFrame.IsPressed(r);
        DrawRect(r, pressed ? PortraitFrame.PressFill : new Color(0f, 0f, 0f, 0.78f));
        InkDraw.Ink(this, RectLoop(r.Grow(-2f)), enabled ? InkStyle.Line : InkStyle.WoodDark, 2.5f);
        if (enabled)
            glyph(r.GetCenter().X, r.GetCenter().Y);
        else
            InkDraw.Jewel(this, r.GetCenter(), 6f, InkStyle.WoodDark);
        _widgets.Add(new PortraitWidget(r, action, 0, enabled, label));
    }

    private float PoiRadius(WorldPoiType type) =>
        Mathf.Clamp(_worldZoom * (type switch
        {
            WorldPoiType.Capital => 0.62f,
            WorldPoiType.Town or WorldPoiType.Castle or WorldPoiType.Fortress => 0.5f,
            _ => 0.38f,
        }), 6f, 30f);

    private void DrawPoiMark(WorldPoiType type, Vector2 c)
    {
        var r = PoiRadius(type);
        switch (type)
        {
            case WorldPoiType.Capital:
                DrawCircle(c, r + 4f, InkStyle.Bg);
                DrawArc(c, r + 4f, 0f, Mathf.Tau, 40, InkStyle.Line, 2.5f, true);
                PortraitGlyph.Castle(this, c.X, c.Y, r * 0.85f, InkStyle.Line);
                break;
            case WorldPoiType.Town:
            case WorldPoiType.Castle:
            case WorldPoiType.Fortress:
                DrawRect(new Rect2(c - Vector2.One * (r + 2f), Vector2.One * (r + 2f) * 2f), InkStyle.Bg);
                InkDraw.Ink(this, RectLoop(new Rect2(c - Vector2.One * (r + 2f), Vector2.One * (r + 2f) * 2f)), InkStyle.Wood, 2f);
                PortraitGlyph.Castle(this, c.X, c.Y, r * 0.8f, InkStyle.Wood);
                break;
            case WorldPoiType.Monastery:
                DrawCircle(c, r + 2f, InkStyle.Bg);
                PortraitGlyph.Bell(this, c.X, c.Y, r * 0.8f, InkStyle.Wood);
                break;
            case WorldPoiType.Ruin:
                InkDraw.Jewel(this, c, r, InkStyle.Bg);
                InkDraw.Jewel(this, c, r, InkStyle.Dim, filled: false);
                break;
            default:
                InkDraw.Jewel(this, c, r + 2f, InkStyle.Bg);
                InkDraw.Jewel(this, c, r, InkStyle.Wood);
                break;
        }
    }

    private readonly List<(Vector2 At, string Text, Color Color)> _worldLabels = new();

    /// <summary>地名签先记下，等标记都画完再一起画。</summary>
    private void WorldLabel(Vector2 at, string text, Color color)
    {
        if (text.Length == 0 || !PortraitLayout.MapGrid.Grow(-4f).HasPoint(at))
            return;
        _worldLabels.Add((at, text, color));
    }

    /// <summary>
    /// 地名签：黑底半透明衬一下，骨白字。签与队伍棋子相撞就挪到棋子下方；
    /// 签整块收在地图框内（贴边的地名往里推，不出框）。
    /// </summary>
    private void DrawWorldLabels(Rect2? partyMark)
    {
        var grid = PortraitLayout.MapGrid.Grow(-4f);
        var size = PortraitLayout.FontMeta - 6;
        foreach (var (at, text, color) in _worldLabels)
        {
            var m = InkDraw.Measure(text, size);
            var r = new Rect2(at - new Vector2(m.X / 2f + 10f, 22f), new Vector2(m.X + 20f, 44f));
            if (partyMark is { } mark && mark.Intersects(r))
                r.Position = new Vector2(r.Position.X, mark.End.Y + 4f);
            r.Position = new Vector2(Mathf.Clamp(r.Position.X, grid.Position.X, grid.End.X - r.Size.X),
                Mathf.Clamp(r.Position.Y, grid.Position.Y, grid.End.Y - r.Size.Y));
            DrawRect(r, new Color(0f, 0f, 0f, 0.72f));
            InkDraw.Text(this, r.GetCenter(), text, size, color, "cm");
        }
    }

    private string TerritoryName() =>
        _vm.Hub.State.Territory.Name.Length > 0 ? _vm.Hub.State.Territory.Name : "领地";

    // ---------- 输入 ----------

    private bool HandleWorldInput(InputEvent e)
    {
        if (!WorldMapActive)
        {
            _worldPress = _worldDrag = false;
            return false;
        }
        var grid = PortraitLayout.MapGrid;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                if (!wheel.Pressed || !grid.HasPoint(wheel.Position))
                    return false;
                ZoomWorld(wheel.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1f / 1.2f, wheel.Position);
                QueueRedraw();
                return true;
            case InputEventMagnifyGesture pinch:
                if (!grid.HasPoint(pinch.Position))
                    return false;
                ZoomWorld(pinch.Factor, pinch.Position);
                QueueRedraw();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    if (!grid.HasPoint(mb.Position) || Hit(mb.Position) != null)
                        return false;
                    _worldPress = true;
                    _worldDrag = false;
                    _worldPressPos = mb.Position;
                    return true;
                }
                if (!_worldPress)
                    return false;
                _worldPress = false;
                if (!_worldDrag)
                {
                    var t = ScreenToWorld(mb.Position);
                    PickWorldTile(new Vector2I(Mathf.FloorToInt(t.X), Mathf.FloorToInt(t.Y)));
                }
                _worldDrag = false;
                QueueRedraw();
                return true;
            case InputEventMouseMotion { ButtonMask: not 0 } motion when _worldPress:
                if (!_worldDrag && motion.Position.DistanceTo(_worldPressPos) >= PortraitLayout.ListDragThreshold)
                    _worldDrag = true;
                if (_worldDrag)
                {
                    _worldCenter -= motion.Relative / _worldZoom;
                    ClampWorldView();
                    QueueRedraw();
                }
                return true;
        }
        return false;
    }

    private void PickWorldTile(Vector2I tile)
    {
        if (!World.InBounds(tile.X, tile.Y))
            return;
        _worldPick = tile;
        _sheet = SheetKind.World;
    }

    private bool ExecuteWorld(PortraitWidget w)
    {
        var grid = PortraitLayout.MapGrid;
        switch (w.Action)
        {
            case PortraitAction.WorldZoomIn:
                ZoomWorld(1.5f, grid.GetCenter());
                return true;
            case PortraitAction.WorldZoomOut:
                ZoomWorld(1f / 1.5f, grid.GetCenter());
                return true;
            case PortraitAction.WorldHome:
                CenterWorldOnHome();
                return true;
            case PortraitAction.WorldStep:
                StepWorld((Territory.RegionDir)w.Index);
                return true;
            case PortraitAction.WorldGo:
                var map = World;
                _sheet = SheetKind.None;
                var poi = map.PoiAt(_worldPick.X, _worldPick.Y);
                var standing = _vm.Hub.WorldPartyPosition == (_worldPick.X, _worldPick.Y);
                // 已站在聚落格上＝直接进场；否则沿最省时的路走过去（到聚落进场、到领地回家）。
                var done = standing && poi != null ? _vm.Hub.EnterWorldPoi(poi.Id) : _vm.Hub.TravelTo(_worldPick.X, _worldPick.Y);
                if (!done)
                    SetNotice("这里去不了。");
                else if (WorldLayer)
                    CenterWorldOnParty();
                return true;
        }
        return false;
    }

    // ---------- 地点抽屉 ----------

    private static string PoiTypeName(WorldPoiType type) => type switch
    {
        WorldPoiType.Capital => "王都",
        WorldPoiType.Town => "城镇",
        WorldPoiType.Castle => "城堡",
        WorldPoiType.Village => "村庄",
        WorldPoiType.Fortress => "要塞",
        WorldPoiType.Monastery => "修道院",
        WorldPoiType.Ruin => "遗迹",
        _ => "聚落",
    };

    private float DrawWorldSheet()
    {
        var top = WorldSheetTop;
        PortraitFrame.Sheet(this, top);
        var map = World;
        var x = _worldPick.X;
        var y = _worldPick.Y;
        var tile = map.GetTile(x, y);
        if (tile == null)
            return top;
        var hub = _vm.Hub;
        var isHome = map.HasHome && x == map.HomeX && y == map.HomeY;
        var poi = map.PoiAt(x, y);
        var terrain = Rimisekai.WorldMap.Generators.NameGenerator.GenerateTerrainName(tile.Terrain).zh;
        var title = isHome ? TerritoryName() : poi != null ? poi.NameZh : map.TileName(x, y);
        var kind = isHome ? "你的领地" : poi != null ? PoiTypeName(poi.Type) : "野外";
        if (poi != null && hub.IsDungeonCleared(poi.Id))
            kind += " · 已肃清";

        var icon = new Rect2(PortraitLayout.Pad + 20f, top + 70f, 150f, 150f);
        DrawRect(icon, InkStyle.Bg);
        var tex = PortraitWorldAtlas.Texture(map);
        var span = 5;
        var src = new Rect2(new Vector2(x - span / 2f + 0.5f, y - span / 2f + 0.5f) * PortraitWorldAtlas.TilePx,
            Vector2.One * span * PortraitWorldAtlas.TilePx);
        DrawTextureRectRegion(tex, icon.Grow(-6f), src);
        var mid = new Rect2(icon.GetCenter() - Vector2.One * (icon.Size.X - 12f) / span / 2f, Vector2.One * (icon.Size.X - 12f) / span);
        InkDraw.Ink(this, RectLoop(mid), InkStyle.Line, 2.5f);
        InkDraw.Ink(this, RectLoop(icon), InkStyle.Dim, 3f);

        InkDraw.TextBounded(this, new Rect2(icon.End.X + 40f, top + 76f, 620f, 76f), title,
            PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        InkDraw.TextBounded(this, new Rect2(icon.End.X + 40f, top + 156f, 620f, 56f), $"{kind} · {terrain}",
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");

        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        var region = map.RegionAt(x, y);
        var way = tile.IsBridge ? "桥" : tile.IsRoad ? (tile.RoadClass >= 2 ? "官道" : tile.RoadClass == 1 ? "车道" : "野径")
            : tile.IsRiver ? "河流" : "无路";
        var standing = hub.WorldPartyPosition == (x, y);
        var minutes = hub.WorldTravelMinutes(x, y);
        var journey = standing ? "就在此处" : minutes < 0 ? (map.IsPassable(x, y) ? "无路可达" : "无法通行")
            : minutes >= 60 ? $"{minutes / 60}时{minutes % 60:00}分" : $"{minutes}分";
        var lines = new[]
        {
            ("地区", region != null && region.NameZh.Length > 0 ? region.NameZh : "无名之地"),
            ("道路", way),
            ("路程", journey),
            ("距领地", map.HasHome ? $"{Math.Abs(x - map.HomeX) + Math.Abs(y - map.HomeY)} 格" : "—"),
        };
        for (var i = 0; i < lines.Length; i++)
        {
            var row = new Rect2(PortraitLayout.Pad + 20f, top + 260f + i * 74f, PortraitLayout.FullWidth - 40f, 64f);
            InkDraw.Text(this, new Vector2(row.Position.X, row.GetCenter().Y), lines[i].Item1, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(row.End.X, row.GetCenter().Y), lines[i].Item2, PortraitLayout.FontMeta, InkStyle.Line, "rm");
            InkDraw.InkLine(this, new Vector2(row.Position.X, row.End.Y + 4f), new Vector2(row.End.X, row.End.Y + 4f), InkStyle.Hover, 2f);
        }

        var canGo = standing ? poi != null || isHome : minutes > 0;
        var goLabel = standing ? (poi != null ? "进入" : isHome ? "回到领地" : "已在此处")
            : minutes < 0 ? "去不了" : isHome ? "回到领地" : "前往";
        PortraitFrame.Plaque(this, PortraitLayout.SheetFooterRight, goLabel, primary: true, enabled: canGo);
        _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterRight, PortraitAction.WorldGo, 0, canGo, goLabel));
        return top;
    }
}
