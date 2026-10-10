using System;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 战斗转场（2026-10-10 主人要求「尽可能精美且有设计感」）：进战斗那一刻盖在最上层演 2.1 秒，全程骨白线稿、不新增色相。
///
/// 1. 定格（0–0.14）：截下进战前那一帧，压暗一层，上下两道黑幕带（内沿双线＋正中菱珠）合拢进来。
/// 2. 双斩（0.08–0.36）：两道骨白刀光先后沿两条对角线劈过，刀头最亮、刀尾收细，划过处留一道裂痕。
/// 3. 碎裂（0.36–0.95）：定格画面沿两道裂痕碎成上 / 右 / 下 / 左四块，各自朝外飞散、微转、淡出；
///    裂缝后面露出石壁暗纹底与一扇缓转的玫瑰窗（三重环、十二枚尖拱花瓣、外圈菱珠、放射光线）。
/// 4. 交锋（0.40–0.95）：两柄长剑自左右旋入，在窗心交成 X，相触一刻炸开一道冲击环与一圈飞散的菱屑。
/// 5. 题字（0.70–）：窗下「交 战」大字由疏到密收拢，两侧细线带菱珠自中向外展开；窗上一行小字「战 斗 开 始」。不列敌方名单（主人定 2026-10-11）。
/// 6. 开门（1.55–2.10）：整幅画面自正中竖向劈开，左右两扇像教堂大门一样向两侧推开，门缝内沿各一道骨白边与铆珠，露出底下已就位的战斗画面。
///
/// 首领专属（拟案，待主人核定）：演 2.8 秒，与普通战处处不同——
/// 黑幕带更厚；一道竖劈自上而下劈开画面并震屏，定格只裂成左右两半向两侧飞开；
/// 窗外加一圈荆棘尖刺，一柄巨剑自上方直插窗心（插入一刻震屏、放射裂纹、冲击环）；
/// 窗下写首领名字，下方双层细线与三枚菱，再下一行小字「首 领 降 临」（窗上正中让给巨剑）；开门段放慢（2.25–2.80）。
///
/// 只是表现层：期间整屏吞掉输入，战斗画面暂停推进（<see cref="Finished"/> 时恢复）；核对模式（<see cref="PortraitMotion.Instant"/>）不演。
/// 左右两扇门是两个裁切子节点，同一幅画各画一半，开门时只挪子节点。
/// </summary>
public sealed partial class PortraitBattleWipe : Control
{
    public const float Duration = 2.1f;

    /// <summary>首领战转场时长。</summary>
    public const float BossDuration = 2.8f;
    private const float W = PortraitLayout.CanvasWidth;
    private const float H = PortraitLayout.CanvasHeight;
    private static readonly Vector2 C = new(W / 2f, H / 2f);

    private readonly Pane _left;
    private readonly Pane _right;
    private ImageTexture? _frame;
    private float _t = BossDuration;
    private bool _frozen;
    private string _title = "";
    private bool _boss;
    private float Length => _boss ? BossDuration : Duration;
    private Transform2D _base = Transform2D.Identity;

    /// <summary>演完（或被跳过）时触发：恢复战斗推进。</summary>
    public event Action? Finished;

    public bool Running => Visible && _t < Length;

    /// <summary>此刻演的是不是首领专属转场。</summary>
    public bool Boss => _boss;

    public PortraitBattleWipe()
    {
        Size = new Vector2(W, H);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;
        Visible = false;
        _left = new Pane(this, 0f);
        _right = new Pane(this, W / 2f);
        AddChild(_left);
        AddChild(_right);
    }

    /// <summary>开演：截当帧（进战前的画面）做定格，title 大字（首领战写首领名字）。</summary>
    public void Play(string title, bool boss)
    {
        Begin(title, boss);
        _t = 0f;
        _frozen = false;
        SetProcess(true);
    }

    /// <summary>核对用：截当帧开演并停在 t 秒（不推进）。之后用 <see cref="DebugSeek"/> 换时刻、<see cref="DebugEnd"/> 收起。</summary>
    public void DebugBegin(string title, bool boss, float t)
    {
        Begin(title, boss);
        _frozen = true;
        SetProcess(false);
        DebugSeek(t);
    }

