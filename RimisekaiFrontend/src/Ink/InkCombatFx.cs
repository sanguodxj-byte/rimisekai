using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Combat;

namespace Rimisekai.Ink;

/// <summary>
/// 全彩 GPU Shader 战斗特效层：
/// 所有攻击动作与受击反馈均由 GPU Shader 渲染，不限制颜色。
/// 包含：
/// 1. 斩击光刃（combat_slash.gdshader）：全彩裂空光弧、灼热核心与飞散火花；
/// 2. 元素爆破（combat_burst.gdshader）：法术能量冲击波、烈焰与极冰冲击环；
/// 3. 远射穿刺（combat_projectile.gdshader）：高速贯穿光束与能量拖尾；
/// 4. 晶体结界与圣光（combat_barrier.gdshader）：六边形能量护盾与升腾神圣光柱；
/// 5. 受击横向高频震颤与彩色动态伤害飘字。
/// </summary>
public partial class InkCombatFxLayer : Control
{
    private static readonly string SlashShaderPath = "res://RimisekaiFrontend/shaders/combat_slash.gdshader";
    private static readonly string BurstShaderPath = "res://RimisekaiFrontend/shaders/combat_burst.gdshader";
    private static readonly string ProjectileShaderPath = "res://RimisekaiFrontend/shaders/combat_projectile.gdshader";
    private static readonly string BarrierShaderPath = "res://RimisekaiFrontend/shaders/combat_barrier.gdshader";

    private Shader? _slashShader;
    private Shader? _burstShader;
    private Shader? _projectileShader;
    private Shader? _barrierShader;

    public enum EffectType
    {
        Slash,
        Burst,
        Projectile,
        Barrier,
    }

    private sealed class ActiveEffect
    {
        public EffectType Type;
        public ColorRect Quad = null!;
        public ShaderMaterial Material = null!;
        public float Time;
        public float Duration;

        public float Progress => Mathf.Clamp(Time / Math.Max(0.001f, Duration), 0f, 1f);
        public bool IsFinished => Time >= Duration;
    }

    private sealed class ActiveShake
    {
        public int UnitId;
        public float Time;
        public float Duration;
        public int Mode; // -1: 我方向上扑击, 1: 敌方向下俯冲, 0: 受击剧烈震颤

        public float Progress => Mathf.Clamp(Time / Math.Max(0.001f, Duration), 0f, 1f);
        public bool IsFinished => Time >= Duration;
    }

    public sealed class CutLine
    {
        public Vector2 Center;
        public float Angle;
        public Vector2 Direction; // 剑光切向单位向量
        public Vector2 Normal;    // 剑光法向单位向量
        public Color GlowColor;

        public CutLine(Vector2 center, float angle, Color glow)
        {
            Center = center;
            Angle = angle;
            Direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Normal = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            GlowColor = glow;
        }

        public float Dist(Vector2 p) => (p.X - Center.X) * Normal.X + (p.Y - Center.Y) * Normal.Y;
    }

    public sealed class CutPiece
    {
        public List<Vector2> Vertices = new();
        public Vector2 Centroid;
        public Vector2 SeparationDir;
        public float RotationDir;
    }

    public sealed class ActiveDeathSlice
    {
        public int UnitId;
        public string Name = "";
        public Rect2 CardRect;
        public float Time;
        public float Duration = 1.05f; // 充足时间展现切开、错位滑移与变透明消散
        public List<CutPiece> Pieces = new();
        public List<CutLine> Cuts = new();
        public Color GlowColor;

        public float Progress => Mathf.Clamp(Time / Math.Max(0.001f, Duration), 0f, 1f);
        public bool IsFinished => Time >= Duration;
    }

    internal sealed class ActivePopup
    {
        public Vector2 Pos;
        public string Text = "";
        public Color Color;
        public float Time;
        public float Duration;

        public float Progress => Mathf.Clamp(Time / Math.Max(0.001f, Duration), 0f, 1f);
        public bool IsFinished => Time >= Duration;
    }

    private readonly List<ActiveEffect> _activeEffects = new();
    private readonly List<ColorRect> _quadPool = new();
    private readonly List<ActiveShake> _activeShakes = new();
    private readonly List<ActivePopup> _activePopups = new();
    private readonly List<ActiveDeathSlice> _deathSlices = new();
    private readonly Dictionary<int, Vector2> _unitOffsets = new();

