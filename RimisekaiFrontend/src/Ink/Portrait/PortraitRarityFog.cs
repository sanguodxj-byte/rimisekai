using System.Collections.Generic;
using Godot;
using Rimisekai.Defs;

namespace Rimisekai.Portrait;

/// <summary>
/// 稀有度雾（2026-10-10 主人定）：精致及以上的装备条目压一层对应颜色的雾，
/// 条目里被风从左吹向右（稀有度越高风越大），物品详情页里整片缓缓上飘。
/// 雾色是**唯一**允许出现在骨白调色板之外的色相，只表稀有度，不另作他用。
///
/// 画法：宿主 Control 的子 ColorRect＋<c>rarity_fog.gdshader</c>（加色混合），
/// 每帧宿主 _Draw 前 <see cref="Begin"/>、画到哪条就 <see cref="Place"/> 哪条、画完 <see cref="End"/> 收起没用到的。
/// 子节点不吃鼠标，不登记命中块；裁切视口时把可见部分还原到整条 UV，滚动时花纹不跳。
/// </summary>
public sealed class PortraitRarityFog
{
    private static Shader? _shader;
    private readonly Control _host;
    private readonly List<ColorRect> _pool = new();
    private int _used;

    public PortraitRarityFog(Control host) => _host = host;

    /// <summary>精致以下不起雾。</summary>
    public static bool Shows(Quality quality) => quality >= Quality.Fine;

    /// <summary>雾色：精致蓝、史诗紫、传说橙金、独特血红。</summary>
    public static Color ColorOf(Quality quality) => quality switch
    {
        Quality.Fine => new Color(0.30f, 0.56f, 1.00f),
        Quality.Epic => new Color(0.66f, 0.36f, 1.00f),
        Quality.Legendary => new Color(1.00f, 0.60f, 0.18f),
        Quality.Unique => new Color(0.95f, 0.22f, 0.22f),
        _ => Colors.White,
    };

    /// <summary>风力 0..1：稀有度越高越大。</summary>
    public static float WindOf(Quality quality) => quality switch
    {
        Quality.Fine => 0.15f,
        Quality.Epic => 0.45f,
        Quality.Legendary => 0.75f,
        Quality.Unique => 1.0f,
        _ => 0f,
    };

    public void Begin() => _used = 0;

    /// <summary>
    /// 在 rect 上放一片雾；viewport 非空时只显示落在视口里的部分。rising＝详情页的上飘模式。
    /// rect 为画布坐标，自动叠上当前图层位移。
    /// </summary>
    public void Place(Rect2 rect, Quality quality, bool rising = false, Rect2? viewport = null)
    {
        if (!Shows(quality))
            return;
        var shown = viewport is { } v ? rect.Intersection(v) : rect;
        if (shown.Size.X < 2f || shown.Size.Y < 2f)
            return;
        var fx = Next();
        fx.Position = shown.Position + PortraitFrame.LayerOffset;
        fx.Size = shown.Size;
        var mat = (ShaderMaterial)fx.Material;
        mat.SetShaderParameter("fog_color", ColorOf(quality));
        mat.SetShaderParameter("wind", WindOf(quality));
        mat.SetShaderParameter("mode", rising ? 1 : 0);
        mat.SetShaderParameter("intensity", rising ? 0.34f : 0.85f);
        mat.SetShaderParameter("rect_size", rect.Size);
        mat.SetShaderParameter("clip", new Vector4(
            (shown.Position.X - rect.Position.X) / rect.Size.X, (shown.Position.Y - rect.Position.Y) / rect.Size.Y,
            shown.Size.X / rect.Size.X, shown.Size.Y / rect.Size.Y));
        mat.SetShaderParameter("seed", (_used * 0.618f) % 1f);
        fx.Visible = true;
    }

    public void End()
    {
        for (var i = _used; i < _pool.Count; i++)
            _pool[i].Visible = false;
    }

    private ColorRect Next()
    {
        if (_used == _pool.Count)
        {
            _shader ??= GD.Load<Shader>("res://RimisekaiFrontend/shaders/rarity_fog.gdshader");
            var fx = new ColorRect
            {
                Color = Colors.White,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = new ShaderMaterial { Shader = _shader },
                Visible = false,
            };
            _host.AddChild(fx);
            _pool.Add(fx);
        }
        return _pool[_used++];
    }
}
