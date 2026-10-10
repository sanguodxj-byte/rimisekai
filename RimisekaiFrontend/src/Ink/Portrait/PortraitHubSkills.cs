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
        var rows = all.Max(s => s.Row) + 1;
        var web = PortraitLayout.SkillWebRect(y, rows);
        PortraitFrame.Panel(this, web);
        var byId = all.ToDictionary(s => s.Def.Id);
        Vector2 At(ChartSkill s) => PortraitLayout.SkillNodeCenter(web, s.Row, s.Column);
        var pulse = SkillPulse();
        var picked = byId[_skillSelectedId];

        // 底纹：每行一道极淡的横线，像星图的纬线。
        for (var row = 0; row < rows; row++)
        {
            var ly = PortraitLayout.SkillNodeCenter(web, row, 0).Y;
            DrawLine(new Vector2(web.Position.X + 30f, ly), new Vector2(web.End.X - 30f, ly), new Color(InkStyle.Dim, 0.12f), 1f, true);
        }

        // 连线：先画暗线，再画亮线（两端已激活 / 来源已激活、下一式作数而未激活的呼吸线 / 与所选相连的），亮的压在上面。
        var edges = SkillTree.Links(all.Select(s => new TreeSlot(s.Def, s.Row, s.Column)).ToList()).Select(l => (Source: byId[l.Source.Id], Target: byId[l.Target.Id])).ToList();
        foreach (var pass in new[] { false, true })
            foreach (var (source, target) in edges)
            {
                var linked = source.Def.Id == _skillSelectedId || target.Def.Id == _skillSelectedId;
                var lit = linked || source.Active && target.Eligible;
                if (lit != pass)
                    continue;
                var color = linked ? InkStyle.Line
                    : source.Active && target.Active ? new Color(InkStyle.Line, 0.75f)
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

        return DrawPoolButtons(who, DrawSkillDetail(who, picked, web.End.Y + 30f) + 10f, view);
    }

    /// <summary>玩家角色在领地里多两枚钮：换身份（弹身份清单，选了即按该身份重抽）、重抽（按当前身份再抽一池）。</summary>
    private float DrawPoolButtons(CharacterState who, float y, Rect2 view)
    {
        var left = PortraitLayout.Pad;
        var right = PortraitLayout.CanvasWidth - PortraitLayout.Pad;
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

    /// <summary>换身份：弹出有技能池的身份清单，选哪个就按哪个重抽。</summary>
    private void OpenPoolIdentities()
    {
        var hub = _vm.Hub;
        var page = new InkModalPage { Title = "换身份", Body = "选一个身份，重新抽取技能。" };
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
        // 自上而下出章底、入章顶；来源在下方（门槛低而估值高）就反过来；同一行从章侧横着连。
        Vector2 a, b, c1, c2;
        if (fromRow == toRow)
        {
            var side = new Vector2(Mathf.Sign(to.X - from.X) * r, 0f);
            a = from + side;
            b = to - side;
            var lift = new Vector2(0f, -48f);
            c1 = a + new Vector2((b.X - a.X) * 0.4f, 0f) + lift;
            c2 = b - new Vector2((b.X - a.X) * 0.4f, 0f) + lift;
        }
        else
        {
            var dir = toRow > fromRow ? 1f : -1f;
            a = from + new Vector2(0f, r * dir);
            b = to - new Vector2(0f, r * dir);
            var bend = sameColumn && System.Math.Abs(toRow - fromRow) > 1 ? new Vector2(96f, 0f) : Vector2.Zero;
            c1 = a + new Vector2(0f, (b.Y - a.Y) * 0.5f) + bend;
            c2 = b - new Vector2(0f, (b.Y - a.Y) * 0.5f) + bend;
        }
        var pts = new Vector2[25];
        for (var k = 0; k < pts.Length; k++)
        {
            var t = k / (float)(pts.Length - 1);
            var u = 1f - t;
            pts[k] = u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
        }
        DrawPolyline(pts, color, width, true);
        // 目标端一枚小菱，标出方向（门槛低 → 门槛高）。
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
        // 核心技能多一道外环。
        if (s.Def.Core != CoreKind.None)
            DrawArc(at, r + 9f, 0f, Mathf.Tau, 48, s.Active ? InkStyle.Line : new Color(InkStyle.Dim, 0.7f), 2f, true);
        DrawCircle(at, r, InkStyle.Panel);
        // 已激活实心；抽到（作数）而未达条件描边呼吸；没抽到的暗。
        var rim = s.Active ? InkStyle.Line : s.Eligible ? new Color(InkStyle.Line, pulse) : new Color(InkStyle.Dim, 0.45f);
        DrawArc(at, r, 0f, Mathf.Tau, 40, rim, selected ? 4f : 2f, true);
        var star = StarPoints(at, r * 0.62f);
        if (s.Active)
            DrawColoredPolygon(star, InkStyle.Line);
        else
            InkDraw.Ink(this, star.Append(star[0]).ToArray(), rim, 1.5f);
        // 名字衬一块底色，压住从后面穿过的连线。
        var nameWidth = Mathf.Min(InkDraw.Measure(s.Def.Name, PortraitLayout.FontMeta).X, 184f) + 12f;
        DrawRect(new Rect2(at.X - nameWidth / 2f, at.Y + r + 6f, nameWidth, 48f), InkStyle.Panel);
        InkDraw.TextBounded(this, new Rect2(at.X - 92f, at.Y + r + 4f, 184f, 52f), s.Def.Name,
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, s.Active || selected ? InkStyle.Line : InkStyle.Dim, "cm");
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
        var lineHeight = PortraitLayout.FontMeta * 1.4f;
        var descLines = def.Description.Length > 0
            ? InkDraw.WrapLines(def.Description, PortraitLayout.FullWidth - 112f, PortraitLayout.FontMeta) : new List<string>();
        var footer = !s.Active;
        var height = 70f + 72f + 64f + 64f + (def.Status.HasValue ? 64f : 0f) + descLines.Count * lineHeight + 20f + 56f
            + System.Math.Max(1, reqs.Count) * 64f + (footer ? 96f : 30f);
        var frame = new Rect2(PortraitLayout.Pad, y, PortraitLayout.FullWidth, height);
        PortraitFrame.GothicFrame(this, frame);
        var left = frame.Position.X + 56f;
        var right = frame.End.X - 56f;
        y = frame.Position.Y + 70f;

        var status = s.Active ? "已激活" : s.Eligible ? "未达条件" : "未抽到";
        var statusWidth = InkDraw.Measure(status, PortraitLayout.FontMeta).X + 56f;
        var chip = new Rect2(right - statusWidth, y - 32f, statusWidth, 64f);
        var lit = s.Eligible;
        PortraitFrame.Brackets(this, chip, lit ? InkStyle.Line : InkStyle.Dim);
        InkDraw.Text(this, chip.GetCenter(), status, PortraitLayout.FontMeta, lit ? InkStyle.Line : InkStyle.Dim, "cm");
        InkDraw.TextBounded(this, new Rect2(left, y - 40f, chip.Position.X - left - 20f, 80f), def.Name,
            PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        y += 72f;

        var tags = new List<string>
        {
            def.Core != CoreKind.None ? InkText.CoreKind(def.Core) : InkText.SkillTier(SkillTier.Of(def)),
            InkText.SkillKind(def.Kind), InkText.SkillTarget(def.Target), InkText.SkillRange(def.Range),
        };
        if (def.Gate.Style.HasValue)
            tags.Insert(1, InkText.Style(def.Gate.Style.Value));
        if (def.Gate.Attribute.HasValue)
            tags.Insert(def.Gate.Style.HasValue ? 2 : 1, InkText.CoreStat(def.Gate.Attribute.Value));
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
        // 说明（核心技能的机制、部分基础技能的附注）：整句折行，不截断。
        foreach (var line in descLines)
        {
            InkDraw.Text(this, new Vector2(left, y + lineHeight / 2f), line, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            y += lineHeight;
        }

        y += 20f;
        PortraitFrame.SectionRule(this, left - 20f, right + 20f, y, "激活条件");
        y += 56f;
        if (reqs.Count == 0)
        {
            InkDraw.Text(this, new Vector2(left, y + 28f), "通用技能，人人都会",
                PortraitLayout.FontMeta, InkStyle.Dim, "lm");
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
        if (footer)
            InkDraw.TextBounded(this, new Rect2(left, y + 10f, right - left, 56f),
                s.Eligible ? "条件达成即自动激活" : "没抽到，换身份或重抽才可能有",
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        return frame.End.Y + 30f;
    }

    /// <summary>换人或离开角色页时选中项回到盘心（普通攻击）。</summary>
    private void ResetSkillView() => _skillSelectedId = BattleSkills.AttackId;
}