    /// <summary>飘字专用子层：攻击光效是子节点，子节点永远盖在父节点的 _Draw 之上，
    /// 所以飘字必须自己也是一个**排在光效之后、ZIndex 更高**的子层，才不会被特效盖住。</summary>
    private PopupCanvas? _popupCanvas;

    public static InkCombatFxLayer? Instance { get; private set; }

    public bool HasActiveDeathAnimations => _deathSlices.Count > 0;
    public bool HasActiveAnimations => _activeEffects.Count > 0 || _activeShakes.Count > 0 || _activePopups.Count > 0 || _deathSlices.Count > 0;

    public bool HasDeathAnimation(int unitId)
    {
        for (var i = 0; i < _deathSlices.Count; i++)
        {
            if (_deathSlices[i].UnitId == unitId)
                return true;
        }
        return false;
    }

    public override void _Ready()
    {
        Instance = this;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        // 由宿主屏幕 InkHubScreen._Process 统一单次驱动 UpdateFx，避免双倍加速消耗时长！
        SetProcess(false);

        _slashShader = GD.Load<Shader>(SlashShaderPath);

        _burstShader = GD.Load<Shader>(BurstShaderPath);
        _projectileShader = GD.Load<Shader>(ProjectileShaderPath);
        _barrierShader = GD.Load<Shader>(BarrierShaderPath);

        _popupCanvas = new PopupCanvas { Popups = _activePopups };
        _popupCanvas.SetAnchorsPreset(LayoutPreset.FullRect);
        _popupCanvas.MouseFilter = MouseFilterEnum.Ignore;
        _popupCanvas.ZIndex = 128; // 稳压所有攻击光效子节点
        AddChild(_popupCanvas);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Clear()
    {
        foreach (var fx in _activeEffects)
        {
            fx.Quad.Visible = false;
            _quadPool.Add(fx.Quad);
        }
        _activeEffects.Clear();
        _activeShakes.Clear();
        _activePopups.Clear();
        _deathSlices.Clear();
        _unitOffsets.Clear();
        QueueRedraw();
    }

    public Vector2 GetUnitOffset(int unitId) =>
        _unitOffsets.TryGetValue(unitId, out var offset) ? offset : Vector2.Zero;

    private ColorRect GetPooledQuad(Shader? shader)
    {
        ColorRect quad;
        if (_quadPool.Count > 0)
        {
            quad = _quadPool[^1];
            _quadPool.RemoveAt(_quadPool.Count - 1);
        }
        else
        {
            quad = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(quad);
        }

        var mat = new ShaderMaterial { Shader = shader };
        quad.Material = mat;
        quad.Visible = true;

        return quad;
    }

    /// <summary>
    /// 从战斗战况事件中捕获并生成对应的全彩 GPU Shader 攻击与受击特效。
    /// </summary>
    public void SpawnFromEvent(BattleEvent ev, Battle battle, Func<int, Vector2> getUnitCenter)
    {
        var actorPos = getUnitCenter(ev.ActorId);
        var targetPos = getUnitCenter(ev.TargetId);

        var actor = battle.Members.Find(m => m.Id == ev.ActorId);
        var isPlayerSide = actor?.Side == battle.ControlledSide;

        switch (ev.Kind)
        {
            case CombatEventKind.Hit:
            {
                // 1. 攻击者扑击位移动画
                _activeShakes.Add(new ActiveShake
                {
                    UnitId = ev.ActorId,
                    Duration = 0.30f,
                    Mode = isPlayerSide ? -1 : 1,
                });

                // 2. 根据技能种类生成专属 GPU Shader 全彩光效，并自适应捕获剑光切线
                var def = battle.Lookup(ev.SkillId ?? "");
                SpawnSkillShaderEffect(def, actorPos, targetPos, out var cuts, out var glowColor);

                // 3. 受击者受创横向剧烈震颤
                _activeShakes.Add(new ActiveShake
                {
                    UnitId = ev.TargetId,
                    Duration = 0.28f,
                    Mode = 0,
                });

                // 4. 纯净线稿伤害跳字（遵守黑白铜版规范，严禁彩色文字）
                var isCritical = ev.SkillId == "cross_slash" || battle.AwakeningActive;
                var popupColor = isCritical ? InkStyle.Line : new Color(InkStyle.Line, 0.85f);

                // 主人定：飘字减慢消失，停留更久更醒目
                _activePopups.Add(new ActivePopup
                {
                    Pos = targetPos,
                    Text = $"-{ev.Amount}",
                    Color = popupColor,
                    Duration = 1.35f,
                });

                // 5. 主人定：仅在死亡回合激活该动画——沿剑光将怪物卡片切开，然后变透明消散。
                // 剑光位置可能会变化或者增加几道，自动根据上面的 cuts 列表自适应切分！
                if (ev.HpAfter <= 0)
                {
                    var target = battle.Members.Find(m => m.Id == ev.TargetId);
                    if (target != null && target.Side != battle.ControlledSide)
                    {
                        var cardRect = InkCombatRenderer.GetEnemyCardRect(battle, target.Id);
                        SpawnDeathSlice(target.Id, target.Name, cardRect, cuts, glowColor);
                    }
                }
                break;
            }

            case CombatEventKind.Miss:
            {
                _activeShakes.Add(new ActiveShake
                {
                    UnitId = ev.ActorId,
                    Duration = 0.25f,
                    Mode = isPlayerSide ? -1 : 1,
                });
                _activePopups.Add(new ActivePopup
                {
                    Pos = targetPos,
                    Text = "闪避",
                    Color = new Color("#B0BEC5"),
                    Duration = 1.15f,
                });
                break;
            }

            case CombatEventKind.Heal:
            {
                // 治疗全彩圣光升腾 Shader
                SpawnBarrierShaderEffect(targetPos, isHeal: true, new Color("#76FF03"));
                _activePopups.Add(new ActivePopup
                {
                    Pos = targetPos,
                    Text = $"+{ev.Amount}",
                    Color = new Color("#00E676"),
                    Duration = 1.35f,
                });
                break;
            }

            case CombatEventKind.Status:
            {
                if (ev.SkillId == BattleSkills.GuardId || ev.SkillId == "iron_wall")
                {
                    // 防御晶体结界 Shader
                    SpawnBarrierShaderEffect(actorPos, isHeal: false, new Color("#2979FF"));
                }
                break;
            }

            case CombatEventKind.Dot:
            {
                _activeShakes.Add(new ActiveShake
                {
                    UnitId = ev.TargetId,
                    Duration = 0.22f,
                    Mode = 0,
                });
                _activePopups.Add(new ActivePopup
                {
                    Pos = targetPos,
                    Text = $"-{ev.Amount}",
                    Color = new Color("#7C4DFF"), // 毒素紫光
                    Duration = 1.25f,
                });

                if (ev.HpAfter <= 0)
                {
                    var target = battle.Members.Find(m => m.Id == ev.TargetId);
                    if (target != null && target.Side != battle.ControlledSide)
                    {
                        var cardRect = InkCombatRenderer.GetEnemyCardRect(battle, target.Id);
                        var dotCuts = new List<CutLine> { new(targetPos, -0.58f, new Color("#7C4DFF")) };
                        SpawnDeathSlice(target.Id, target.Name, cardRect, dotCuts, new Color("#7C4DFF"));
                    }
                }
                break;
            }
        }
    }

    private void SpawnSkillShaderEffect(SkillDef? def, Vector2 actorPos, Vector2 targetPos, out List<CutLine> cuts, out Color primaryGlow)
    {
        cuts = new List<CutLine>();
        primaryGlow = new Color("#2979FF");
        if (def == null)
            def = BattleSkills.Attack;

        if (def.Kind == SkillKind.Spell)
        {
            // 元素法术爆破 Shader
            var (coreColor, glowColor) = def.Id switch
            {
                "flame_burst" => (new Color("#FFF3E0"), new Color("#FF3D00")), // 烈焰橙红
                "frost_bind" => (new Color("#E0F7FA"), new Color("#00E5FF")),  // 极冰冰青
                _ => (new Color("#EDE7F6"), new Color("#D500F9")),             // 奥术秘紫
            };
            primaryGlow = glowColor;
            SpawnBurstShader(targetPos, coreColor, glowColor);
            // 元素能量爆破沿斜向对角线裂开
            cuts.Add(new CutLine(targetPos, -0.60f, glowColor));
        }
        else if (def.Range == SkillRange.Ranged)
        {
            // 远射光束刺线 Shader
            primaryGlow = new Color("#FFEA00");
            SpawnProjectileShader(actorPos, targetPos, primaryGlow);
            var dir = targetPos - actorPos;
            var angle = Mathf.Atan2(dir.Y, dir.X);
            cuts.Add(new CutLine(targetPos, angle, primaryGlow));
        }
        else
        {
            // 纯近战斩击光弧 Shader：支持单道或多道剑光自适应切开
            var (coreColor, glowColor, angle, isDouble) = def.Id switch
            {
                "slash" => (new Color("#FFFFFF"), new Color("#2979FF"), -0.58f, false), // 疾斩：纯净电光宝蓝（严禁发绿）
                "armor_break" => (new Color("#FFFDE7"), new Color("#FF1744"), -0.68f, false),// 破甲：左侧熔岩猩红大剑光
                "cross_slash" => (new Color("#FFFFFF"), new Color("#D500F9"), -0.58f, true), // 二连斩：雷电紫芒交叉大斩
                _ => (new Color("#FFFFF0"), new Color("#FF9100"), -0.55f, false),            // 普攻：炽阳烈焰大剑气
            };
            primaryGlow = glowColor;
            SpawnSlashShader(targetPos, coreColor, glowColor, angle);
            cuts.Add(new CutLine(targetPos, angle, glowColor));
            if (isDouble)
            {
                var crossGlow = new Color("#00E5FF");
                SpawnSlashShader(targetPos, coreColor, crossGlow, 0.78f);
                cuts.Add(new CutLine(targetPos, 0.78f, crossGlow));
            }
        }
    }

    public void SpawnSlashShader(Vector2 center, Color core, Color glow, float angle, float duration = 0.45f)
    {
        var quad = GetPooledQuad(_slashShader);
        // 左侧磅礴巨型大剑光：宽 1050px、高 680px，自左侧我方队伍狂暴横扫全场目标
        var size = new Vector2(1050f, 680f);
        quad.Position = center - size / 2f;
        quad.Size = size;

        var mat = (ShaderMaterial)quad.Material;
        mat.SetShaderParameter("color_core", core);
        mat.SetShaderParameter("color_glow", glow);
        mat.SetShaderParameter("angle", angle);
        mat.SetShaderParameter("quad_size", size);
        mat.SetShaderParameter("slash_width_px", 12.0f);
        mat.SetShaderParameter("progress", 0.0f);

        _activeEffects.Add(new ActiveEffect
        {
            Type = EffectType.Slash,
            Quad = quad,
            Material = mat,
            Duration = duration,
        });
    }

    public void SpawnBurstShader(Vector2 center, Color core, Color outer, float duration = 0.42f)
    {
        var quad = GetPooledQuad(_burstShader);
        var size = new Vector2(280f, 280f);
        quad.Position = center - size / 2f;
        quad.Size = size;

        var mat = (ShaderMaterial)quad.Material;
        mat.SetShaderParameter("color_core", core);
        mat.SetShaderParameter("color_outer", outer);
        mat.SetShaderParameter("progress", 0.0f);

        _activeEffects.Add(new ActiveEffect
        {
            Type = EffectType.Burst,
            Quad = quad,
            Material = mat,
            Duration = duration,
        });
    }

    public void SpawnProjectileShader(Vector2 start, Vector2 target, Color color, float duration = 0.32f)
    {
        var quad = GetPooledQuad(_projectileShader);
        var dir = target - start;
        var len = dir.Length();
        var angle = Mathf.Atan2(dir.Y, dir.X);

        var size = new Vector2(Mathf.Max(300f, len * 1.2f), 140f);
        quad.Position = (start + target) / 2f - size / 2f;
        quad.Size = size;

        var mat = (ShaderMaterial)quad.Material;
        mat.SetShaderParameter("color_beam", color);
        mat.SetShaderParameter("angle", angle);
        mat.SetShaderParameter("progress", 0.0f);

        _activeEffects.Add(new ActiveEffect
        {
            Type = EffectType.Projectile,
            Quad = quad,
            Material = mat,
            Duration = duration,
        });
    }

    public void SpawnBarrierShaderEffect(Vector2 center, bool isHeal, Color color, float duration = 0.40f)
    {
        var quad = GetPooledQuad(_barrierShader);
        var size = new Vector2(240f, 240f);
        quad.Position = center - size / 2f;
        quad.Size = size;

        var mat = (ShaderMaterial)quad.Material;
        mat.SetShaderParameter("color_fx", color);
        mat.SetShaderParameter("mode", isHeal ? 1 : 0);
        mat.SetShaderParameter("progress", 0.0f);

        _activeEffects.Add(new ActiveEffect
        {
            Type = EffectType.Barrier,
            Quad = quad,
            Material = mat,
            Duration = duration,
        });
    }

    public void UpdateFx(float delta)
    {
        UpdateEffects(delta);
        UpdateShakes(delta);
        UpdatePopups(delta);
        UpdateDeathSlices(delta);
        if (HasActiveAnimations)
            QueueRedraw();
        if (_activePopups.Count > 0)
            _popupCanvas?.QueueRedraw();
    }

    public void SpawnDeathSlice(int targetId, string name, Rect2 cardRect, List<CutLine> cuts, Color glowColor)
    {
        var initialRect = new List<Vector2>
        {
            cardRect.Position,
            new Vector2(cardRect.End.X, cardRect.Position.Y),
            cardRect.End,
            new Vector2(cardRect.Position.X, cardRect.End.Y),
        };

        if (cuts.Count == 0)
        {
            cuts.Add(new CutLine(cardRect.GetCenter(), -0.58f, glowColor));
        }

        var currentPolys = new List<List<Vector2>> { initialRect };
        foreach (var cut in cuts)
        {
            var nextPolys = new List<List<Vector2>>();
            foreach (var p in currentPolys)
            {
                var split = SplitConvexPolygon(p, cut);
                if (split.Count > 0)
                    nextPolys.AddRange(split);
                else
                    nextPolys.Add(p);
            }
            currentPolys = nextPolys;
        }

        var pieces = new List<CutPiece>();
        foreach (var poly in currentPolys)
        {
            var centroid = Vector2.Zero;
            foreach (var v in poly)
                centroid += v;
            centroid /= poly.Count;

            var sepDir = Vector2.Zero;
            var rotDir = 0f;
            foreach (var cut in cuts)
            {
                var d = cut.Dist(centroid);
                var sgn = d >= 0f ? 1f : -1f;
                // 沿刀光法向有力推开（42px），叠加顺着切向的滑移分量（24px），并产生轻微断裂力矩
                sepDir += sgn * (cut.Normal * 42f + cut.Direction * 24f);
                rotDir += sgn * 0.055f;
            }

            pieces.Add(new CutPiece
            {
                Vertices = poly,
                Centroid = centroid,
                SeparationDir = sepDir,
                RotationDir = rotDir,
            });
        }

        _deathSlices.Add(new ActiveDeathSlice
        {
            UnitId = targetId,
            Name = name,
            CardRect = cardRect,
            Duration = 1.05f,
            Pieces = pieces,
            Cuts = cuts,
            GlowColor = glowColor,
        });

        QueueRedraw();
    }

    public static List<List<Vector2>> SplitConvexPolygon(List<Vector2> poly, CutLine cut)
    {
        var polyA = new List<Vector2>();
        var polyB = new List<Vector2>();
        var n = poly.Count;
        for (var i = 0; i < n; i++)
        {
            var p1 = poly[i];
            var p2 = poly[(i + 1) % n];
            var d1 = cut.Dist(p1);
            var d2 = cut.Dist(p2);

            if (d1 >= -1e-4f)
            {
                if (d2 >= -1e-4f)
                {
                    polyA.Add(p2);
                }
                else
                {
                    var t = -d1 / (d2 - d1);
                    var ix = p1.X + t * (p2.X - p1.X);
                    var iy = p1.Y + t * (p2.Y - p1.Y);
                    polyA.Add(new Vector2(ix, iy));
                }
            }
            else
            {
                if (d2 >= -1e-4f)
                {
                    var t = -d1 / (d2 - d1);
                    var ix = p1.X + t * (p2.X - p1.X);
                    var iy = p1.Y + t * (p2.Y - p1.Y);
                    polyA.Add(new Vector2(ix, iy));
                    polyA.Add(p2);
                }
            }

            if (d1 <= 1e-4f)
            {
                if (d2 <= 1e-4f)
                {
                    polyB.Add(p2);
                }
                else
                {
                    var t = -d1 / (d2 - d1);
                    var ix = p1.X + t * (p2.X - p1.X);
                    var iy = p1.Y + t * (p2.Y - p1.Y);
                    polyB.Add(new Vector2(ix, iy));
                }
            }
            else
            {
                if (d2 <= 1e-4f)
                {
                    var t = -d1 / (d2 - d1);
                    var ix = p1.X + t * (p2.X - p1.X);
                    var iy = p1.Y + t * (p2.Y - p1.Y);
                    polyB.Add(new Vector2(ix, iy));
                    polyB.Add(p2);
                }
            }
        }

        var res = new List<List<Vector2>>();
        if (polyA.Count >= 3) res.Add(polyA);
        if (polyB.Count >= 3) res.Add(polyB);
        return res.Count > 0 ? res : new List<List<Vector2>> { poly };
    }

    private void UpdateDeathSlices(float delta)
    {
        for (var i = _deathSlices.Count - 1; i >= 0; i--)
        {
            var ds = _deathSlices[i];
            ds.Time += delta;
            if (ds.IsFinished)
                _deathSlices.RemoveAt(i);
        }
    }

    public override void _Process(double delta)
    {
        UpdateFx((float)delta);
    }
    private void _OldProcess(double delta)
    {
        var dt = (float)delta;
        UpdateEffects(dt);
        UpdateShakes(dt);
        UpdatePopups(dt);

        if (HasActiveAnimations)
            QueueRedraw();
    }

    private void UpdateEffects(float delta)
    {
        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var fx = _activeEffects[i];
            fx.Time += delta;
            fx.Material.SetShaderParameter("progress", fx.Progress);


            if (fx.IsFinished)
            {
                fx.Quad.Visible = false;
                _quadPool.Add(fx.Quad);
                _activeEffects.RemoveAt(i);
            }
        }
    }

