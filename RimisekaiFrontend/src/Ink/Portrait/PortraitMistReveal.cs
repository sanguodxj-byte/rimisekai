using System;
using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 继续游戏的入场过场（2026-10-11 主人要求补回「雾化散开」）：读档进领地那一刻，整屏盖一层灰白浓雾，
/// 1.6 秒内自正中向四周化开、边缘拉丝飘散，露出领地画面。全程骨白灰阶（mist_reveal.gdshader）。
/// 期间吞掉输入；核对模式（<see cref="PortraitMotion.Instant"/>）不演。
/// </summary>
public sealed partial class PortraitMistReveal : ColorRect
{
    public const float Duration = 1.6f;

    private ShaderMaterial _material = null!;
    private float _t = Duration;
    private bool _frozen;

    public bool Running => Visible && _t < Duration;

    public override void _Ready()
    {
        Size = new Vector2(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        Color = Colors.White;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;
        Visible = false;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://RimisekaiFrontend/shaders/mist_reveal.gdshader") };
        _material.SetShaderParameter("rect_size", Size);
        Material = _material;
        SetProcess(false);
    }

    /// <summary>开演：雾满屏，随后散开。</summary>
    public void Play()
    {
        if (PortraitMotion.Instant)
            return;
        Begin(0f);
        _frozen = false;
        SetProcess(true);
    }

    /// <summary>核对用：停在进度 t 秒处（不推进）。</summary>
    public void DebugSeek(float t)
    {
        Begin(t);
        _frozen = true;
        SetProcess(false);
    }

    public void DebugEnd() => Finish();

    private void Begin(float t)
    {
        _t = Mathf.Clamp(t, 0f, Duration - 0.001f);
        Visible = true;
        GetParent()?.MoveChild(this, -1);
        Apply();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _frozen)
            return;
        _t += (float)delta;
        if (_t >= Duration)
        {
            Finish();
            return;
        }
        Apply();
    }

    /// <summary>进度缓入缓出：开头雾停一拍再散，收尾散得干净。</summary>
    private void Apply()
    {
        var x = Mathf.Clamp(_t / Duration, 0f, 1f);
        var eased = x * x * (3f - 2f * x);
        _material.SetShaderParameter("progress", eased);
    }

    private void Finish()
    {
        Visible = false;
        _frozen = false;
        _t = Duration;
        SetProcess(false);
    }
}
