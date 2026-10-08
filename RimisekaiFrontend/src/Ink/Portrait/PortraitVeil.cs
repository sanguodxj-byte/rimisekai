using System;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>过渡遮罩的中心图标：劳作＝锤与砧，歇息＝新月与烛，其余＝沙漏。</summary>
public enum VeilIcon
{
    Work,
    Rest,
    Wait,
}

/// <summary>
/// 过渡遮罩（移动不用，走王棋行走动画）：设施操作时全屏盖一层半透明的磨砂黑（开演当帧画面缩糊后压暗，再叠黑），中心一枚灰阶图标做小动画
/// （缓缓放大落定、呼吸、外圈一环小菱绕行），图标下一行说明。状态在开演前就已改好，遮罩褪去即露出新画面。
/// 只是表现层：不注册命中块，遮罩期间由 <see cref="PortraitHubScreen"/> 锁输入。
/// </summary>
public sealed partial class PortraitVeil : Control
{
    /// <summary>整段时长（秒）：淡入 0.18、停留、淡出 0.24。</summary>
    public const float Duration = 0.9f;
    private const float FadeIn = 0.18f;
    private const float FadeOut = 0.24f;

    private static readonly Texture2D?[] Icons = new Texture2D?[3];
    private static bool _loaded;

    /// <summary>开演那一刻整屏的磨砂底：截下当帧画面，两级缩到 1/16 再拉回全屏（双线性），即一层柔糊。</summary>
    private ImageTexture? _frost;
    private float _t = 1f;
    private VeilIcon _icon;
    private string _caption = "";

    public bool Running => _t < 1f;
    public float Progress => _t;
    public VeilIcon Icon => _icon;
    public string Caption => _caption;

    public PortraitVeil()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        Visible = false;
        ZIndex = 50;
    }

    /// <summary>截当帧（还是操作前的画面）做磨砂底。截不到就只压黑。</summary>
    private void Grab()
    {
        _frost = null;
        var image = GetViewport()?.GetTexture()?.GetImage();
        if (image == null || image.IsEmpty())
            return;
        image.Convert(Image.Format.Rgb8);
        image.Resize(Math.Max(1, image.GetWidth() / 4), Math.Max(1, image.GetHeight() / 4), Image.Interpolation.Bilinear);
        image.Resize(Math.Max(1, image.GetWidth() / 4), Math.Max(1, image.GetHeight() / 4), Image.Interpolation.Bilinear);
        _frost = ImageTexture.CreateFromImage(image);
    }

    /// <summary>开演。核对模式（<see cref="PortraitMotion.Instant"/>）不演。</summary>
    public void Play(VeilIcon icon, string caption)
    {
        _icon = icon;
        _caption = caption;
        if (PortraitMotion.Instant)
        {
            _t = 1f;
            Visible = false;
            return;
        }
        Grab();
        _t = 0f;
        Visible = true;
        QueueRedraw();
    }

    /// <summary>推进一帧；仍在演时返回 true。</summary>
    public bool Step(float delta)
    {
        if (_t >= 1f)
            return false;
        _t = Mathf.Min(1f, _t + delta / Duration);
        Visible = _t < 1f;
        QueueRedraw();
        return true;
    }

    /// <summary>遮罩浓度：淡入、停留、淡出（各段缓出）。</summary>
    private float Amount()
    {
        var s = _t * Duration;
        if (s < FadeIn)
            return PortraitMotion.EaseOut(s / FadeIn);
        if (s > Duration - FadeOut)
            return 1f - PortraitMotion.EaseOut((s - (Duration - FadeOut)) / FadeOut);
        return 1f;
    }

    public override void _Draw()
    {
        if (!Running)
            return;
        Load();
        var ci = this;
        var a = Amount();
        var full = new Rect2(Vector2.Zero, Size);
        // 磨砂：糊掉的旧画面压暗成灰，再盖一层黑。
        if (_frost != null)
            DrawTextureRect(_frost, full, false, new Color(0.42f, 0.42f, 0.42f, a));
        DrawRect(full, new Color(0f, 0f, 0f, 0.5f * a));
        var s = _t * Duration;
        var c = new Vector2(PortraitLayout.CanvasWidth / 2f, PortraitLayout.CanvasHeight / 2f - 40f);
        // 落定：0.82 → 1 缓出；之后轻微呼吸。
        var settle = 0.82f + 0.18f * PortraitMotion.EaseOut(s / 0.32f);
        var breathe = 1f + 0.03f * Mathf.Sin(s * 7f);
        var size = 260f * settle * breathe;
        // 外圈：细环＋八枚小菱绕行，亮度随位置渐变成一条「拖尾」。
        var ring = 210f * settle;
        ci.DrawArc(c, ring, 0f, Mathf.Tau, 96, new Color(InkStyle.Dim, 0.55f * a), 2f, true);
        ci.DrawArc(c, ring + 14f, 0f, Mathf.Tau, 96, new Color(InkStyle.WoodDark, 0.6f * a), 1.5f, true);
        var spin = s * 2.4f;
        for (var k = 0; k < 8; k++)
        {
            var ang = spin + k * Mathf.Tau / 8f;
            var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring;
            var lit = 0.25f + 0.75f * (k / 7f);
            InkDraw.Jewel(ci, p, 5f + 4f * lit, new Color(InkStyle.Line, lit * a));
        }
        var tex = Icons[(int)_icon];
        if (tex != null)
            ci.DrawTextureRect(tex, new Rect2(c - Vector2.One * size / 2f, Vector2.One * size), false, new Color(1f, 1f, 1f, a));
        if (_caption.Length > 0)
            InkDraw.Text(ci, c + new Vector2(0f, ring + 86f), _caption, PortraitLayout.FontBody, new Color(InkStyle.Line, a), "cm");
    }

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var names = new[] { "tr_work.png", "tr_rest.png", "tr_hourglass.png" };
        for (var i = 0; i < names.Length; i++)
        {
            var path = "res://RimisekaiFrontend/ui/gothic/" + names[i];
            Icons[i] = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : InkIllustration.LoadTexture(path);
        }
    }

    /// <summary>行动归哪枚图标：睡 / 歇 / 沐浴 / 冥想＝歇息；产出类＝劳作；其余＝沙漏。</summary>
    public static VeilIcon IconFor(ActionKind kind) => kind switch
    {
        ActionKind.Sleep or ActionKind.Rest or ActionKind.Bathe or ActionKind.Meditate or ActionKind.Stargaze => VeilIcon.Rest,
        ActionKind.Mine or ActionKind.Fell or ActionKind.Forge or ActionKind.Till or ActionKind.Tend or ActionKind.Woodwork
            or ActionKind.Sew or ActionKind.Brew or ActionKind.Cook or ActionKind.Haul or ActionKind.Train => VeilIcon.Work,
        _ => VeilIcon.Wait,
    };
}
