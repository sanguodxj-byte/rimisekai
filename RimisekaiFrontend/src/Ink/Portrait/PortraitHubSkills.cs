using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>竖屏技能页复用真实星盘；只有详情区响应纵向拖动，不平移星盘。</summary>
public partial class PortraitHubScreen
{
    private string _skillSelectedId = "";
    private int _skillFocusedSector = -1;
    private int _skillDetailFirst;
    private int _skillDetailTotal;
    private int _skillDetailPointer = -2;
    private int _skillDetailPressFirst;
    private float _skillDetailPressY;
    private readonly record struct SkillDetailLine(string Text, int Size, Color Color, bool Rule);

    // —— 星盘视角动效：横板时期的平滑推拉在竖屏迁移中被简化成跳变，这里还原插值 ——
    // 目标视角由 _skillFocusedSector 决定（全景 1.16 / 聚焦 3.0），
    // 当前视角逐帧向目标指数趋近；命中多边形与画面共用同一组当前值，动效期间点选依旧同源。
    private float _skillViewZoom = PortraitLayout.SkillOverviewZoom;
    private float _skillViewRotation;
    private Vector2 _skillViewPivot;
    private bool _skillViewInit;

    private const float SkillViewRate = 9f;     // 指数趋近速率，约 0.35s 收敛
    private const float SkillViewSnap = 0.002f; // 收敛阈值，低于即吸附目标并停止

    private (float Zoom, Vector2 Pivot, float Rotation) SkillViewTarget => (
        PortraitLayout.SkillViewZoom(_skillFocusedSector),
        PortraitLayout.SkillViewPivot(_skillFocusedSector),
        PortraitLayout.SkillViewRotation(_skillFocusedSector));

    /// <summary>把当前视角向目标推进一帧；返回是否仍在运动（运动中由 _Process 持续重绘）。</summary>
    private bool AdvanceSkillView(double delta)
    {
        var (targetZoom, targetPivot, targetRotation) = SkillViewTarget;
        if (!_skillViewInit)
        {
            _skillViewZoom = targetZoom;
            _skillViewPivot = targetPivot;
            _skillViewRotation = targetRotation;
            _skillViewInit = true;
            return false;
        }
        var t = 1f - Mathf.Exp(-(float)delta * SkillViewRate);
        _skillViewZoom = Mathf.Lerp(_skillViewZoom, targetZoom, t);
        _skillViewPivot = _skillViewPivot.Lerp(targetPivot, t);
        _skillViewRotation = Mathf.Wrap(
            _skillViewRotation + Mathf.Wrap(targetRotation - _skillViewRotation, -Mathf.Pi, Mathf.Pi) * t,
            -Mathf.Tau, Mathf.Tau);
        if (Mathf.Abs(_skillViewZoom - targetZoom) < SkillViewSnap
            && _skillViewPivot.DistanceTo(targetPivot) < 0.5f
            && Mathf.Abs(Mathf.Wrap(targetRotation - _skillViewRotation, -Mathf.Pi, Mathf.Pi)) < SkillViewSnap)
        {
            _skillViewZoom = targetZoom;
            _skillViewPivot = targetPivot;
            _skillViewRotation = targetRotation;
            return false;
        }
        return true;
    }

    public string DebugSelectedSkill => _skillSelectedId;
    public int DebugSkillSector => _skillFocusedSector;
    public string[] DebugSkillIds => SkillTiles(BuildSkillPage().Disc!).Select(tile => tile.Id).ToArray();
    private IReadOnlyList<PortraitRegion> SkillRegions() => new[]
    {
        new PortraitRegion("page_top", PortraitLayout.PageTop),
        new PortraitRegion("skill_disc", PortraitLayout.SkillDiscPanel),
        new PortraitRegion("skill_controls", PortraitLayout.SkillControls),
        new PortraitRegion("skill_detail", PortraitLayout.SkillDetailArea),
    };

