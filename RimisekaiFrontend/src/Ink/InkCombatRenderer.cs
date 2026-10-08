using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Combat;

namespace Rimisekai.Ink;

/// <summary>
/// 战斗场景渲染器：纯绘制，无额外节点容器，所有交互由主界面统一处理。
/// 仅负责两项主人明确允许的画面元素：
/// 1. 中上方速度跑条；
/// 2. 敌方 4x4 透视网格（无外框）。
/// </summary>
public static class InkCombatRenderer
{
    private static readonly Dictionary<int, float> TimelineMarkerY = new();
    private static readonly Dictionary<int, float> EnemyHpRatios = new();
    private static readonly Dictionary<int, float> BossHpRatios = new();
    private static readonly Dictionary<int, Vector2> EnemyPositions = new();
    private static readonly Dictionary<int, float> EnemyScales = new();
    public static bool HasActiveAnimations { get; private set; }

    /// <summary>行动指示大钝角三角形的呼吸动画时间戳（秒）。</summary>
    public static float IndicatorTime { get; set; }

    private static readonly float[] RowHeights = { 150f, 118f, 92f, 72f };

    /// <summary>跑条标记头像边长（1:1）；有立绘就画立绘，没有就退回名字小牌。</summary>
    private const float TimelineAvatar = 42f;

    /// <summary>
    /// 绘制左上角竖向跑条（地图名称下方）：顶端为出手线，向下依剩余时间展开。
    /// 主人定：标记用**头像**展示——我方在导轨左车道、敌方在右车道，
    /// 有立绘用立绘，没有立绘退回名字小牌（内容缺失不是画不出来的理由）。
    /// </summary>
    public static void DrawVerticalTimeline(CanvasItem ci, Rect2 rect, Battle battle)
    {
        var trackX = rect.Position.X + rect.Size.X / 2f;
        var topY = rect.Position.Y + 24f;
        var bottomY = rect.End.Y - 12f;

        // 顶端出手基准线：横向双线刻度
        ci.DrawLine(new Vector2(rect.Position.X + 8f, topY), new Vector2(rect.End.X - 8f, topY), InkStyle.Line, 2f, antialiased: true);
        ci.DrawLine(new Vector2(rect.Position.X + 16f, topY - 3f), new Vector2(rect.End.X - 16f, topY - 3f), new Color(InkStyle.Line, 0.45f), 1f, antialiased: true);

        // 中央垂直导轨
        ci.DrawLine(new Vector2(trackX, topY), new Vector2(trackX, bottomY), InkStyle.Dim, 1.2f, antialiased: true);

        var alive = battle.Members.FindAll(m => m.Alive);
        if (alive.Count == 0)
            return;

        var maxRemaining = alive.Max(m => Math.Max(0L, (m.Chanting != null ? m.ChantFireAt : m.NextActAt) - battle.Time));
        var horizon = Math.Max(120f, (float)maxRemaining);

        var sorted = alive
            .Select(m => new
            {
                Unit = m,
                Remaining = Math.Max(0L, (m.Chanting != null ? m.ChantFireAt : m.NextActAt) - battle.Time)
            })
            .OrderBy(x => x.Remaining)
            .Take(8)
            .ToList();

        var lastPos = new Dictionary<CombatSide, float> { [CombatSide.Attacker] = -1000f, [CombatSide.Defender] = -1000f };
        var laneStep = TimelineAvatar + 8f;

        foreach (var item in sorted)
        {
            var m = item.Unit;
            var frac = Math.Clamp((float)item.Remaining / horizon, 0f, 1f);
            var targetY = topY + frac * (bottomY - topY - laneStep);

            var side = m.Side;
            if (targetY < lastPos[side] + laneStep)
                targetY = lastPos[side] + laneStep;
            lastPos[side] = targetY;

            // 竖向平滑位移插值，严禁瞬移
            var curY = TimelineMarkerY.TryGetValue(m.Id, out var cy) ? cy : targetY;
            var newY = Mathf.Lerp(curY, targetY, 0.22f);
            if (Math.Abs(newY - targetY) > 0.4f)
                HasActiveAnimations = true;
            else
                newY = targetY;
            TimelineMarkerY[m.Id] = newY;

            // 我方在导轨左车道，敌方在右车道
            var isPlayerSide = side == battle.ControlledSide;
            var iconX = isPlayerSide ? trackX - TimelineAvatar - 6f : trackX + 6f;
            var iconBox = new Rect2(iconX, newY, TimelineAvatar, TimelineAvatar);

            // 卡片深底 + 细双线框（与全局头像同一套画法）
            ci.DrawRect(iconBox, new Color(InkStyle.Bg, 0.92f));

            var portrait = PortraitFor(m.Name);
            if (portrait != null)
            {
                var psize = portrait.GetSize();
                var scale = Mathf.Min(iconBox.Size.X / psize.X, iconBox.Size.Y / psize.Y);
                var drawSize = (psize * scale).Round();
                ci.DrawTextureRect(portrait, new Rect2(iconBox.GetCenter() - drawSize / 2f, drawSize), false);
            }
            else
            {
                // 没立绘：用名字小牌兜底（首字），文字绝对居中。
                var tag = m.Name.Length > 0 ? m.Name[..1] : "?";
                InkDraw.Text(ci, iconBox.GetCenter(), tag, 20, InkStyle.Line, "cm");
            }

            InkFrame.CardOutline(ci, iconBox);

            // 咏唱中的单位在框外挂一个「咏」字。
            if (m.Chanting != null)
            {
                var chantX = isPlayerSide ? iconBox.Position.X - 14f : iconBox.End.X + 14f;
                InkDraw.Text(ci, new Vector2(chantX, newY + TimelineAvatar / 2f), "咏", 12, InkStyle.Line, "cm");
            }
        }
    }