    public void DebugSeek(float t)
    {
        _t = Mathf.Clamp(t, 0f, Length - 0.001f);
        Layout();
    }

    public void DebugEnd() => Finish();

    private void Begin(string title, bool boss)
    {
        _title = title;
        _boss = boss;
        _frame = null;
        var image = GetViewport()?.GetTexture()?.GetImage();
        if (image != null && !image.IsEmpty())
            _frame = ImageTexture.CreateFromImage(image);
        Visible = true;
        GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _frozen)
            return;
        _t += (float)delta;
        if (_t >= Length)
        {
            Finish();
            return;
        }
        Layout();
    }

    private void Finish()
    {
        Visible = false;
        _frozen = false;
        _frame = null;
        _t = Length;
        SetProcess(false);
        Finished?.Invoke();
    }

    /// <summary>两扇门的位置随开门进度外推，每帧重画。</summary>
    private void Layout()
    {
        var open = Ease3(Seg(DoorStart, Length));
        _left.Position = new Vector2(-open * (W / 2f + 40f), 0f);
        _right.Position = new Vector2(W / 2f + open * (W / 2f + 40f), 0f);
        _left.QueueRedraw();
        _right.QueueRedraw();
    }

    // ---------- 时间 ----------

    /// <summary>纹章（玫瑰窗＋荆棘＋巨剑）中心：首领战上移，给下方首领名字留出整块空间。</summary>
    private Vector2 O => _boss ? new Vector2(C.X, C.Y - 140f) : C;

    /// <summary>首领战玫瑰窗缩一号，荆棘与巨剑同比。</summary>
    private float RoseScale => _boss ? 0.84f : 1f;

    private float DoorStart => _boss ? 2.25f : 1.55f;

    /// <summary>首领战震屏：竖劈落下与巨剑插入两下，各自衰减。</summary>
    private Vector2 Shake()
    {
        if (!_boss)
            return Vector2.Zero;
        var amp = 26f * (1f - Seg(0.16f, 0.40f)) * (_t >= 0.16f ? 1f : 0f)
            + 34f * (1f - Seg(0.62f, 0.92f)) * (_t >= 0.62f ? 1f : 0f);
        return new Vector2(MathF.Sin(_t * 97f), MathF.Cos(_t * 83f)) * amp;
    }

    private float Seg(float a, float b) => Mathf.Clamp((_t - a) / (b - a), 0f, 1f);

    private static float Ease3(float x) => 1f - MathF.Pow(1f - x, 3f);

    private static float EaseIn2(float x) => x * x;

    // ---------- 整幅画（两扇门各画一半） ----------

    private sealed partial class Pane : Control
    {
        private readonly PortraitBattleWipe _owner;
        private readonly float _rest;

        public Pane(PortraitBattleWipe owner, float rest)
        {
            _owner = owner;
            _rest = rest;
            Size = new Vector2(W / 2f, H);
            ClipContents = true;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw() => _owner.DrawScene(this, _rest, _rest > 0f);
    }

    private void SetT(CanvasItem ci, Transform2D local) => ci.DrawSetTransformMatrix(_base * local);

    private void DrawScene(CanvasItem ci, float rest, bool rightDoor)
    {
        _base = new Transform2D(0f, new Vector2(-rest, 0f) + (_t < DoorStart ? Shake() : Vector2.Zero));
        SetT(ci, Transform2D.Identity);

        DrawBackdrop(ci);
        if (_boss)
        {
            DrawThorns(ci);
            DrawRose(ci);
            DrawGreatSword(ci);
            DrawBossCaption(ci);
            DrawHalves(ci);
            DrawCleave(ci);
        }
        else
        {
            DrawRose(ci);
            DrawSwords(ci);
            DrawCaption(ci);
            DrawShards(ci);
            DrawSlashes(ci);
        }
        DrawLetterbox(ci);
        DrawDoorEdge(ci, rightDoor);
        SetT(ci, Transform2D.Identity);
    }

    /// <summary>石壁暗纹底压暗，窗心一团极淡的骨白辉光。</summary>
    private void DrawBackdrop(CanvasItem ci)
    {
        ci.DrawRect(new Rect2(0, 0, W, H), InkStyle.Bg);
        GothicArt.Backdrop(ci);
        ci.DrawRect(new Rect2(0, 0, W, H), new Color(InkStyle.Bg, 0.55f));
        var glow = Ease3(Seg(0.36f, 0.9f));
        for (var i = 0; i < 10; i++)
            ci.DrawCircle(O, 640f - i * 52f, new Color(InkStyle.Line, 0.012f * glow));
    }

    /// <summary>玫瑰窗：三重环、十二尖拱花瓣、外圈菱珠，外侧放射细线反向缓转。</summary>
    private void DrawRose(CanvasItem ci)
    {
        var show = Ease3(Seg(0.36f, 0.85f));
        if (show <= 0f)
            return;
        var scale = (0.72f + 0.28f * show) * RoseScale;
        var spin = _t * 0.22f;
        var a = show;
        // 放射光线。
        for (var k = 0; k < 36; k++)
        {
            var th = -spin * 0.6f + k * Mathf.Tau / 36f;
            var dir = new Vector2(MathF.Cos(th), MathF.Sin(th));
            ci.DrawLine(O + dir * 360f * scale, O + dir * (k % 3 == 0 ? 760f : 600f) * scale,
                new Color(InkStyle.Line, (k % 3 == 0 ? 0.16f : 0.07f) * a), k % 3 == 0 ? 2f : 1.2f, true);
        }
        float R(float r) => r * scale;
        ci.DrawCircle(O, R(338f), new Color(InkStyle.Bg, 0.9f * a));
        ci.DrawArc(O, R(338f), 0f, Mathf.Tau, 96, new Color(InkStyle.Line, a), 4f, true);
        ci.DrawArc(O, R(322f), 0f, Mathf.Tau, 96, new Color(InkStyle.Dim, a), 1.5f, true);
        ci.DrawArc(O, R(176f), 0f, Mathf.Tau, 72, new Color(InkStyle.Line, a), 3f, true);
        ci.DrawArc(O, R(160f), 0f, Mathf.Tau, 72, new Color(InkStyle.Dim, a), 1.5f, true);
        // 十二枚尖拱花瓣：根在内环，尖顶抵外环。
        for (var k = 0; k < 12; k++)
        {
            var th = spin + k * Mathf.Tau / 12f;
            var pts = new Vector2[17];
            for (var i = 0; i <= 16; i++)
            {
                var u = i / 16f;
                var side = u <= 0.5f ? -1f : 1f;
                var v = u <= 0.5f ? u * 2f : (1f - u) * 2f; // 0 根 → 1 顶
                var half = 0.2f * MathF.Pow(1f - v, 0.7f);
                var r = Mathf.Lerp(186f, 312f, v);
                var ang = th + side * half;
                pts[i] = O + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * R(r);
            }
            ci.DrawPolyline(pts, new Color(InkStyle.Dim, a), 2f, true);
            // 瓣心一枚小菱、瓣间一道辐条。
            var mid = th;
            var pm = O + new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * R(258f);
            PortraitGlyph.Diamond(ci, pm.X, pm.Y, 6f * scale, new Color(InkStyle.Line, 0.8f * a));
            var gap = th + Mathf.Tau / 24f;
            var gd = new Vector2(MathF.Cos(gap), MathF.Sin(gap));
            ci.DrawLine(O + gd * R(176f), O + gd * R(322f), new Color(InkStyle.WoodDark, a), 1.5f, true);
        }
        for (var k = 0; k < 24; k++)
        {
            var th = spin + (k + 0.5f) * Mathf.Tau / 24f;
            var p = O + new Vector2(MathF.Cos(th), MathF.Sin(th)) * R(330f);
            PortraitGlyph.Diamond(ci, p.X, p.Y, 5f * scale, new Color(InkStyle.Line, a));
        }
        ci.DrawCircle(O, R(150f), new Color(InkStyle.Bg, a));
    }

    /// <summary>双剑自左右旋入交成 X，相触时冲击环＋菱屑。</summary>
    private void DrawSwords(CanvasItem ci)
    {
        var come = Seg(0.40f, 0.62f);
        if (come <= 0f)
            return;
        var e = Ease3(come);
        foreach (var side in new[] { -1f, 1f })
        {
            // 终态：剑尖朝上外侧、剑柄朝下内侧，两剑在窗心交叉成 X。
            var angle = side * (Mathf.Pi / 4f) + side * (1f - e) * 1.4f;
            // 两剑在剑身中段（护手上方 200）交叉：把这一点对准窗心。
            var pos = C + new Vector2(0f, 200f).Rotated(angle) + new Vector2(side * (1f - e) * 700f, (1f - e) * 120f);
            SetT(ci, new Transform2D(angle, pos));
            DrawSword(ci, e);
        }
        SetT(ci, Transform2D.Identity);

        var burst = Seg(0.60f, 1.0f);
        if (burst > 0f && burst < 1f)
        {
            var be = Ease3(burst);
            ci.DrawArc(C, 70f + be * 560f, 0f, Mathf.Tau, 96, new Color(InkStyle.Line, 0.9f * (1f - burst)), 10f * (1f - be) + 2f, true);
            ci.DrawArc(C, 40f + be * 380f, 0f, Mathf.Tau, 96, new Color(InkStyle.Line, 0.5f * (1f - burst)), 3f, true);
            for (var k = 0; k < 16; k++)
            {
                var th = k * Mathf.Tau / 16f + 0.2f;
                var d = (k % 2 == 0 ? 520f : 380f) * be;
                var p = C + new Vector2(MathF.Cos(th), MathF.Sin(th)) * (60f + d);
                PortraitGlyph.Diamond(ci, p.X, p.Y, 9f * (1f - burst) + 2f, new Color(InkStyle.Line, 1f - burst));
            }
        }
        // 相触一闪：窗心白光一下盖过再退。
        var flash = Seg(0.60f, 0.74f);
        if (flash > 0f && flash < 1f)
            ci.DrawCircle(C, 150f, new Color(InkStyle.Line, 0.55f * (1f - flash)));
    }

    /// <summary>一柄长剑（本地坐标：剑尖朝上 -y，护手在原点）：收尖剑身＋血槽、带菱端的护手、缠握柄、圆首。</summary>
    private static void DrawSword(CanvasItem ci, float alpha)
    {
        var line = new Color(InkStyle.Line, alpha);
        var edge = new Color(InkStyle.Bg, alpha);
        var blade = new[] { new Vector2(-22f, -8f), new Vector2(-22f, -380f), new Vector2(0f, -450f), new Vector2(22f, -380f), new Vector2(22f, -8f) };
        ci.DrawColoredPolygon(blade, line);
        ci.DrawPolyline(new[] { blade[0], blade[1], blade[2], blade[3], blade[4], blade[0] }, edge, 3f, true);
        ci.DrawLine(new Vector2(0f, -30f), new Vector2(0f, -390f), new Color(InkStyle.WoodDark, alpha), 4f, true);
        // 护手：两端下弯，各收一枚菱。
        var guard = new[]
        {
            new Vector2(-104f, 10f), new Vector2(-92f, -12f), new Vector2(92f, -12f), new Vector2(104f, 10f),
            new Vector2(80f, 14f), new Vector2(-80f, 14f),
        };
        ci.DrawColoredPolygon(guard, line);
        ci.DrawPolyline(new[] { guard[0], guard[1], guard[2], guard[3], guard[4], guard[5], guard[0] }, edge, 3f, true);
        PortraitGlyph.Diamond(ci, -112f, 14f, 13f, line);
        PortraitGlyph.Diamond(ci, 112f, 14f, 13f, line);
        PortraitGlyph.Diamond(ci, 0f, 0f, 12f, edge);
        // 握柄＋缠绳。
        ci.DrawRect(new Rect2(-13f, 14f, 26f, 104f), line);
        for (var y = 26f; y < 116f; y += 15f)
            ci.DrawLine(new Vector2(-13f, y), new Vector2(13f, y + 7f), edge, 2.5f, true);
        ci.DrawCircle(new Vector2(0f, 136f), 22f, line);
        ci.DrawArc(new Vector2(0f, 136f), 22f, 0f, Mathf.Tau, 24, edge, 3f, true);
        PortraitGlyph.Diamond(ci, 0f, 136f, 8f, edge);
    }

    /// <summary>窗上小字、窗下大字（由疏到密收拢）＋展开的细线菱珠。</summary>
    private void DrawCaption(CanvasItem ci)
    {
        var show = Ease3(Seg(0.70f, 1.15f));
        if (show <= 0f)
            return;
        DrawSpaced(ci, new Vector2(C.X, C.Y - 452f), "战斗开始", 40, 26f, new Color(InkStyle.Dim, show));
        var spread = 120f - 70f * show;
        var titleY = C.Y + 470f;
        DrawSpaced(ci, new Vector2(C.X, titleY), _title, 112, spread, new Color(InkStyle.Line, show));
        var reach = 380f * Ease3(Seg(0.85f, 1.3f));
        var ry = titleY + 98f;
        if (reach > 2f)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                ci.DrawLine(new Vector2(C.X + side * 30f, ry), new Vector2(C.X + side * (30f + reach), ry), new Color(InkStyle.Line, show), 2.5f, true);
                ci.DrawLine(new Vector2(C.X + side * 30f, ry + 9f), new Vector2(C.X + side * (30f + reach * 0.7f), ry + 9f), new Color(InkStyle.Dim, show), 1.2f, true);
                PortraitGlyph.Diamond(ci, C.X + side * (36f + reach), ry, 9f, new Color(InkStyle.Line, show));
            }
            PortraitGlyph.Diamond(ci, C.X, ry + 4f, 13f, new Color(InkStyle.Line, show));
        }
    }

    // ---------- 首领专属 ----------

    /// <summary>窗外一圈荆棘尖刺：自窗缘向外刺出，逆着窗转。</summary>
    private void DrawThorns(CanvasItem ci)
    {
        var grow = Ease3(Seg(0.40f, 0.95f));
        if (grow <= 0f)
            return;
        var spin = -_t * 0.15f;
        for (var k = 0; k < 18; k++)
        {
            var th = spin + k * Mathf.Tau / 18f;
            var dir = new Vector2(MathF.Cos(th), MathF.Sin(th));
            var n = new Vector2(-dir.Y, dir.X);
            var len = (k % 2 == 0 ? 170f : 100f) * grow * RoseScale;
            var root = O + dir * 344f * RoseScale;
            var tip = root + dir * len;
            ci.DrawColoredPolygon(new[] { root - n * 16f, tip, root + n * 16f }, new Color(InkStyle.Line, 0.85f * grow));
            ci.DrawLine(root, tip, new Color(InkStyle.Bg, grow), 2f, true);
        }
        ci.DrawArc(O, 352f * RoseScale, 0f, Mathf.Tau, 96, new Color(InkStyle.Line, grow), 6f, true);
    }

    /// <summary>巨剑自上方直插窗心：剑尖没入时冲击环、放射裂纹、窗心一闪。</summary>
    private void DrawGreatSword(CanvasItem ci)
    {
        var fall = Seg(0.44f, 0.62f);
        if (fall <= 0f)
            return;
        var e = EaseIn2(fall);
        // 剑尖朝下、放大 1.2 倍：剑尖在护手下方 450×1.2，自画面上方落到窗心下方 40；剑首留在上黑幕带之下。
        const float scale = 1.2f;
        var tipY = Mathf.Lerp(-80f, O.Y + 40f, e);
        SetT(ci, new Transform2D(Mathf.Pi, new Vector2(O.X, tipY - 450f * scale)) * Transform2D.Identity.Scaled(new Vector2(scale, scale)));
        DrawSword(ci, 1f);
        SetT(ci, Transform2D.Identity);

        var hit = Seg(0.62f, 1.10f);
        if (hit <= 0f)
            return;
        var he = Ease3(hit);
        // 放射裂纹：八道折线，插入后留着。
        for (var k = 0; k < 8; k++)
        {
            var th = k * Mathf.Tau / 8f + 0.3f;
            var d = new Vector2(MathF.Cos(th), MathF.Sin(th));
            var n = new Vector2(-d.Y, d.X);
            var reach = 260f * he;
            var mid = O + d * reach * 0.5f + n * (k % 2 == 0 ? 22f : -22f);
            ci.DrawPolyline(new[] { O, mid, O + d * reach }, new Color(InkStyle.Line, 0.8f), 3f, true);
        }
        if (hit < 1f)
        {
            ci.DrawArc(O, 60f + he * 640f, 0f, Mathf.Tau, 96, new Color(InkStyle.Line, 0.95f * (1f - hit)), 14f * (1f - he) + 2f, true);
            ci.DrawArc(O, 30f + he * 420f, 0f, Mathf.Tau, 96, new Color(InkStyle.Line, 0.5f * (1f - hit)), 4f, true);
        }
        var flash = Seg(0.62f, 0.78f);
        if (flash > 0f && flash < 1f)
            ci.DrawRect(new Rect2(0, 0, W, H), new Color(InkStyle.Line, 0.45f * (1f - flash)));
    }

    /// <summary>窗下首领名字（按宽度收字号），下方双层细线与三枚菱，再下一行「首领降临」。</summary>
    private void DrawBossCaption(CanvasItem ci)
    {
        var show = Ease3(Seg(0.95f, 1.45f));
        if (show <= 0f)
            return;
        var titleY = C.Y + 500f;
        var size = 120;
        while (size > 64 && InkDraw.Measure(_title, size).X + 24f * (_title.Length - 1) > W - 160f)
            size -= 4;
        var drop = (1f - show) * -40f;
        DrawSpaced(ci, new Vector2(C.X, titleY + drop), _title, size, 24f, new Color(InkStyle.Line, show));
        var reach = 420f * Ease3(Seg(1.15f, 1.6f));
        var ry = titleY + size * 0.5f + 40f;
        if (reach > 2f)
        {
            ci.DrawLine(new Vector2(C.X - reach, ry), new Vector2(C.X + reach, ry), new Color(InkStyle.Line, show), 4f, true);
            ci.DrawLine(new Vector2(C.X - reach * 0.8f, ry + 14f), new Vector2(C.X + reach * 0.8f, ry + 14f), new Color(InkStyle.Dim, show), 1.5f, true);
            ci.DrawLine(new Vector2(C.X - reach, ry - 14f - size - 40f), new Vector2(C.X + reach, ry - 14f - size - 40f), new Color(InkStyle.Dim, show), 1.5f, true);
            foreach (var x in new[] { C.X - reach, C.X, C.X + reach })
            {
                PortraitGlyph.Diamond(ci, x, ry, 15f, new Color(InkStyle.Line, show));
                PortraitGlyph.Diamond(ci, x, ry, 6f, new Color(InkStyle.Bg, show));
            }
        }
        // 窗上正中是插着的巨剑，「首领降临」放在名字下方。
        DrawSpaced(ci, new Vector2(C.X, ry + 76f), "首领降临", 44, 30f, new Color(InkStyle.Dim, show));
    }

    /// <summary>首领战定格：沿正中竖劈裂成左右两半，各自向外飞、微转、淡出。</summary>
    private void DrawHalves(CanvasItem ci)
    {
        var fly = Seg(0.34f, 0.95f);
        if (fly >= 1f)
            return;
        var dim = 0.45f * Ease3(Seg(0f, 0.3f));
        var e = EaseIn2(fly);
        var alpha = 1f - Seg(0.6f, 0.95f);
        var tint = new Color(1f - dim, 1f - dim, 1f - dim, alpha);
        foreach (var side in new[] { -1f, 1f })
        {
            var x0 = side < 0 ? 0f : W / 2f;
            var rect = new[] { new Vector2(x0, 0), new Vector2(x0 + W / 2f, 0), new Vector2(x0 + W / 2f, H), new Vector2(x0, H) };
            var pivot = new Vector2(side < 0 ? W / 2f : W / 2f, H);
            SetT(ci, new Transform2D(side * 0.12f * e, pivot + new Vector2(side * e * 900f, 0f)) * new Transform2D(0f, -pivot));
            var uv = new Vector2[4];
            for (var i = 0; i < 4; i++)
                uv[i] = rect[i] / new Vector2(W, H);
            if (_frame != null)
                ci.DrawPolygon(rect, new[] { tint, tint, tint, tint }, uv, _frame);
            else
                ci.DrawColoredPolygon(rect, new Color(InkStyle.Panel, alpha));
            if (fly > 0f)
                ci.DrawLine(new Vector2(W / 2f, 0), new Vector2(W / 2f, H), new Color(InkStyle.Line, 0.9f * alpha), 4f, true);
        }
        SetT(ci, Transform2D.Identity);
    }

    /// <summary>一道竖劈：自上而下，比普通刀光更宽更亮，劈过留裂痕。</summary>
    private void DrawCleave(CanvasItem ci)
    {
        if (_t >= 0.34f)
            return;
        var p = Seg(0.06f, 0.18f);
        if (p <= 0f)
            return;
        var from = new Vector2(W / 2f, -150f);
        var to = new Vector2(W / 2f, H + 150f);
        var head = from.Lerp(to, Ease3(p));
        ci.DrawLine(from, head, new Color(InkStyle.Line, 0.95f), 4f, true);
        if (p < 1f)
        {
            var tail = from.Lerp(to, Mathf.Max(0f, Ease3(p) - 0.5f));
            foreach (var (width, alpha) in new[] { (90f, 0.14f), (44f, 0.32f), (16f, 1f) })
            {
                var neck = head - new Vector2(0f, 90f);
                ci.DrawColoredPolygon(new[] { tail, neck + new Vector2(width / 2f, 0f), head, neck - new Vector2(width / 2f, 0f) }, new Color(InkStyle.Line, alpha));
            }
            ci.DrawCircle(head, 16f, InkStyle.Line);
        }
        // 劈落一刻，整屏一闪。
        var flash = Seg(0.16f, 0.30f);
        if (flash > 0f && flash < 1f)
            ci.DrawRect(new Rect2(0, 0, W, H), new Color(InkStyle.Line, 0.35f * (1f - flash)));
    }

    /// <summary>逐字等距排开、整行居中（字距 spacing 加在字宽之外）。</summary>
    private static void DrawSpaced(CanvasItem ci, Vector2 center, string text, int size, float spacing, Color color)
    {
        if (text.Length == 0)
            return;
        var widths = new float[text.Length];
        var total = spacing * (text.Length - 1);
        for (var i = 0; i < text.Length; i++)
        {
            widths[i] = InkDraw.Measure(text[i].ToString(), size).X;
            total += widths[i];
        }
        var x = center.X - total / 2f;
        for (var i = 0; i < text.Length; i++)
        {
            InkDraw.Text(ci, new Vector2(x + widths[i] / 2f, center.Y), text[i].ToString(), size, color, "cm");
            x += widths[i] + spacing;
        }
    }

    /// <summary>定格画面：先整幅压暗；碎裂段沿两条对角线分成四块各自外飞。</summary>
    private void DrawShards(CanvasItem ci)
    {
        var fly = Seg(0.36f, 0.95f);
        if (fly >= 1f)
            return;
        var dim = 0.35f * Ease3(Seg(0f, 0.3f));
        Vector2[] corners = { new(0, 0), new(W, 0), new(W, H), new(0, H) };
        Vector2[] push = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
        float[] twist = { -0.10f, 0.08f, 0.12f, -0.07f };
        var e = EaseIn2(fly);
        for (var k = 0; k < 4; k++)
        {
            var a = corners[k];
            var b = corners[(k + 1) % 4];
            var center = (a + b + C) / 3f;
            var move = push[k] * e * (k % 2 == 0 ? 1500f : 900f);
            var rot = twist[k] * e;
            var local = new Transform2D(rot, center + move) * new Transform2D(0f, -center);
            SetT(ci, local);
            var tri = new[] { a, b, C };
            var alpha = 1f - Seg(0.6f, 0.95f);
            var tint = new Color(1f - dim, 1f - dim, 1f - dim, alpha);
            if (_frame != null)
                ci.DrawPolygon(tri, new[] { tint, tint, tint }, new[] { a / new Vector2(W, H), b / new Vector2(W, H), C / new Vector2(W, H) }, _frame);
            else
                ci.DrawColoredPolygon(tri, new Color(InkStyle.Panel, alpha));
            if (fly > 0f)
            {
                // 裂口：两条斜边各一道骨白描线。
                ci.DrawLine(a, C, new Color(InkStyle.Line, 0.9f * alpha), 3f, true);
                ci.DrawLine(b, C, new Color(InkStyle.Line, 0.9f * alpha), 3f, true);
            }
        }
        SetT(ci, Transform2D.Identity);
    }

    /// <summary>两道刀光：刀头最亮最宽，刀尾收细；劈过后留一道细裂痕，碎裂开始即消失。</summary>
    private void DrawSlashes(CanvasItem ci)
    {
        if (_t >= 0.36f)
            return;
        DrawSlash(ci, new Vector2(W + 80f, -130f), new Vector2(-80f, H + 130f), Seg(0.08f, 0.22f));
        DrawSlash(ci, new Vector2(-80f, -130f), new Vector2(W + 80f, H + 130f), Seg(0.20f, 0.34f));
    }

    private static void DrawSlash(CanvasItem ci, Vector2 from, Vector2 to, float p)
    {
        if (p <= 0f)
            return;
        var e = Ease3(p);
        var head = from.Lerp(to, e);
        var dir = (to - from).Normalized();
        var n = new Vector2(-dir.Y, dir.X);
        // 留下的裂痕。
        ci.DrawLine(from, head, new Color(InkStyle.Line, 0.85f), 2.5f, true);
        if (p >= 1f)
            return;
        var tail = from.Lerp(to, Mathf.Max(0f, e - 0.45f));
        // 外辉（宽、淡）＋刀芯（窄、亮），都收成尖。
        foreach (var (width, alpha) in new[] { (46f, 0.16f), (22f, 0.35f), (9f, 1f) })
        {
            var neck = head - dir * 60f;
            ci.DrawColoredPolygon(new[] { tail, neck + n * width / 2f, head, neck - n * width / 2f }, new Color(InkStyle.Line, alpha));
        }
        ci.DrawCircle(head, 10f, InkStyle.Line);
    }

    /// <summary>上下黑幕带：定格时合拢进来，内沿双线＋正中菱珠＋两端角花。</summary>
    private void DrawLetterbox(CanvasItem ci)
    {
        var close = Ease3(Seg(0f, 0.3f));
        var band = (_boss ? 250f : 190f) * close;
        if (band <= 1f)
            return;
        foreach (var top in new[] { true, false })
        {
            var r = top ? new Rect2(0, 0, W, band) : new Rect2(0, H - band, W, band);
            ci.DrawRect(r, InkStyle.Bg);
            var y = top ? r.End.Y : r.Position.Y;
            var inward = top ? 1f : -1f;
            ci.DrawLine(new Vector2(0, y), new Vector2(W, y), InkStyle.Line, 3f);
            ci.DrawLine(new Vector2(60f, y - inward * 14f), new Vector2(W - 60f, y - inward * 14f), InkStyle.WoodDark, 1.5f);
            PortraitGlyph.Diamond(ci, W / 2f, y, 14f, InkStyle.Line);
            PortraitGlyph.Diamond(ci, W / 2f, y, 6f, InkStyle.Bg);
            foreach (var x in new[] { 200f, W - 200f })
                PortraitGlyph.Diamond(ci, x, y, 7f, InkStyle.Dim);
        }
    }

    /// <summary>开门时门缝内沿：骨白边＋暗线＋一列铆珠。</summary>
    private void DrawDoorEdge(CanvasItem ci, bool rightDoor)
    {
        if (_t < DoorStart)
            return;
        var x = rightDoor ? W / 2f + 2f : W / 2f - 2f;
        var inward = rightDoor ? 1f : -1f;
        ci.DrawLine(new Vector2(x, 0), new Vector2(x, H), InkStyle.Line, 5f);
        ci.DrawLine(new Vector2(x + inward * 14f, 0), new Vector2(x + inward * 14f, H), InkStyle.WoodDark, 2f);
        for (var y = 120f; y < H; y += 180f)
            PortraitGlyph.Diamond(ci, x + inward * 30f, y, 7f, InkStyle.Dim);
    }
}