    private InkPageModel BuildSkillPage() => InkCharacterPageBuilder.Build(_vm, InkPage.Skills,
        Who,
        selectedSkillId: _skillSelectedId, focusedSector: _skillFocusedSector,
        viewZoom: _skillViewZoom, viewPivot: _skillViewPivot, viewRotation: _skillViewRotation)!;

    // —— 星盘 2x 超采样：gl_compatibility 不支持 MSAA 2D（实测设置生效但渲染无变化），
    //    多边形填充边在 1:1 下有明显锯齿。星盘几何画进 2 倍尺寸的透明离屏视口，
    //    再线性缩回面板矩形，等价 4x 超采样；命中多边形仍走主画布投影，不受影响。 ——
    private const float SkillSupersample = 2f;
    private SubViewport _skillView;
    private Vector2I _skillViewSize;

    private sealed partial class SkillDiscProxy : Node2D
    {
        public PortraitHubScreen Screen = null!;

        public override void _Draw() => Screen.DrawDiscContent(this);
    }

    /// <summary>懒建星盘离屏视口；只在技能页激活时更新，离开页面即停更省电。</summary>
    private SubViewport EnsureSkillView()
    {
        if (_skillView != null)
            return _skillView;
        _skillViewSize = new Vector2I(
            (int)(PortraitLayout.SkillDiscPanel.Size.X * SkillSupersample),
            (int)(PortraitLayout.SkillDiscPanel.Size.Y * SkillSupersample));
        _skillView = new SubViewport
        {
            Name = "SkillDiscView",
            Size = _skillViewSize,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
        };
        var proxy = new SkillDiscProxy { Name = "SkillDiscProxy", Screen = this };
        proxy.Scale = new Vector2(SkillSupersample, SkillSupersample);
        proxy.Position = -PortraitLayout.SkillDiscPanel.Position * SkillSupersample;
        _skillView.AddChild(proxy);
        AddChild(_skillView);
        return _skillView;
    }

    private void DrawDiscContent(CanvasItem ci)
    {
        var disc = BuildSkillPage().Disc;
        if (disc == null)
            return;
        InkSkillDisc.DrawContent(ci, disc, PortraitLayout.SkillDiscClip,
            minLineWidth: PortraitLayout.LineHair, drawText: false);
    }

    /// <summary>星盘内容贴图（2x 超采样），由 DrawSkillPage 缩回面板矩形绘制。</summary>
    private Texture2D? SkillDiscTexture()
    {
        if (_push != PushPage.Disc)
            return null;
        var view = EnsureSkillView();
        var want = SubViewport.UpdateMode.Always;
        if (view.RenderTargetUpdateMode != want)
            view.RenderTargetUpdateMode = want;
        return view.GetTexture();
    }

