using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 全局进度条动态动效系统（血量、经验等）。
/// 规则（主人定 2026-10-02）：
/// 1. 血量增长：向右延伸一个绿色条到指定位置，之后白色条跟进；
/// 2. 血量扣减：反过来，向左延伸一个暗红色条（残影缓冲），然后向左消除掉；
/// 3. 经验等进度条：平滑推进生长。
/// </summary>
public static class InkDynamicMeter
{
    public static readonly Color ColorWhite = InkStyle.Line;
    public static readonly Color ColorGreen = new(0.24f, 0.75f, 0.38f);
    public static readonly Color ColorDarkRed = new(0.72f, 0.16f, 0.18f);

    private enum Mode
    {
        Idle,
        Gain, // 血量增长（绿色先伸，白色跟进）
        Loss, // 血量扣减（白色骤降，暗红滞后并向左消除）
    }

    private sealed class MeterState
    {
        public float WhiteRatio;      // 白色主条比例
        public float GhostRatio;      // 绿色先导条 或 暗红残影条比例
        public float TargetRatio;     // 最终目标比例
        public Mode Mode = Mode.Idle;
        public float DelayTimer;      // 扣减后的短暂停顿
        public bool Initialized;
    }

    private static readonly Dictionary<string, MeterState> States = new();

    public static bool HasActiveAnimations { get; private set; }

    /// <summary>每帧更新所有活跃进度条的补间状态。</summary>
    public static void Update(float delta)
    {
        var active = false;

        foreach (var state in States.Values)
        {
            if (!state.Initialized)
                continue;

            switch (state.Mode)
            {
                case Mode.Gain:
                {
                    active = true;
                    // 第一阶段：绿色条向右延伸到目标位置
                    if (state.GhostRatio < state.TargetRatio)
                    {
                        state.GhostRatio = Mathf.MoveToward(state.GhostRatio, state.TargetRatio, delta * 2.2f);
                    }
                    // 第二阶段：白色条随后跟进向右延伸
                    else if (state.WhiteRatio < state.TargetRatio)
                    {
                        state.WhiteRatio = Mathf.MoveToward(state.WhiteRatio, state.TargetRatio, delta * 1.5f);
                    }
                    else
                    {
                        state.WhiteRatio = state.TargetRatio;
                        state.GhostRatio = state.TargetRatio;
                        state.Mode = Mode.Idle;
                    }
                    break;
                }

                case Mode.Loss:
                {
                    active = true;
                    // 第一阶段：白色条迅速降至目标位置（向左收缩）
                    if (state.WhiteRatio > state.TargetRatio)
                    {
                        state.WhiteRatio = Mathf.MoveToward(state.WhiteRatio, state.TargetRatio, delta * 3.5f);
                    }
                    // 短暂缓冲停顿，让暗红色伤害条清晰可见
                    else if (state.DelayTimer > 0f)
                    {
                        state.DelayTimer -= delta;
                    }
                    // 第二阶段：暗红色条向左消除掉（追随白色条）
                    else if (state.GhostRatio > state.TargetRatio)
                    {
                        state.GhostRatio = Mathf.MoveToward(state.GhostRatio, state.TargetRatio, delta * 1.6f);
                    }
                    else
                    {
                        state.WhiteRatio = state.TargetRatio;
                        state.GhostRatio = state.TargetRatio;
                        state.Mode = Mode.Idle;
                    }
                    break;
                }

                case Mode.Idle:
                {
                    // 普通经验条或静止微调平滑
                    if (Mathf.Abs(state.WhiteRatio - state.TargetRatio) > 0.002f)
                    {
                        active = true;
                        state.WhiteRatio = Mathf.MoveToward(state.WhiteRatio, state.TargetRatio, delta * 1.6f);
                        state.GhostRatio = state.WhiteRatio;
                    }
                    else
                    {
                        state.WhiteRatio = state.TargetRatio;
                        state.GhostRatio = state.TargetRatio;
                    }
                    break;
                }
            }
        }

        HasActiveAnimations = active;
    }