    /// <summary>按角色名解析立绘纹理；没有配置或文件缺失返回 null，由调用方退回名字牌。</summary>
    private static Texture2D? PortraitFor(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return InkIllustration.LoadTexture($"res://content/portraits/{name}.png");
    }

    /// <summary>
    /// 绘制中上方 Boss/精英大血条与行动点条（类似碧蓝幻想风格）。
    /// 主人定：这一条是**专属于 Boss/精英（多格单位）**的——小怪不在这里出，
    /// 小怪的血量只看脚下那条自己的血条。
    /// </summary>
    public static void DrawBossHeader(CanvasItem ci, Rect2 rect, Battle battle)
    {
        var foes = battle.Members.FindAll(m => m.Alive && m.Side != battle.ControlledSide);
        if (foes.Count == 0)
            return;

        // 只有多格单位（2x2 精英 / 4x4 首领）才算 Boss，才配上这条大血条。
        var boss = foes.Find(f => f.Size > 1);
        if (boss == null)
            return;

        // 严格以 1920 画布中轴 960f 为中心对齐，绝不偏轴
        const float cx = 960f;

        // 1. Boss 名称（居中大字，加粗加亮）
        var titleY = rect.Position.Y + 6f;
        InkDraw.Text(ci, new Vector2(cx, titleY), boss.Name, 34, InkStyle.Line, "cm");

        // 2. 居中高亮 HP 数值（比名称更大更亮，永不被血条遮盖或反白消失）
        var hpText = $"HP {boss.Hp} / {boss.MaxHp}";
        InkDraw.Text(ci, new Vector2(cx, titleY + 34f), hpText, 22, InkStyle.Line, "cm");

        // 3. GBF 风格巨型大血条（血量增长右延绿条白条跟进，扣减暗红左延随后消除）
        var barW = Math.Min(740f, rect.Size.X - 40f);
        var barH = 18f;
        var barRect = new Rect2(cx - barW / 2f, rect.Position.Y + 56f, barW, barH);
        var targetRatio = boss.MaxHp <= 0 ? 0f : Mathf.Clamp((float)boss.Hp / boss.MaxHp, 0f, 1f);
        InkDynamicMeter.Draw(ci, $"combat_boss_{boss.Id}", barRect, targetRatio, isHp: true);

        // 4. GBF 风格行动点条（Charge Diamonds，大号纯实心充能菱珠，严禁空心！）
        const int maxDiamonds = 4;
        var charged = battle.Time > 0
            ? Math.Clamp((int)((battle.Time / 100) % (maxDiamonds + 1)), 0, maxDiamonds)
            : 2; // 开局就绪态展示 2 颗高亮纯白充满状态
        var diamondY = barRect.End.Y + 20f;
        const float gap = 48f;
        var startX = cx - (maxDiamonds - 1) * gap / 2f;

        for (var i = 0; i < maxDiamonds; i++)
        {
            var pt = new Vector2(startX + i * gap, diamondY);
            var isCharged = i < charged;
            var diamondColor = isCharged ? InkStyle.Line : new Color(InkStyle.Dim, 0.45f);
            // 大号纯实心菱珠（半径 13px，跨度 26px），100% 实心色块填充
            InkDraw.Jewel(ci, pt, 13f, diamondColor, filled: true);
        }

        // 5. Boss 行动点下方的当前行动名称容器（攻击、防御、技能名、道具名等，居中 960 中轴）
        var actionName = battle.CurrentActionName;
        if (string.IsNullOrEmpty(actionName))
            actionName = BattleSkills.Attack.Name;

        var actionBox = InkLayout.CombatActionNameBox;
        ci.DrawRect(actionBox, InkStyle.Bg);
        InkFrame.CardOutline(ci, actionBox);

        InkDraw.Text(ci, actionBox.GetCenter(), actionName, 20, InkStyle.Line, "cm");
    }

