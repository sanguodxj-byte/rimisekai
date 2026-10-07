using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏动效的公共口径：一律 0.22 秒、三次缓出。核对 / 截图工具把 <see cref="Instant"/> 打开，
/// 一切过渡当场跳到终态，截图与命中块不受动画中间帧影响。
/// </summary>
public static class PortraitMotion
{
    /// <summary>抽屉、推入页、页签药丸的过渡时长（秒）。</summary>
    public const float Duration = 0.22f;

    /// <summary>松手后按压浅填的淡出时长（秒）。</summary>
    public const float PressFade = 0.18f;

    /// <summary>长按判定时长（秒）：领地格长按看设施。</summary>
    public const float LongPress = 0.45f;

    /// <summary>核对模式：动效直接到终态。</summary>
    public static bool Instant { get; set; }

    /// <summary>三次缓出：t∈[0,1] → 1-(1-t)³。</summary>
    public static float EaseOut(float t)
    {
        var u = 1f - Mathf.Clamp(t, 0f, 1f);
        return 1f - u * u * u;
    }
}

/// <summary>一段从 0 走到 1 的过渡。Start 归零（核对模式直接置 1），Step 按帧推进。</summary>
public sealed class PortraitTransition
{
    private readonly float _duration;

    public PortraitTransition(float duration = PortraitMotion.Duration) => _duration = duration;

    public float T { get; private set; } = 1f;

    public bool Running => T < 1f;

    /// <summary>缓出后的进度。</summary>
    public float Eased => PortraitMotion.EaseOut(T);

    public void Start() => T = PortraitMotion.Instant ? 1f : 0f;

    public void Finish() => T = 1f;

    /// <summary>推进一帧；本帧仍在走（需要重画）时返回 true。</summary>
    public bool Step(float delta)
    {
        if (T >= 1f)
            return false;
        T = Mathf.Min(1f, T + delta / _duration);
        return true;
    }
}