    private void StopSkillView()
    {
        if (_skillView == null)
            return;
        _skillView.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    private void DrawSkillPage()
    {
        var page = BuildSkillPage();
        DrawPageTop(page.Title);
        var disc = page.Disc;
        if (disc == null)
        {
            DrawSkillDetails(page);
            return;
        }
        _skillSelectedId = disc.SelectedId;
        PortraitFrame.Panel(this, PortraitLayout.SkillDiscPanel);
        var discTexture = SkillDiscTexture();
        if (discTexture != null)
            DrawTextureRect(discTexture, PortraitLayout.SkillDiscPanel, false);
        else
            InkSkillDisc.DrawContent(this, disc, PortraitLayout.SkillDiscClip,
                minLineWidth: PortraitLayout.LineHair, drawText: false);
        if (disc.FocusedSector < 0)
        {
            DrawSkillOverviewText(disc);
            for (var sector = 0; sector < disc.SectorLabels.Count; sector++)
            {
                AddSkillPolygon(PortraitAction.SkillSector, sector, disc.SectorLabels[sector],
                    PortraitLayout.SkillSectorPolygon(disc, sector));
                var labelTarget = PortraitLayout.SkillSectorLabelTarget(disc, sector);
                if (PortraitLayout.SkillTouchTarget(labelTarget))
                    _widgets.Add(new PortraitWidget(labelTarget, PortraitAction.SkillSector, sector, true, disc.SectorLabels[sector]));
            }
        }
        else
        {
            for (var i = 0; i < disc.Tiles.Count; i++)
            {
                var tile = disc.Tiles[i];
                if (tile.Kind == InkSkillNodeKind.Skill && tile.Sector == disc.FocusedSector)
                    AddSkillPolygon(PortraitAction.SkillNode, i, tile.Name, PortraitLayout.SkillNodePolygon(disc, tile));
            }
            InkDraw.TextBounded(this, PortraitLayout.SkillFocusedLabel,
                disc.SectorLabels[disc.FocusedSector], PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            PortraitFrame.Plaque(this, PortraitLayout.SkillResetButton, "全盘视角", glyph: PortraitGlyph.Back);
            _widgets.Add(new PortraitWidget(PortraitLayout.SkillResetButton, PortraitAction.SkillReset, 0, true, "‹ 全盘视角"));
            DrawSkillNavigation(PortraitLayout.SkillNavigation);
            var canNavigate = SkillTiles(disc).Length > 0;
            AddSkillPolygon(PortraitAction.SkillPrevious, 0, "", PortraitLayout.SkillNavigationPolygon(true), canNavigate);
            AddSkillPolygon(PortraitAction.SkillNext, 0, "", PortraitLayout.SkillNavigationPolygon(false), canNavigate);
        }
        DrawSkillDetails(page);
    }

    /// <summary>
    /// 星盘右上角翻瓦片钮（原 disc_nav_button.svg 的同一几何，改为代码绘制）：正方形削去一段与星盘外弧同心的弧，
    /// 沿对角线切成两翼，各一圈骨白外框＋银灰内框，左上翼一枚 ◤、右下翼一枚 ◢ 实心三角。坐标沿用原稿 254 见方的视框。
    /// </summary>
    private void DrawSkillNavigation(Rect2 rect)
    {
        var k = rect.Size / 254f;
        Vector2 P(float x, float y) => rect.Position + new Vector2(x - 650f, y - 174f) * k;
        // 两翼外框 / 内框：直角三边＋一段半径 R 的弧（弧心在左下方，与星盘同侧）。
        Vector2[] Wing(Vector2 a, Vector2 b, Vector2 c, float radius)
        {
            var points = new List<Vector2> { a, b, c };
            var mid = (c + a) / 2f;
            var half = c.DistanceTo(a) / 2f;
            var normal = (a - c).Normalized().Orthogonal();
            var h = Mathf.Sqrt(radius * radius - half * half);
            var o1 = mid + normal * h;
            var o2 = mid - normal * h;
            var center = o1.Y - o1.X > o2.Y - o2.X ? o1 : o2;
            var a0 = (c - center).Angle();
            var a1 = (a - center).Angle();
            var span = Mathf.Wrap(a1 - a0, -Mathf.Pi, Mathf.Pi);
            for (var i = 1; i < 12; i++)
                points.Add(center + Vector2.FromAngle(a0 + span * i / 12f) * radius);
            return points.ToArray();
        }
        var scale = k.X;
        var outerUpper = Wing(P(678f, 178f), P(898f, 178f), P(796f, 280f), 1018f * scale);
        var innerUpper = Wing(P(696f, 184f), P(884f, 184f), P(796f, 272f), 1024f * scale);
        var outerLower = Wing(P(902f, 402f), P(902f, 182f), P(800f, 284f), 1018f * scale);
        var innerLower = Wing(P(896f, 384f), P(896f, 196f), P(808f, 284f), 1024f * scale);
        foreach (var (outer, inner) in new[] { (outerUpper, innerUpper), (outerLower, innerLower) })
        {
            DrawColoredPolygon(outer, InkStyle.Bg);
            InkDraw.Ink(this, outer.Append(outer[0]).ToArray(), InkStyle.Line, 2.5f * scale);
            DrawColoredPolygon(inner, InkStyle.Panel);
            InkDraw.Ink(this, inner.Append(inner[0]).ToArray(), InkStyle.Dim, 1.4f * scale);
        }
        DrawColoredPolygon(new[] { P(776f, 196f), P(824f, 196f), P(776f, 244f) }, InkStyle.Line);
        DrawColoredPolygon(new[] { P(884f, 304f), P(884f, 256f), P(836f, 304f) }, InkStyle.Line);
    }

    private void DrawSkillOverviewText(InkSkillDiscModel disc)
    {
        InkDraw.TextBounded(this, PortraitLayout.SkillHubName(disc), disc.CharacterName,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        var styleLines = InkDraw.WrapLines(disc.StyleLine, PortraitLayout.SkillHubStyleLine(disc, 0).Size.X, PortraitLayout.FontMeta);
        for (var i = 0; i < styleLines.Count; i++)
        {
            var rect = PortraitLayout.SkillHubStyleLine(disc, i);
            if (PortraitLayout.SkillDiscClip.Encloses(rect))
                InkDraw.Text(this, rect.GetCenter(), styleLines[i].Trim(), PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        }
        for (var sector = 0; sector < disc.SectorLabels.Count; sector++)
        {
            var rect = PortraitLayout.SkillSectorLabel(disc, sector);
            if (PortraitLayout.SkillDiscClip.Encloses(rect))
                InkDraw.TextBounded(this, rect, disc.SectorLabels[sector], PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        }
    }

    private void AddSkillPolygon(PortraitAction action, int index, string label, Vector2[] polygon, bool enabled = true)
    {
        if (polygon.Length < 3)
            return;
        var bounds = PortraitLayout.SkillPolygonBounds(polygon);
        if (PortraitLayout.SkillTouchTarget(bounds))
            _widgets.Add(new PortraitWidget(bounds, action, index, enabled, label, Polygon: polygon));
    }

    private void DrawSkillDetails(InkPageModel page)
    {
        var lines = new List<SkillDetailLine>();
        AddText(page.DetailTitle, PortraitLayout.FontTitle, InkStyle.Line, true);
        AddText(page.DetailNote, PortraitLayout.FontBody, InkStyle.Line);
        AddText(page.DetailEffect, PortraitLayout.FontBody, InkStyle.Line);
        foreach (var requirement in page.DetailRequirements)
            AddText(requirement.Text, PortraitLayout.FontBody, requirement.Unmet ? InkStyle.Dim : InkStyle.Line);
        AddText(page.EmptyHint, PortraitLayout.FontBody, InkStyle.Dim);
        _skillDetailTotal = lines.Count;
        _skillDetailFirst = Mathf.Clamp(_skillDetailFirst, 0, SkillDetailMaxFirst);
        for (var row = 0; row < PortraitLayout.SkillDetailVisibleRows; row++)
        {
            var index = _skillDetailFirst + row;
            if (index >= lines.Count)
                break;
            var line = lines[index];
            var rect = PortraitLayout.SkillDetailRow(row);
            InkDraw.Text(this, new Vector2(rect.Position.X, rect.GetCenter().Y), line.Text, line.Size, line.Color, "lm");
            if (line.Rule)
                PortraitFrame.FadingRule(this, rect.Position.X, rect.End.X, PortraitLayout.SkillDetailRuleY(row));
        }
        if (SkillDetailMaxFirst > 0)
        {
            InkDraw.InkLine(this, PortraitLayout.SkillDetailTrackTop, PortraitLayout.SkillDetailTrackBottom, InkStyle.Dim, PortraitLayout.LineHair);
            DrawRect(PortraitLayout.SkillDetailThumb(_skillDetailFirst, _skillDetailTotal), InkStyle.Line);
        }
        void AddText(string text, int size, Color color, bool title = false)
        {
            if (text.Length == 0)
                return;
            var wrapped = InkDraw.WrapLines(text, PortraitLayout.SkillDetailText.Size.X, size);
            for (var i = 0; i < wrapped.Count; i++)
                lines.Add(new SkillDetailLine(wrapped[i], size, color, title && i == wrapped.Count - 1));
        }
    }

    private int SkillDetailMaxFirst => System.Math.Max(0, _skillDetailTotal - PortraitLayout.SkillDetailVisibleRows);
    private static InkSkillTile[] SkillTiles(InkSkillDiscModel disc) => disc.Tiles
        .Where(tile => tile.Kind == InkSkillNodeKind.Skill && tile.Sector == disc.FocusedSector)
        .OrderBy(tile => tile.Ring).ThenBy(tile => tile.Col).ThenBy(tile => tile.Upper).ToArray();

    private void ExecuteSkillWidget(PortraitWidget widget)
    {
        if (_push != PushPage.Disc || !widget.Enabled)
            return;
        switch (widget.Action)
        {
            case PortraitAction.SkillSector:
                _skillFocusedSector = widget.Index;
                _skillSelectedId = "";
                break;
            case PortraitAction.SkillNode:
                _skillSelectedId = BuildSkillPage().Disc!.Tiles[widget.Index].Id;
                break;
            case PortraitAction.SkillReset:
                ResetSkillView();
                break;
            case PortraitAction.SkillPrevious:
            case PortraitAction.SkillNext:
                var disc = BuildSkillPage().Disc!;
                var tiles = SkillTiles(disc);
                if (tiles.Length == 0)
                    return;
                var current = System.Array.FindIndex(tiles, tile => tile.Id == disc.SelectedId);
                var direction = widget.Action == PortraitAction.SkillPrevious ? -1 : 1;
                _skillSelectedId = tiles[(current + direction + tiles.Length) % tiles.Length].Id;
                break;
            default:
                return;
        }
        _skillDetailFirst = 0;
        QueueRedraw();
    }

    private bool HandleSkillInput(InputEvent e)
    {
        if (_push != PushPage.Disc)
            return false;
        switch (e)
        {
            case InputEventMouseButton mouse when mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (!mouse.Pressed || !PortraitLayout.SkillDetailArea.HasPoint(mouse.Position))
                    return false;
                _skillDetailFirst = Mathf.Clamp(_skillDetailFirst + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 0, SkillDetailMaxFirst);
                QueueRedraw();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                return mouse.Pressed ? BeginSkillDetailDrag(mouse.Position, -1) : EndSkillDetailDrag(-1);
            case InputEventMouseMotion motion:
                return MoveSkillDetailDrag(motion.Position, -1);
            case InputEventScreenTouch touch:
                return touch.Pressed ? BeginSkillDetailDrag(touch.Position, touch.Index) : EndSkillDetailDrag(touch.Index);
            case InputEventScreenDrag drag:
                return MoveSkillDetailDrag(drag.Position, drag.Index);
            default:
                return false;
        }
    }
    private bool BeginSkillDetailDrag(Vector2 at, int pointer)
    {
        if (_skillDetailPointer != -2 || !PortraitLayout.SkillDetailArea.HasPoint(at))
            return false;
        _skillDetailPointer = pointer;
        _skillDetailPressY = at.Y;
        _skillDetailPressFirst = _skillDetailFirst;
        return true;
    }
    private bool MoveSkillDetailDrag(Vector2 at, int pointer)
    {
        if (_skillDetailPointer != pointer)
            return false;
        var dy = at.Y - _skillDetailPressY;
        if (Mathf.Abs(dy) >= PortraitLayout.SkillDragThreshold)
        {
            _skillDetailFirst = Mathf.Clamp(_skillDetailPressFirst - (int)(dy / PortraitLayout.SkillDetailLineHeight), 0, SkillDetailMaxFirst);
            QueueRedraw();
        }
        return true;
    }
    private bool EndSkillDetailDrag(int pointer)
    {
        if (_skillDetailPointer != pointer)
            return false;
        _skillDetailPointer = -2;
        return true;
    }
    private void ResetSkillView()
    {
        _skillSelectedId = "";
        _skillFocusedSector = -1;
        _skillDetailFirst = 0;
        _skillDetailTotal = 0;
        _skillDetailPointer = -2;
        _skillDetailPressFirst = 0;
        _skillDetailPressY = 0f;
        _skillViewInit = false;
    }
}