    /// <summary>绘制右侧战况日志窗口（无标题，紧凑客观战报流水）。</summary>
    public static void DrawCombatLog(CanvasItem ci, Rect2 rect, Battle battle)
    {
        // 1. 窗口底与精致外框
        ci.DrawRect(rect, InkStyle.Bg);
        InkFrame.CardOutline(ci, rect);

        var left = rect.Position.X + 16f;
        var width = rect.Size.X - 32f;

        // 2. 提取最近战况日志流水（紧凑战报排版：行距 24px，字号 17/13，容量增至 15 行）
        var lines = FormatBattleEvents(battle);
        const float step = 24f;
        var capacity = Mathf.Max(1, (int)((rect.Size.Y - 24f) / step));
        var start = Math.Max(0, lines.Count - capacity);
        var y = rect.Position.Y + 14f;

        for (var i = start; i < lines.Count; i++)
        {
            var isLatest = i == lines.Count - 1;
            var isRound = lines[i].StartsWith("— 第 ") && lines[i].EndsWith(" 回合 —");

            Color col;
            if (isRound)
            {
                col = new Color(InkStyle.Dim, 0.55f);
                InkDraw.Text(ci, new Vector2(rect.GetCenter().X, y + 10f), lines[i], 16, col, "cm");
            }
            else
            {
                if (isLatest)
                    col = InkStyle.Line;
                else
                {
                    var alpha = Math.Clamp(0.38f + (i - start) * 0.04f, 0.38f, 0.85f);
                    col = new Color(InkStyle.Dim, alpha);
                }
                InkDraw.TextBounded(ci, new Rect2(left, y, width, 22f), lines[i], 17, 13, col, "lm");
            }
            y += step;
        }
    }