    /// <summary>绘制动态进度条。</summary>
    public static void Draw(CanvasItem ci, string key, Rect2 r, float targetRatio, bool isHp = false)
    {
        targetRatio = Mathf.Clamp(targetRatio, 0f, 1f);

        if (!States.TryGetValue(key, out var state))
        {
            state = new MeterState
            {
                WhiteRatio = targetRatio,
                GhostRatio = targetRatio,
                TargetRatio = targetRatio,
                Initialized = true,
                Mode = Mode.Idle,
            };
            States[key] = state;
        }

        // 目标值发生变化时触发对应的动效状态机
        if (Mathf.Abs(targetRatio - state.TargetRatio) > 0.003f)
        {
            state.TargetRatio = targetRatio;

            if (isHp)
            {
                if (targetRatio > state.WhiteRatio)
                {
                    // 血量增长：向右延伸绿色条，之后白色条跟进
                    state.Mode = Mode.Gain;
                    // 若此前不在增长模式，则绿条从当前白条位置开始向右延伸
                    if (state.GhostRatio < state.WhiteRatio)
                        state.GhostRatio = state.WhiteRatio;
                }
                else
                {
                    // 血量扣减：白色条骤降，暗红色向左消除
                    state.Mode = Mode.Loss;
                    state.DelayTimer = 0.22f; // 0.22秒暗红残影停顿展示
                    if (state.GhostRatio < state.WhiteRatio)
                        state.GhostRatio = state.WhiteRatio;
                }
            }
            else
            {
                // 经验等进度条：平滑推进
                state.Mode = Mode.Idle;
            }

            HasActiveAnimations = true;
        }

        // 1. 底槽与边框绘制
        ci.DrawRect(r, InkStyle.Inset);
        InkDraw.Ink(ci, new[]
        {
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
            r.Position,
        }, InkStyle.Dim, 0.8f, 0.2f, 9200);

        var innerX = r.Position.X + 1f;
        var innerY = r.Position.Y + 1f;
        var innerW = r.Size.X - 2f;
        var innerH = r.Size.Y - 2f;
        if (innerW <= 0f || innerH <= 0f)
            return;

        // 2. 动效色块填充
        if (isHp)
        {
            if (state.Mode == Mode.Gain)
            {
                // 增长态：底层绿条（从 0 到 GhostRatio），上层白条（从 0 到 WhiteRatio）
                // 视觉上：先看到绿色向右延伸，随后白色跟进覆盖
                var greenW = innerW * state.GhostRatio;
                if (greenW > 0f)
                    ci.DrawRect(new Rect2(innerX, innerY, greenW, innerH), ColorGreen);

                var whiteW = innerW * state.WhiteRatio;
                if (whiteW > 0f)
                    ci.DrawRect(new Rect2(innerX, innerY, whiteW, innerH), ColorWhite);
            }
            else if (state.Mode == Mode.Loss)
            {
                // 扣减态：底层暗红条（从 0 到 GhostRatio），上层白条（从 0 到 WhiteRatio）
                // 视觉上：白色条迅速回撤，右侧暴露出暗红色掉血残影，随后暗红向左消除
                var redW = innerW * state.GhostRatio;
                if (redW > 0f)
                    ci.DrawRect(new Rect2(innerX, innerY, redW, innerH), ColorDarkRed);

                var whiteW = innerW * state.WhiteRatio;
                if (whiteW > 0f)
                    ci.DrawRect(new Rect2(innerX, innerY, whiteW, innerH), ColorWhite);
            }
            else
            {
                // 稳定态：纯白填充
                var whiteW = innerW * state.WhiteRatio;
                if (whiteW > 0f)
                    ci.DrawRect(new Rect2(innerX, innerY, whiteW, innerH), ColorWhite);
            }
        }
        else
        {
            // 经验/普通进度条：平滑推进生长
            var fillW = innerW * state.WhiteRatio;
            if (fillW > 0f)
            {
                ci.DrawRect(new Rect2(innerX, innerY, fillW, innerH), ColorWhite);

                // 推进中前缘微光
                if (state.WhiteRatio < state.TargetRatio - 0.01f)
                {
                    var headW = Mathf.Min(4f, fillW);
                    ci.DrawRect(new Rect2(innerX + fillW - headW, innerY, headW, innerH),
                        new Color(1f, 1f, 1f, 0.95f));
                }
            }
        }
    }

    /// <summary>测试与调试用：手动触发血量扣减或增长。</summary>
    public static void DebugSet(string key, float current, float target, bool isHp = true)
    {
        States[key] = new MeterState
        {
            WhiteRatio = current,
            GhostRatio = current,
            TargetRatio = target,
            Initialized = true,
            Mode = target > current ? Mode.Gain : Mode.Loss,
            DelayTimer = target < current ? 0.22f : 0f,
        };
        HasActiveAnimations = true;
    }
}
