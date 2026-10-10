using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 角色页技能段里的技能网（2026-10-10 重做）：一式一格，自上而下由浅入深，五列铺开；
/// 格与格按来源技能连线成网（可跨流派）——用上方的技能，就有机会学会下方与它相连的技能。
/// 已学习＝实心四芒星；此刻有学习率＝呼吸的空心星；尚远＝暗星。选中一式时，与它相连的线全部提亮。
/// </summary>
public partial class PortraitHubScreen
{
    private string _skillSelectedId = BattleSkills.AttackId;

    public string DebugSelectedSkill => _skillSelectedId;

    /// <summary>呼吸：1.8 秒一拍（与全项目的呼吸箭头同拍），核对模式定格最亮。</summary>
    private static float SkillPulse() => PortraitMotion.Instant
        ? 1f : 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 1000f * Mathf.Tau / 1.8f));

    /// <summary>画技能网与所选技能的详情，返回内容下沿。</summary>
    private float DrawSkillChart(CharacterState who, float y, Rect2 view)
    {
        var all = PortraitSkillChart.Build(who);
        if (all.All(s => s.Def.Id != _skillSelectedId))
            _skillSelectedId = BattleSkills.AttackId;
        var web = PortraitLayout.SkillWebRect(y, PortraitSkillChart.Rows);
        PortraitFrame.Panel(this, web);
        var byId = all.ToDictionary(s => s.Def.Id);
        Vector2 At(ChartSkill s) => PortraitLayout.SkillNodeCenter(web, s.Row, s.Column);
        var pulse = SkillPulse();
        var picked = byId[_skillSelectedId];

        // 底纹：每行一道极淡的横线，像星图的纬线。
        for (var row = 0; row < PortraitSkillChart.Rows; row++)
        {
            var ly = PortraitLayout.SkillNodeCenter(web, row, 0).Y;
            DrawLine(new Vector2(web.Position.X + 30f, ly), new Vector2(web.End.X - 30f, ly), new Color(InkStyle.Dim, 0.12f), 1f, true);
        }

        // 连线：先画暗线，再画亮线（两端已学习 / 正可学习的呼吸线 / 与所选相连的），亮的压在上面。
        var edges = all.SelectMany(t => t.Def.DeriveFrom.Select(id => (Source: byId[id], Target: t))).ToList();
        foreach (var pass in new[] { false, true })
            foreach (var (source, target) in edges)
            {
                var linked = source.Def.Id == _skillSelectedId || target.Def.Id == _skillSelectedId;
                var lit = linked || source.Learned && (target.Learned || target.Chance > 0);
                if (lit != pass)
                    continue;
                var color = linked ? InkStyle.Line
                    : source.Learned && target.Learned ? new Color(InkStyle.Line, 0.75f)
                    : lit ? new Color(InkStyle.Line, 0.2f + 0.5f * pulse)
                    : new Color(InkStyle.Dim, 0.35f);
                DrawEdge(At(source), At(target), source.Row, target.Row, source.Column == target.Column,
                    color, linked ? 3f : lit ? 2f : 1.4f);
            }

        for (var i = 0; i < all.Count; i++)
        {
            var s = all[i];
            var at = At(s);
            DrawSkillNode(at, s, pulse, s.Def.Id == _skillSelectedId);
            var hit = PortraitLayout.SkillNodeHit(at);
            if (view.Encloses(hit))
                _widgets.Add(new PortraitWidget(hit, PortraitAction.SkillNode, i, true, s.Def.Id));
        }

        return DrawSkillPool(who, DrawSkillDetail(who, picked, web.End.Y + 30f) + 40f, view);
    }

    /// <summary>
    /// 身份技能池（拟案，待主人核定）：抽到的几式，一式一行（名字、基础 / 核心种类、机制说明或数值）。
    /// 玩家角色在领地里多两枚钮：换身份（弹身份清单，选了即按该身份重抽）、重抽（按当前身份再抽一池）。
    /// </summary>
    private float DrawSkillPool(CharacterState who, float y, Rect2 view)
    {
        var left = PortraitLayout.Pad;
        var right = PortraitLayout.CanvasWidth - PortraitLayout.Pad;
        PortraitFrame.SectionRule(this, left, right, y, who.PoolIdentity.Length > 0 ? $"身份技能池 · {who.PoolIdentity}" : "身份技能池");
        y += 56f;
        foreach (var def in SkillPool.Skills(who))
        {
            var row = new Rect2(left, y, right - left, 150f);
            PortraitFrame.Panel(this, row);
            var kind = InkText.CoreKind(def.Core);
            var chipWidth = InkDraw.Measure(kind, PortraitLayout.FontMeta).X + 56f;
            var chip = new Rect2(row.End.X - 30f - chipWidth, row.Position.Y + 14f, chipWidth, 60f);
            var core = def.Core != CoreKind.None;
            PortraitFrame.Brackets(this, chip, core ? InkStyle.Line : InkStyle.Dim);
            InkDraw.Text(this, chip.GetCenter(), kind, PortraitLayout.FontMeta, core ? InkStyle.Line : InkStyle.Dim, "cm");
            InkDraw.TextBounded(this, new Rect2(row.Position.X + 30f, row.Position.Y + 10f, chip.Position.X - row.Position.X - 50f, 68f),
                def.Name, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            var line = def.Description.Length > 0 ? def.Description : PoolSkillLine(def);
            InkDraw.TextBounded(this, new Rect2(row.Position.X + 30f, row.Position.Y + 80f, row.Size.X - 60f, 60f),
                line, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            y += 166f;
        }

        if (!who.IsMaster)
            return y;
        var hub = _vm.Hub;
        var half = (right - left - 24f) / 2f;
        var pick = new Rect2(left, y + 10f, half, PortraitLayout.TouchMin);
        var again = new Rect2(left + half + 24f, y + 10f, half, PortraitLayout.TouchMin);
        var enabled = hub.CanRerollSkillPool;
        PortraitFrame.Plaque(this, pick, "换身份", enabled: enabled);
        PortraitFrame.Plaque(this, again, "重抽", primary: true, enabled: enabled && SkillPool.PoolOf(who.PoolIdentity) != null);
        if (view.Encloses(pick))
            _widgets.Add(new PortraitWidget(pick, PortraitAction.PoolIdentity, 0, enabled, "换身份"));
        if (view.Encloses(again))
            _widgets.Add(new PortraitWidget(again, PortraitAction.PoolReroll, 0, enabled && SkillPool.PoolOf(who.PoolIdentity) != null, "重抽"));
        return y + 10f + PortraitLayout.TouchMin;
    }

    /// <summary>基础技能的一行数值：种类、威力、咏唱、附加。</summary>
    private static string PoolSkillLine(SkillDef def)
    {
        var parts = new List<string> { InkText.SkillKind(def.Kind), InkText.SkillTarget(def.Target) };
        if (def.Kind != SkillKind.Buff)
            parts.Add($"威力 {def.Power}%");
        if (def.ChantRounds > 0)
            parts.Add($"咏唱 {def.ChantRounds} 回合");
        if (def.Control)
            parts.Add("打断咏唱");
        if (def.Status is StatusKind.StatMod)
            parts.Add($"{InkText.StatusStat(def.StatusStat)} {def.StatusPercent:+0;-0}% {def.StatusRounds} 回合");
        else if (def.Status is StatusKind.Dot)
            parts.Add($"每回合 {def.StatusPower} 伤害 {def.StatusRounds} 回合");
        else if (def.Status is StatusKind.Points)
            parts.Add($"护盾 {def.StatusPower} 点 {def.StatusRounds} 回合");
        return string.Join(" · ", parts);
    }

    /// <summary>换身份：弹出有技能池的身份清单，选哪个就按哪个重抽。</summary>
    private void OpenPoolIdentities()
    {
        var hub = _vm.Hub;
        var page = new InkModalPage { Title = "换身份", Body = "选一个身份，按它的技能池重新抽取。" };
        foreach (var identity in hub.PoolIdentities)
        {
            var chosen = identity;
            page.Choices.Add(new InkModalChoice { Id = chosen, Label = chosen, OnSelected = () => { hub.RerollSkillPool(chosen); QueueRedraw(); } });
        }
        ModalWanted!(page);
    }

    /// <summary>
    /// 一条来源线：从来源章底到目标章顶的三次曲线，上下竖直出入；同列跨行的线向右鼓出，免得穿过中间那一格。
    /// </summary>
    private void DrawEdge(Vector2 from, Vector2 to, int fromRow, int toRow, bool sameColumn, Color color, float width)
    {
        var r = PortraitLayout.SkillNodeRadius;
        var a = from + new Vector2(0f, r);
        var b = to - new Vector2(0f, r);
        var bend = sameColumn && toRow - fromRow > 1 ? new Vector2(96f, 0f) : Vector2.Zero;
        var c1 = a + new Vector2(0f, (b.Y - a.Y) * 0.5f) + bend;
        var c2 = b - new Vector2(0f, (b.Y - a.Y) * 0.5f) + bend;
        var pts = new Vector2[25];
        for (var k = 0; k < pts.Length; k++)
        {
            var t = k / (float)(pts.Length - 1);
            var u = 1f - t;
            pts[k] = u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
        }
        DrawPolyline(pts, color, width, true);
        // 目标端一枚小菱，标出方向（来源 → 学到）。
        InkDraw.Jewel(this, b, 5f, color);
    }

    /// <summary>一格：圆章里一枚四芒星，名字写在章下。</summary>
    private void DrawSkillNode(Vector2 at, ChartSkill s, float pulse, bool selected)
    {
        var r = PortraitLayout.SkillNodeRadius;
        if (selected)
        {
            var hit = PortraitLayout.SkillNodeHit(at);
            PortraitFrame.Brackets(this, hit.Grow(-6f), InkStyle.Line);
        }
        DrawCircle(at, r, InkStyle.Panel);
        var rim = s.Learned ? InkStyle.Line : s.Chance > 0 ? new Color(InkStyle.Line, pulse) : new Color(InkStyle.Dim, 0.7f);
        DrawArc(at, r, 0f, Mathf.Tau, 40, rim, selected ? 4f : 2f, true);
        var star = StarPoints(at, r * 0.62f);
        if (s.Learned)
            DrawColoredPolygon(star, s.Usable ? InkStyle.Line : new Color(InkStyle.Line, 0.55f));
        else
            InkDraw.Ink(this, star.Append(star[0]).ToArray(), rim, 1.5f);
        // 名字衬一块底色，压住从后面穿过的连线。
        var nameWidth = Mathf.Min(InkDraw.Measure(s.Def.Name, PortraitLayout.FontMeta).X, 184f) + 12f;
        DrawRect(new Rect2(at.X - nameWidth / 2f, at.Y + r + 6f, nameWidth, 48f), InkStyle.Panel);
        InkDraw.TextBounded(this, new Rect2(at.X - 92f, at.Y + r + 4f, 184f, 52f), s.Def.Name,
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, s.Learned || selected ? InkStyle.Line : InkStyle.Dim, "cm");
    }

    private static Vector2[] StarPoints(Vector2 c, float r)
    {
        var pts = new Vector2[8];
        for (var i = 0; i < 8; i++)
            pts[i] = c + Vector2.FromAngle(-Mathf.Pi / 2f + i * Mathf.Pi / 4f) * (i % 2 == 0 ? r : r * 0.32f);
        return pts;
    }

    /// <summary>详情框：名字＋状态签、题签、数值、附加状态、学习条件逐条。返回下沿。</summary>
    private float DrawSkillDetail(CharacterState who, ChartSkill s, float y)
    {
        var def = s.Def;
        var reqs = PortraitSkillChart.Requirements(who, def);
        var height = 70f + 72f + 64f + 64f + (def.Status.HasValue ? 64f : 0f) + 20f + 56f
            + System.Math.Max(1, reqs.Count) * 64f + (s.Learned ? 30f : 96f);
        var frame = new Rect2(PortraitLayout.Pad, y, PortraitLayout.FullWidth, height);
        PortraitFrame.GothicFrame(this, frame);
        var left = frame.Position.X + 56f;
        var right = frame.End.X - 56f;
        y = frame.Position.Y + 70f;

        var status = s.Learned ? s.Usable ? "已学习" : "已学习 · 未持流派" : s.Chance > 0 ? $"学习率 {s.Chance}%" : "未学习";
        var statusWidth = InkDraw.Measure(status, PortraitLayout.FontMeta).X + 56f;
        var chip = new Rect2(right - statusWidth, y - 32f, statusWidth, 64f);
        var lit = s.Learned || s.Chance > 0;
        PortraitFrame.Brackets(this, chip, lit ? InkStyle.Line : InkStyle.Dim);
        InkDraw.Text(this, chip.GetCenter(), status, PortraitLayout.FontMeta, lit ? InkStyle.Line : InkStyle.Dim, "cm");
        InkDraw.TextBounded(this, new Rect2(left, y - 40f, chip.Position.X - left - 20f, 80f), def.Name,
            PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        y += 72f;

        var tags = new List<string> { InkText.SkillKind(def.Kind), InkText.SkillTarget(def.Target), InkText.SkillRange(def.Range) };
        if (def.Gate.Style.HasValue)
            tags.Insert(0, InkText.Style(def.Gate.Style.Value));
        if (def.Gate.Attribute.HasValue)
            tags.Insert(def.Gate.Style.HasValue ? 1 : 0, InkText.CoreStat(def.Gate.Attribute.Value));
        if (def.ChantRounds > 0)
            tags.Add($"咏唱{def.ChantRounds}回合");
        if (def.Control)
            tags.Add("打断咏唱");
        PortraitFrame.TagLine(this, left, y, tags, right, InkStyle.Dim);
        y += 64f;

        var numbers = new List<(string, string)> { ("威力", $"{def.Power}%") };
        if (def.HitMod != 0)
            numbers.Add(("命中", def.HitMod.ToString("+0;-0")));
        PortraitFrame.CountTags(this, left, y, numbers, right);
        y += 64f;
        if (def.Status.HasValue)
        {
            var effect = def.Status.Value switch
            {
                StatusKind.StatMod => $"{InkText.StatusStat(def.StatusStat)} {def.StatusPercent:+0;-0}%，持续 {def.StatusRounds} 回合",
                StatusKind.Dot => $"每回合 {def.StatusPower} 点伤害，持续 {def.StatusRounds} 回合",
                StatusKind.Points => $"点数护盾 {def.StatusPower} 点，持续 {def.StatusRounds} 回合",
            };
            PortraitFrame.CountTag(this, left, y, "附加", effect, true);
            y += 64f;
        }

        y += 20f;
        PortraitFrame.SectionRule(this, left - 20f, right + 20f, y, "学习条件");
        y += 56f;
        if (reqs.Count == 0)
        {
            InkDraw.Text(this, new Vector2(left, y + 28f), "通用技能，人人都会", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            y += 64f;
        }
        foreach (var (label, need, have, met) in reqs)
        {
            var row = new Rect2(left - 16f, y, right - left + 32f, 58f);
            InkDraw.Jewel(this, new Vector2(row.Position.X + 22f, row.GetCenter().Y), 7f, met ? InkStyle.Line : InkStyle.WoodDark);
            InkDraw.Text(this, new Vector2(row.Position.X + 46f, row.GetCenter().Y), label, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            // 右侧：要求（来源技能的名字 / 门槛数值）＋现值，达成亮字。
            var haveX = row.End.X - 10f;
            InkDraw.Text(this, new Vector2(haveX, row.GetCenter().Y), have, PortraitLayout.FontMeta,
                met ? InkStyle.Line : InkStyle.Dim, "rm");
            var needX = haveX - InkDraw.Measure(have, PortraitLayout.FontMeta).X - 36f;
            InkDraw.Jewel(this, new Vector2(needX + 18f, row.GetCenter().Y), 4f, InkStyle.Dim);
            InkDraw.Text(this, new Vector2(needX, row.GetCenter().Y), need, PortraitLayout.FontMeta, InkStyle.Line, "rm");
            InkDraw.InkLine(this, new Vector2(row.Position.X + 40f, row.End.Y + 2f), new Vector2(row.End.X, row.End.Y + 2f), InkStyle.Hover, 1.5f);
            y += 64f;
        }
        if (!s.Learned)
            InkDraw.TextBounded(this, new Rect2(left, y + 10f, right - left, 56f),
                "战斗中使用来源技能时有机会学会",
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        return frame.End.Y + 30f;
    }

    /// <summary>换人或离开角色页时选中项回到盘心（普通攻击）。</summary>
    private void ResetSkillView() => _skillSelectedId = BattleSkills.AttackId;
}