    private void UpdateShakes(float delta)
    {
        _unitOffsets.Clear();
        for (var i = _activeShakes.Count - 1; i >= 0; i--)
        {
            var s = _activeShakes[i];
            s.Time += delta;
            var p = s.Progress;
            var curve = Mathf.Sin(p * Mathf.Pi);

            if (s.Mode == -1) // 我方冲刺
                _unitOffsets[s.UnitId] = new Vector2(0f, -22f * curve);
            else if (s.Mode == 1) // 敌方俯冲
                _unitOffsets[s.UnitId] = new Vector2(0f, 24f * curve);
            else // 受击剧烈震颤
            {
                var shake = Mathf.Sin(p * 45f) * 11f * (1f - p);
                _unitOffsets[s.UnitId] = new Vector2(shake, 0f);
            }

            if (s.IsFinished)
                _activeShakes.RemoveAt(i);
        }
    }

    private void UpdatePopups(float delta)
    {
        for (var i = _activePopups.Count - 1; i >= 0; i--)
        {
            var p = _activePopups[i];
            p.Time += delta;
            if (p.IsFinished)
                _activePopups.RemoveAt(i);
        }
    }

    public override void _Draw()
    {
        // 沿剑光切开、然后变透明消散的死亡动画
        foreach (var ds in _deathSlices)
        {
            var p = ds.Progress;
            // 三次缓动曲线（1 - (1-p)^3）：受击瞬间即刻利落破开错位，随后平稳滑移
            var ease = 1.0f - Mathf.Pow(1.0f - p, 3.0f);

            // 前 30% 刀痕破开、切缝炽热辉光，完全不透明；后 70% 边滑开边平滑淡出到 0
            var alpha = p < 0.30f ? 1.0f : Math.Clamp(1.0f - (p - 0.30f) / 0.70f, 0f, 1f);
            if (alpha <= 0.001f)
                continue;

            var bgCol = new Color(InkStyle.Bg, 0.92f * alpha);
            var lineCol = new Color(InkStyle.Line, alpha);
            var dimCol = new Color(InkStyle.Dim, 0.65f * alpha);

            // 1. 绘制各破片多边形
            foreach (var piece in ds.Pieces)
            {
                var offset = piece.SeparationDir * ease;
                var rot = piece.RotationDir * ease;
                var c = piece.Centroid;

                var shifted = new Vector2[piece.Vertices.Count];
                for (var i = 0; i < piece.Vertices.Count; i++)
                {
                    var v = piece.Vertices[i];
                    var rel = v - c;
                    var cos = Mathf.Cos(rot);
                    var sin = Mathf.Sin(rot);
                    var rotated = new Vector2(rel.X * cos - rel.Y * sin, rel.X * sin + rel.Y * cos);
                    shifted[i] = c + rotated + offset;
                }

                // 破片深黑背景填充
                DrawColoredPolygon(shifted, bgCol);

                // 破片外轮廓与断口切面双线描边
                var closed = new Vector2[shifted.Length + 1];
                Array.Copy(shifted, closed, shifted.Length);
                closed[^1] = shifted[0];
                DrawPolyline(closed, lineCol, 1.6f, antialiased: true);

                var inner = new Vector2[shifted.Length + 1];
                var pieceCenter = c + offset;
                for (var i = 0; i < shifted.Length; i++)
                    inner[i] = shifted[i].Lerp(pieceCenter, 0.08f);
                inner[^1] = inner[0];
                DrawPolyline(inner, dimCol, 1.0f, antialiased: true);
            }

            // 2. 切缝刀痕炽热辉光：闪电般贯穿整张卡片的刀痕裂隙
            if (p < 0.65f)
            {
                var seamAlpha = (1.0f - p / 0.65f) * alpha;
                var glow = new Color(ds.GlowColor.R, ds.GlowColor.G, ds.GlowColor.B, seamAlpha);
                foreach (var cut in ds.Cuts)
                {
                    var len = ds.CardRect.Size.Length() * 0.85f;
                    var p1 = cut.Center - cut.Direction * len;
                    var p2 = cut.Center + cut.Direction * len;
                    DrawLine(p1, p2, glow, 4.5f * (1f - p), antialiased: true);
                    DrawLine(p1, p2, new Color(1f, 1f, 1f, seamAlpha), 1.8f * (1f - p), antialiased: true);
                }
            }

            // 3. 怪物名称：落在对应破片上（随破片位移平滑淡出）
            if (ds.Pieces.Count > 0 && ds.Name.Length > 0)
            {
                var upperPiece = ds.Pieces[0];
                var offset = upperPiece.SeparationDir * ease;
                var namePos = new Vector2(ds.CardRect.GetCenter().X, ds.CardRect.Position.Y + 24f) + offset;
                InkDraw.Text(this, namePos, ds.Name, 18, lineCol, "cm");
            }
        }
    }
}