    public static List<string> FormatBattleEvents(Battle battle)
    {
        var result = new List<string>(battle.Events.Count);
        foreach (var ev in battle.Events)
        {
            var actor = battle.Find(ev.ActorId)?.Name ?? (ev.ActorId == 0 ? "系统" : $"#{ev.ActorId}");
            var target = battle.Find(ev.TargetId)?.Name ?? (ev.TargetId == 0 ? "目标" : $"#{ev.TargetId}");

            switch (ev.Kind)
            {
                case CombatEventKind.Round:
                    result.Add($"— 第 {ev.Round} 回合 —");
                    break;
                case CombatEventKind.Hit:
                {
                    var isCrit = ev.Amount > 20;
                    var killSuffix = ev.HpAfter <= 0 ? " 击坠" : "";
                    var actName = !string.IsNullOrEmpty(ev.SkillId) && ev.SkillId != BattleSkills.AttackId
                        ? battle.Lookup(ev.SkillId)?.Name ?? ev.SkillId
                        : (isCrit ? "暴击" : "击中");
                    var hitText = $"{actor} {actName} {target} -{ev.Amount}{killSuffix}";
                    result.Add(hitText);
                    break;
                }
                case CombatEventKind.Miss:
                    result.Add($"{target} 闪避");
                    break;
                case CombatEventKind.Heal:
                    result.Add($"{actor} 治疗 {target} +{ev.Amount}");
                    break;
                case CombatEventKind.Status:
                    if (ev.SkillId == BattleSkills.GuardId)
                        result.Add($"{actor} 防御");
                    else if (ev.SkillId == "awakening")
                        result.Add($"{actor} 觉醒");
                    break;
                case CombatEventKind.Chant:
                    result.Add($"{actor} 咏唱");
                    break;
                case CombatEventKind.SpellFire:
                {
                    var spellName = !string.IsNullOrEmpty(ev.SkillId)
                        ? battle.Lookup(ev.SkillId)?.Name ?? ev.SkillId
                        : "法术";
                    result.Add($"{actor} 施放 {spellName}");
                    break;
                }
                case CombatEventKind.Interrupt:
                    result.Add($"{target} 咏唱打断");
                    break;
                case CombatEventKind.Flee:
                    result.Add(ev.Amount == 1 ? $"{actor} 撤退成功" : $"{actor} 撤退失败");
                    break;
                case CombatEventKind.Dot:
                {
                    var killSuffix = ev.HpAfter <= 0 ? " 击坠" : "";
                    result.Add($"{target} 持续伤害 -{ev.Amount}{killSuffix}");
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>3D DRPG 纵深投影深度分档（从近到远）。</summary>
    private static readonly float[] ZDepths = { 1.0f, 1.42f, 2.05f, 2.95f, 4.1f };
    public static float PerspectiveDepth(int depth) => ZDepths[depth];
    private const float VpX = 960f;
    private const float VpY = 180f;
    private const float YBottom = 660f;
    private const float WCol = 390f;

    private static float GetX(float c, int r) => VpX + (c - 2.0f) * WCol / ZDepths[r];
    private static float GetY(int r) => VpY + (YBottom - VpY) / ZDepths[r];

    /// <summary>
    /// 绘制战斗场景背景插画（纯无外框，等比居中裁切铺满背景区域）。
    /// 插画路径完全由 content/illustrations.json 配置驱动，禁止代码硬编码。
    /// dimFactor 为插画亮度系数（0=全黑，1=原图亮度），直接作用于插画本身而非 UI 元素。
    /// </summary>
    public static void DrawCombatBackground(CanvasItem ci, Rect2 rect, string key = "default", float dimFactor = 1.0f)
    {
        var texture = InkIllustration.GetCombatBackground(key);
        if (texture == null)
            return;

        var size = texture.GetSize();
        if (size.X <= 0 || size.Y <= 0)
            return;

        var scale = Mathf.Max(rect.Size.X / size.X, rect.Size.Y / size.Y);
        var srcW = rect.Size.X / scale;
        var srcH = rect.Size.Y / scale;
        var src = new Rect2((size.X - srcW) / 2f, (size.Y - srcH) / 2f, srcW, srcH);

        ci.DrawTextureRectRegion(texture, rect, src, new Color(dimFactor, dimFactor, dimFactor, 1f));
    }

    /// <summary>绘制 4x4 向中心汇聚的 3D DRPG 透视网格敌阵（地面网格线 + 立体站位）。</summary>
    public static void DrawEnemyField(CanvasItem ci, Rect2 field, Battle battle, int selectedTargetId, int selectedTargetColumn, SkillDef? armed)
    {
        var pickColumn = armed != null && (armed.Target == SkillTarget.FoesColumn || (armed.Target == SkillTarget.Enemy && armed.Range == SkillRange.Ranged));

        // 1. 绘制地面 3D 透视网格线：5 根向中心灭点汇聚的光线 + 5 根横向网格线
        var gridColor = new Color(InkStyle.Line, 0.28f);
        for (var c = 0; c <= 4; c++)
        {
            ci.DrawLine(new Vector2(GetX(c, 0), GetY(0)), new Vector2(GetX(c, 4), GetY(4)), gridColor, 1.2f, antialiased: true);
        }
        for (var r = 0; r <= 4; r++)
        {
            ci.DrawLine(new Vector2(GetX(0, r), GetY(r)), new Vector2(GetX(4, r), GetY(r)), gridColor, 1.2f, antialiased: true);
        }

        // 选列模式：高亮选中的透视列梯形
        if (pickColumn && selectedTargetColumn is >= 1 and <= 4)
        {
            var colIdx = selectedTargetColumn;
            var poly = new Vector2[]
            {
                new(GetX(colIdx - 1, 0), GetY(0)),
                new(GetX(colIdx, 0), GetY(0)),
                new(GetX(colIdx, 4), GetY(4)),
                new(GetX(colIdx - 1, 4), GetY(4)),
            };
            ci.DrawColoredPolygon(poly, new Color(InkStyle.Line, 0.08f));
        }

        var foes = battle.Members.FindAll(m => m.Alive && m.Side != battle.ControlledSide);

        var grid = new Combatant?[4, 4];
        foreach (var f in foes)
        {
            var depth = 4 - Math.Clamp(f.ThreatTier, 1, 4);
            var left = Math.Clamp(f.Column, 1, 4) - 1;
            for (var dr = 0; dr < f.Size && depth + dr < 4; dr++)
                for (var dc = 0; dc < f.Size && left + dc < 4; dc++)
                    grid[left + dc, depth + dr] = f;
        }

        var clickable = new HashSet<int>();
        for (var c = 0; c < 4; c++)
        for (var r = 0; r < 4; r++)
            if (grid[c, r] is { } front)
            {
                clickable.Add(front.Id);
                break;
            }

        // 由深至浅（远至近）绘制，前排自然遮挡后排
        for (var r = 3; r >= 0; r--)
        for (var c = 0; c < 4; c++)
        {
            var f = grid[c, r];
            if (f == null)
                continue;
            var depth = 4 - Math.Clamp(f.ThreatTier, 1, 4);
            var left = Math.Clamp(f.Column, 1, 4) - 1;
            if (c != left || r != depth)
                continue;

            var targetScale = 1.0f / ZDepths[depth];
            var curScale = EnemyScales.TryGetValue(f.Id, out var cs) ? cs : targetScale;
            var newScale = Mathf.Lerp(curScale, targetScale, 0.20f);
            if (Mathf.Abs(newScale - targetScale) > 0.003f)
                HasActiveAnimations = true;
            else
                newScale = targetScale;
            EnemyScales[f.Id] = newScale;

            var xLeft = GetX(left, depth);
            var xRight = GetX(left + f.Size, depth);
            var colWidth = xRight - xLeft;
            var w = Math.Max(f.Size > 1 ? 260f : 100f, colWidth - (f.Size > 1 ? 54f : 16f) * newScale);
            var h = f.Size > 1 ? Math.Max(120f, 190f * newScale) : Math.Max(64f, 130f * newScale);
            var targetXc = (xLeft + xRight) / 2f;
            var targetYb = GetY(depth);
            var targetPos = new Vector2(targetXc - w / 2f, targetYb - h - 4f);

            var curPos = EnemyPositions.TryGetValue(f.Id, out var cp) ? cp : targetPos;
            Vector2 newPos;
            if (InkCombatFx.HasActiveDeathAnimations)
            {
                // 主人定：后续动作需要等动画播放完。死亡切开与消散播放中，后排存活单位绝对保持原本站位，绝不提前向前推！
                newPos = curPos;
            }
            else
            {
                newPos = curPos.Lerp(targetPos, 0.20f);
            }
            if ((newPos - targetPos).LengthSquared() > 0.4f)
                HasActiveAnimations = true;
            else
                newPos = targetPos;
            EnemyPositions[f.Id] = newPos;

            var fxOffset = InkCombatFx.GetOffset(f.Id);
            var card = new Rect2(newPos.X + fxOffset.X, newPos.Y + fxOffset.Y, w, h);

            var isFront = clickable.Contains(f.Id);
            // 只有当玩家已点选动作/技能且该怪为合法目标时，才高亮为可点目标态
            var isTargetable = (armed == null || (armed.Target == SkillTarget.Enemy && !pickColumn)) && isFront;
            // 主人定：名称与 HP 数值统一调亮（骨白），不再随前后排压暗；字号同步放大。
            var ink = InkStyle.Line;
            var nameSize = f.Size > 1
                ? Math.Clamp((int)(24 * newScale + 8), 22, 30)
                : Math.Clamp((int)(16 * newScale + 6), 13, 20);

            // 1. 卡片深底：遮挡背后穿过的地面透视线条，层级清晰
            ci.DrawRect(card, new Color(InkStyle.Bg, 0.90f));

            // 2. 卡片框：前排可攻击者高亮亮框，后排敌人绘制常规银灰框，全阵型表现统一
            InkFrame.CardOutline(ci, card, highlighted: isTargetable);

            // 行动敌人头顶显示缓慢呼吸变暗变亮的大钝角三角形指向敌人
            var isCurrent = (battle.CurrentActor?.Id == f.Id) || (battle.PendingActor?.Id == f.Id);
            if (isCurrent)
            {
                var foeTopCenter = new Vector2(card.GetCenter().X, card.Position.Y);
                InkCharRenderer.DrawTurnIndicator(ci, foeTopCenter, newScale);
            }

            // 主人定：删除位x说明文字，敌人名称文字完全居中（绝对同轴）
            // 优化排版：名称与顶部框线、HP文本与底部血条保持充足呼吸净空，严禁文字与框线重合！
            var isBoss = f.Size > 1;
            float nameY;
            float meterH;
            float meterY;
            float hpTextY;
            int hpSize;

            if (isBoss)
            {
                nameY = card.Position.Y + card.Size.Y * 0.38f;
                meterH = Math.Clamp(8f * newScale, 6f, 10f);
                meterY = card.End.Y - 14f - meterH;
                hpSize = Math.Clamp((int)(16 * newScale + 8), 16, 22);
                hpTextY = meterY - 8f - hpSize / 2f;
            }
            else
            {
                var topPad = Math.Max(8f, 12f * newScale);
                nameY = card.Position.Y + topPad + nameSize / 2f;
                meterH = Math.Clamp(6f * newScale, 5f, 8f);
                meterY = card.End.Y - 8f - meterH;
                hpSize = Math.Clamp((int)(12 * newScale + 4), 11, 15);
                hpTextY = meterY - 4f - hpSize / 2f;
            }

            InkDraw.Text(ci, new Vector2(card.GetCenter().X, nameY), f.Name, nameSize, ink, "cm");

            if (f.MaxHp > 0)
            {
                var meterRect = new Rect2(card.Position.X + 8f, meterY, card.Size.X - 16f, meterH);

                // 主人定：敌人 HP 文字完全居中对齐，调亮调大
                var hpText = string.Concat("HP ", f.Hp.ToString(), "/", f.MaxHp.ToString());
                InkDraw.Text(ci, new Vector2(card.GetCenter().X, hpTextY), hpText, hpSize, InkStyle.Line, "cm");
                var targetRatio = (float)f.Hp / f.MaxHp;
                InkDynamicMeter.Draw(ci, $"combat_enemy_{f.Id}", meterRect, targetRatio, isHp: true);
            }
        }
    }

    /// <summary>计算任意参战单位在屏幕上的中心像素坐标，供攻击光效与伤害飘字定位。</summary>
    public static Vector2 GetUnitCenter(Battle battle, int unitId)
    {
        var unit = battle.Members.Find(m => m.Id == unitId);
        if (unit == null)
            return new Vector2(960f, 540f);

        if (unit.Side == battle.ControlledSide)
        {
            // 我方角色：基于左下角色栏头像位置（威胁等级 3 为原位，与角色栏同一套位移公式）
            var party = battle.Members.FindAll(m => m.Side == battle.ControlledSide);
            var idx = party.IndexOf(unit);
            if (idx < 0) idx = 0;
            var rect = InkLayout.Card(idx);
            var lift = (unit.ThreatTier - 3) * 14f;
            return rect.GetCenter() + InkLayout.CombatPanelSink - new Vector2(0f, lift);
        }

        // 敌方单位：严格对齐卡片真实几何中心，确保剑痕核心、切缝与伤害飘字同轴重合！
        return GetEnemyCardRect(battle, unitId).GetCenter();
    }

    /// <summary>获取任意敌方单位在屏幕上的卡片完整矩形 Rect2，供死亡切片动画定位。</summary>
    public static Rect2 GetEnemyCardRect(Battle battle, int unitId)
    {
        var unit = battle.Members.Find(m => m.Id == unitId);
        if (unit == null || unit.Side == battle.ControlledSide)
            return new Rect2(960f - 80f, 540f - 50f, 160f, 100f);

        var depth = 4 - Math.Clamp(unit.ThreatTier, 1, 4);
        var left = Math.Clamp(unit.Column, 1, 4) - 1;
        var scale = EnemyScales.TryGetValue(unit.Id, out var s) ? s : 1.0f / ZDepths[depth];
        var xLeft = GetX(left, depth);
        var xRight = GetX(left + unit.Size, depth);
        var colWidth = xRight - xLeft;
        var w = colWidth - (unit.Size > 1 ? 54f : 16f) * scale;
        var h = (unit.Size > 1 ? 180f : 120f) * scale;
        var targetXc = (xLeft + xRight) / 2f;
        var targetYb = GetY(depth);
        var targetPos = new Vector2(targetXc - w / 2f, targetYb - h - 4f);

        if (EnemyPositions.TryGetValue(unit.Id, out var animatedPos))
            return new Rect2(animatedPos.X, animatedPos.Y, w, h);
        return new Rect2(targetPos.X, targetPos.Y, w, h);
    }

    /// <summary>为敌方单位和列带生成用于点击命中的 Widgets 表。</summary>
    public static void BuildCombatWidgets(Battle battle, Rect2 field, SkillDef? armed, List<InkWidget> widgets)
    {
        var pickColumn = armed != null && (armed.Target == SkillTarget.FoesColumn || (armed.Target == SkillTarget.Enemy && armed.Range == SkillRange.Ranged));

        if (pickColumn)
        {
            for (var c = 1; c <= 4; c++)
            {
                var xLeft = GetX(c - 1, 0);
                var xRight = GetX(c, 0);
                var strip = new Rect2(xLeft, GetY(4), xRight - xLeft, GetY(0) - GetY(4));
                widgets.Add(new InkWidget(strip, InkAction.CombatColumn, c, true, $"第{c}列"));
            }
            return;
        }

        var foes = battle.Members.FindAll(m => m.Alive && m.Side != battle.ControlledSide);
        var grid = new Combatant?[4, 4];
        foreach (var f in foes)
        {
            var depth = 4 - Math.Clamp(f.ThreatTier, 1, 4);
            var left = Math.Clamp(f.Column, 1, 4) - 1;
            for (var dr = 0; dr < f.Size && depth + dr < 4; dr++)
                for (var dc = 0; dc < f.Size && left + dc < 4; dc++)
                    grid[left + dc, depth + dr] = f;
        }

        var clickable = new HashSet<int>();
        for (var c = 0; c < 4; c++)
        for (var r = 0; r < 4; r++)
            if (grid[c, r] is { } front)
            {
                clickable.Add(front.Id);
                break;
            }

        for (var r = 0; r < 4; r++)
        for (var c = 0; c < 4; c++)
        {
            var f = grid[c, r];
            if (f == null || !clickable.Contains(f.Id))
                continue;
            var depth = 4 - Math.Clamp(f.ThreatTier, 1, 4);
            var left = Math.Clamp(f.Column, 1, 4) - 1;
            if (c != left || r != depth)
                continue;

            var scale = 1.0f / ZDepths[depth];
            var xLeft = GetX(left, depth);
            var xRight = GetX(left + f.Size, depth);
            var w = (xRight - xLeft) - (f.Size > 1 ? 54f : 16f) * scale;
            var h = (f.Size > 1 ? 180f : 120f) * scale;
            var xc = (xLeft + xRight) / 2f;
            var yb = GetY(depth);
            var card = EnemyPositions.TryGetValue(f.Id, out var p)
                ? new Rect2(p.X, p.Y, w, h)
                : new Rect2(xc - w / 2f, yb - h - 4f, w, h);
            widgets.Add(new InkWidget(card, InkAction.CombatTarget, f.Id, true, f.Name));
        }
    }
}
