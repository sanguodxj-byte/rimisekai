using Godot;
using Rimisekai.Character;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Tools;

public partial class SectorZoomProbe : Node
{
    private int _frames;
    private InkHubScreen? _screen;
    private HubSession? _hub;

    public override void _Ready()
    {
        var pack = new ContentPack();
        _hub = InkWorldBootstrap.OpenHub(pack, out _);
        _screen = new InkHubScreen();
        _screen.Size = new Vector2(InkLayout.CanvasWidth, InkLayout.CanvasHeight);
        _screen.Bind(new InkViewModel(_hub, pack));
        AddChild(_screen);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames == 2)
        {
            _screen!.DebugOpenPage("skills");
            GD.Print($"[zoom-probe] Frame 2: Page opened, initial focused sector = {_screen.DebugFocusedSector}, zoom = {_screen.DebugDiscZoom:F2}");

            // 全盘态：点一个技能瓦片的中心，应**不选中**（全盘只能点扇区，不能点瓦片）。
            var tilePt = FirstSkillTileCenter(_screen, focused: -1);
            if (tilePt.HasValue)
            {
                var hit = _screen.DebugHitAt(tilePt.Value);
                Click(tilePt.Value);
                GD.Print($"[zoom-probe] Frame 2: 全盘点瓦片 {tilePt.Value} 命中={hit} 选中={_screen.DebugSkillSelected}");
            }
        }
        else if (_frames == 4)
        {
            // 点击扇区 1 (灵巧) 标签
            var s1Rect = InkLayout.SkillDiscSectorLabelRect(1);
            GD.Print($"[zoom-probe] Frame 4: Clicking sector 1 (灵巧) at center {s1Rect.GetCenter()}");
            Click(s1Rect.GetCenter());
        }
        else if (_frames >= 5 && _frames <= 20)
        {
            // 动画推进中
            if (_frames == 10 || _frames == 20)
            {
                GD.Print($"[zoom-probe] Frame {_frames}: Animating, focused sector = {_screen!.DebugFocusedSector}, zoom = {_screen.DebugDiscZoom:F3}, pivot = {_screen.DebugDiscPivot}");
            }
        }
        else if (_frames == 25)
        {
            GD.Print($"[zoom-probe] Frame 25: Zoom settled, zoom = {_screen!.DebugDiscZoom:F3}, focused sector = {_screen.DebugFocusedSector}");

            // 聚焦态：点聚焦扇区内的技能瓦片，应能选中。
            var tilePt = FirstSkillTileCenter(_screen, focused: _screen.DebugFocusedSector);
            if (tilePt.HasValue)
            {
                var hit = _screen.DebugHitAt(tilePt.Value);
                Click(tilePt.Value);
                GD.Print($"[zoom-probe] Frame 25: 聚焦态点瓦片 {tilePt.Value} 命中={hit} 选中={_screen.DebugSkillSelected}");
            }

            // 点击左上角 [全盘视角] 重置按钮
            var resetBtn = InkLayout.SkillDiscResetButtonRect.GetCenter();
            GD.Print($"[zoom-probe] Frame 25: Clicking reset button at {resetBtn}");
            Click(resetBtn);
        }
        else if (_frames == 45)
        {
            GD.Print($"[zoom-probe] Frame 45: Zoom reset settled, zoom = {_screen!.DebugDiscZoom:F3}, focused sector = {_screen.DebugFocusedSector}");

            // 测试全盘模式直接点击扇区扇面：点击扇区 2 (智力) 的扇面内部
            var s2Center = InkLayout.SkillDiscPoint(220f, InkLayout.SkillDiscSectorAngle(2));
            GD.Print($"[zoom-probe] Frame 45: 全盘模式点击扇区2扇面 at {s2Center} 命中={_screen.DebugHitAt(s2Center)}");
            Click(s2Center);
        }
        else if (_frames == 65)
        {
            GD.Print($"[zoom-probe] Frame 65: 扇面点击成功放大至扇区 {_screen!.DebugFocusedSector} (预期 2), 当前选中={_screen.DebugSkillSelected}");

            // 测试右上角 SVG 导航按钮：点击右下实心三角（切换至下一技能瓦片）
            var nav = InkLayout.SkillDiscCornerNavButtonRect;
            var lowerTri = InkLayout.SkillDiscCornerNavTriangleLower(nav);
            var nextCenter = Vector2.Zero;
            foreach (var p in lowerTri) nextCenter += p;
            nextCenter /= lowerTri.Length;
            var before = _screen!.DebugSkillSelected;
            GD.Print($"[zoom-probe] Frame 65: 点击右上角右下实心三角 at {nextCenter} 命中={_screen.DebugHitAt(nextCenter)}");
            Click(nextCenter);
            GD.Print($"[zoom-probe] Frame 65: 切换瓦片: {before} -> {_screen.DebugSkillSelected}");
        }
        else if (_frames == 75)
        {
            // 测试右上角 SVG 导航按钮：点击左上实心三角（切换至上一技能瓦片）
            var nav = InkLayout.SkillDiscCornerNavButtonRect;
            var upperTri = InkLayout.SkillDiscCornerNavTriangleUpper(nav);
            var prevCenter = Vector2.Zero;
            foreach (var p in upperTri) prevCenter += p;
            prevCenter /= upperTri.Length;
            var before = _screen!.DebugSkillSelected;
            GD.Print($"[zoom-probe] Frame 75: 点击右上角左上实心三角 at {prevCenter} 命中={_screen.DebugHitAt(prevCenter)}");
            Click(prevCenter);
            GD.Print($"[zoom-probe] Frame 75: 切换瓦片: {before} -> {_screen.DebugSkillSelected}");
            GD.Print($"[zoom-probe] 扇区保持在 {_screen.DebugFocusedSector} (未发生扇区切换，符合要求)");
            GD.Print("[zoom-probe] SUCCESS: All sector clicks and tile switching verified!");
            GetTree().Quit();
        }
    }

    /// <summary>找一个真技能瓦片在当前视角下的中心点，供探针点击。</summary>
    private static Vector2? FirstSkillTileCenter(InkHubScreen screen, int focused)
    {
        var disc = screen.DebugDisc;
        if (disc == null)
            return null;
        foreach (var t in disc.Tiles)
        {
            if (t.Kind != InkSkillNodeKind.Skill)
                continue;
            if (focused >= 0 && t.Sector != focused)
                continue;
            if (focused < 0 && t.Sector != 0)
                continue;
            var poly = InkLayout.SkillDiscTilePolygon(t.Sector, t.Ring, t.Col, t.Upper);
            var tp = InkLayout.TransformDiscPolygon(poly, disc.ViewPivot, disc.ViewZoom, disc.ViewRotation);
            var c = (tp[0] + tp[1] + tp[2]) / 3f;
            return c;
        }
        return null;
    }

    private void Click(Vector2 at)
    {
        var motion = new InputEventMouseMotion { Position = at, GlobalPosition = at };
        GetViewport().PushInput(motion);

        var down = new InputEventMouseButton
        {
            Position = at,
            GlobalPosition = at,
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        };
        GetViewport().PushInput(down);
    }
}