/// <summary>
/// 伤害飘字专用子画布：独立于特效层，ZIndex 最高，保证飘字永远压在攻击光效之上。
/// 主人定：飘字成绩要最高、不能被攻击特效覆盖。
/// </summary>
internal sealed partial class PopupCanvas : Control
{
    public List<InkCombatFxLayer.ActivePopup> Popups = new();

    public override void _Process(double delta)
    {
        if (Popups.Count > 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var p in Popups)
        {
            var prog = p.Progress;
            // 前 16% 弹出放大（0.72 -> 1.18），之后平稳回落到标准 1.0
            var pop = prog < 0.16f
                ? Mathf.Lerp(0.72f, 1.18f, prog / 0.16f)
                : Mathf.Lerp(1.18f, 1.0f, (prog - 0.16f) / 0.84f);

            // 主人定：飘字减慢消失。前 65% 时间保持 100% 完全不透明高亮，清晰读数；后 35% 时间才舒缓淡出。
            var alpha = prog < 0.65f
                ? 1.0f
                : 1.0f - (prog - 0.65f) / 0.35f;

            // 舒缓平滑上浮
            var driftY = p.Pos.Y - 28f - prog * 44f;
            var rect = new Rect2(p.Pos.X - 110f, driftY, 220f, 52f);
            var col = new Color(p.Color.R, p.Color.G, p.Color.B, alpha);
            var size = Math.Max(22, (int)(44f * pop));
            InkDraw.Text(this, rect.GetCenter(), p.Text, size, col, "cm");
        }
    }
}

/// <summary>
/// 静态桥接类，供外界透明调用 InkCombatFxLayer。
/// </summary>
public static class InkCombatFx
{
    public static bool HasActiveAnimations => InkCombatFxLayer.Instance?.HasActiveAnimations ?? false;
    public static bool HasActiveDeathAnimations => InkCombatFxLayer.Instance?.HasActiveDeathAnimations ?? false;
    public static bool HasDeathAnimation(int unitId) => InkCombatFxLayer.Instance?.HasDeathAnimation(unitId) ?? false;

    public static Vector2 GetOffset(int unitId) =>
        InkCombatFxLayer.Instance?.GetUnitOffset(unitId) ?? Vector2.Zero;

    public static void Update(float delta) =>
        InkCombatFxLayer.Instance?.UpdateFx(delta);

    public static void Clear() =>
        InkCombatFxLayer.Instance?.Clear();

    public static void SpawnFromEvent(BattleEvent ev, Battle battle, Func<int, Vector2> getUnitCenter) =>
        InkCombatFxLayer.Instance?.SpawnFromEvent(ev, battle, getUnitCenter);
}
