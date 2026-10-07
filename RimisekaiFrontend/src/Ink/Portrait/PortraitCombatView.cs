using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>竖屏战斗：透视敌阵、Boss 血条与底部错层头像。绘制只读取战斗快照。</summary>
public partial class PortraitCombatView : Control
{
    private readonly List<PortraitWidget> _hits = new();
    private readonly List<SkillDef> _menu = new();
    private readonly List<(Combatant Unit, Rect2 Card, bool Front)> _enemies = new();
    private readonly InkCombatFxLayer _combatFx = new();
    private int _lastEventCount;
    private InkViewModel _vm = null!;
    private PortraitModalLayer _modal = null!;
    private Combatant? _actor;
    private string _armed = BattleSkills.AttackId;
    private float _tick;
    private bool _settled;
    private int _skillPage;
    private int _itemPage;
    private int _selectedTarget = -1;
    private int _selectedColumn;
    private int _loadPage;
    private Vector2 _press;
    private bool _pressed;
    private Rect2? _pressRect;

    /// <summary>未绑定时保留空白图像区；以后按参战者身份提供纹理，无须改动卡片与命中。</summary>
    public Func<Combatant, Texture2D?>? UnitImageProvider { get; set; }

    /// <summary>跑条等小图位的图像源（头像裁切）；空则回退 UnitImageProvider。</summary>
    public Func<Combatant, Texture2D?>? TrackImageProvider { get; set; }
    public event Action? Finished;
    public event Action<string>? LoadRequested;
    public event Action? TitleRequested;
    public IReadOnlyList<PortraitWidget> DebugWidgets => _hits;
    public IReadOnlyList<PortraitRegion> DebugRegions => PortraitLayout.CombatRegions;
    public int DebugActorId => _actor?.Id ?? -1;
    public int DebugSelectedColumn => _selectedColumn;
    private Battle? B => _vm.Combat?.Battle;
    private SkillDef? Armed => B?.Lookup(_armed);
    private bool PickColumn => Armed is { Target: SkillTarget.FoesColumn }
        or { Target: SkillTarget.Enemy, Range: SkillRange.Ranged };

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_combatFx);
        SetProcess(false);
    }

    public void Bind(InkViewModel vm, PortraitModalLayer modal)
    {
        _vm = vm;
        _modal = modal;
        _settled = false;
        _tick = 0f;
        _actor = null;
        _armed = BattleSkills.AttackId;
        _selectedTarget = -1;
        _selectedColumn = 0;
        _skillPage = 0;
        _loadPage = 0;
        _pressed = false;
        _lastEventCount = 0;
        InkCombatFx.Clear();
        Size = new Vector2(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        CustomMinimumSize = Size;
        RefreshMenu();
        SetProcess(true);
        QueueRedraw();
    }

    private void RefreshMenu()
    {
        _menu.Clear();
        var source = _actor ?? B?.Members.Find(m => m.Alive && m.Side == B.ControlledSide);
        if (source != null)
            foreach (var id in source.Skills.Distinct())
            {
                // 普通攻击走攻击钮，不在技能菜单里重复列出。
                if (id == BattleSkills.AttackId)
                    continue;
                var skill = B!.Lookup(id);
                if (skill != null)
                    _menu.Add(skill);
            }
    }

    public override void _Process(double delta)
    {
        var battle = B;
        if (battle == null)
            return;
        InkCombatRenderer.IndicatorTime += (float)delta;
        // 攻击光效、受击震颤、卡片斩裂与伤害飘字（横版迁入）。
        InkCombatFx.Update((float)delta);
        ConsumeBattleEvents();
        if (InkCombatFx.HasActiveDeathAnimations)
        {
            // 死亡斩裂动画播放期间锁住时间轴，播完再结算。
            QueueRedraw();
            return;
        }
        InkDynamicMeter.Update((float)delta);
        if (battle.Outcome != CombatOutcome.Ongoing)
        {
            if (_settled || _modal.IsActive)
                return;
            _settled = true;
            var outcome = CombatSettlement.Settle(_vm.Hub.State, battle, _vm.Combat!.QuestRun);
            if (outcome != null)
                _modal.Show(InkModalFactory.CreateCombatSettlement(outcome.Result, outcome.Loot,
                    () => Finished?.Invoke()));
            else
                Finished?.Invoke();
            return;
        }
        // PendingActor 会自动执行 AI，禁止在绘制/命中构建中调用。
        _tick += (float)delta;
        if (_tick >= 0.35f)
        {
            _tick = 0f;
            var advanced = battle.StepTurn(out var actor);
            _actor = !advanced && actor?.Side == battle.ControlledSide ? actor : null;
            RefreshMenu();
        }
        QueueRedraw();
    }

    /// <summary>捕获战斗新产生的战况事件，激活斩击光弧、受击震颤、卡片斩裂与伤害飘字。</summary>
    private void ConsumeBattleEvents()
    {
        var battle = B!;
        while (_lastEventCount < battle.Events.Count)
        {
            var ev = battle.Events[_lastEventCount++];
            InkCombatFx.SpawnFromEvent(ev, battle, UnitCenter);
        }
    }

    /// <summary>战斗单位在竖屏画布上的视觉中心：敌方取占格方区、我方取底部头像卡。</summary>
    private Vector2 UnitCenter(int unitId)
    {
        foreach (var (unit, rect, _) in _enemies)
            if (unit.Id == unitId)
                return rect.GetCenter();
        if (B != null)
        {
            var allies = B.Members.Where(m => m.Side == B.ControlledSide).Take(4).ToArray();
            for (var i = 0; i < allies.Length; i++)
                if (allies[i].Id == unitId)
                    return PortraitLayout.AllyCard(i).GetCenter();
        }
        return PortraitLayout.CombatField.GetCenter();
    }

    public override void _Draw()
    {
        _hits.Clear();
        PortraitFrame.SetPress(_pressed ? _pressRect : null);
        PortraitFrame.Backdrop(this);
        var battle = B;
        if (battle == null)
            return;
        InkCombatRenderer.DrawCombatBackground(this, PortraitLayout.CombatBackground, dimFactor: 0.32f);
        DrawTopBar(battle);
        DrawBoss(battle);
        BuildEnemyGeometry(battle);
        DrawEnemyField(battle);
        DrawTurnOrder(battle);
        DrawAllies(battle);
        DrawOpEntry(battle);
        DrawSettingsGear();
    }

    /// <summary>顶栏：左战场名、中回合数（右侧是设置齿轮）。</summary>
    private void DrawTopBar(Battle battle)
    {
        var top = PortraitLayout.CombatTop;
        InkDraw.TextBounded(this, new Rect2(PortraitLayout.Pad + 20f, top.Position.Y, 420f, top.Size.Y), battle.PlaceName,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        InkDraw.Text(this, new Vector2(top.GetCenter().X + 110f, top.GetCenter().Y), $"第 {battle.Round} 回合",
            PortraitLayout.FontMeta, InkStyle.Dim, "cm");
    }

    /// <summary>
    /// 行动顺序条：存活者按下次出手先后自左而右；我方圆形头像、敌方菱形，正在等指令的我方行动者实心骨白托底。
    /// </summary>
    private void DrawTurnOrder(Battle battle)
    {
        var order = PortraitLayout.CombatOrder;
        InkDraw.Text(this, new Vector2(PortraitLayout.Pad + 20f, order.GetCenter().Y), "行动顺序", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        var alive = battle.Members.Where(m => m.Alive).OrderBy(m => m.NextActAt).ThenBy(m => m.Id).Take(6).ToArray();
        for (var i = 0; i < alive.Length; i++)
        {
            var m = alive[i];
            var c = PortraitLayout.CombatOrderToken(i);
            var ally = m.Side == battle.ControlledSide;
            var tex = TrackImageProvider?.Invoke(m) ?? UnitImageProvider?.Invoke(m);
            if (ally)
            {
                if (_actor?.Id == m.Id)
                    DrawRect(new Rect2(c - new Vector2(54f, 54f), new Vector2(108f, 108f)), InkStyle.Line);
                PortraitFrame.Avatar(this, c, 44f, tex, m.Name, ring: _actor?.Id != m.Id);
            }
            else
            {
                InkDraw.Jewel(this, c, 48f, InkStyle.Dim);
                InkDraw.Jewel(this, c, 44f, InkStyle.Bg);
                InkDraw.Text(this, c, m.Name[..1], PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            }
        }
    }

    private void DrawBoss(Battle battle)
    {
        var boss = battle.Members.Find(m => m.Alive && m.Side != battle.ControlledSide && m.Size > 1);
        if (boss == null)
            return;
        InkDraw.TextBounded(this, PortraitLayout.BossName, boss.Name, PortraitLayout.FontBody,
            PortraitLayout.FontMeta, InkStyle.Line, "lm");
        InkDraw.TextBounded(this, PortraitLayout.BossHp, $"HP {boss.Hp} / {boss.MaxHp}",
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "rm");
        PortraitFrame.Bar(this, PortraitLayout.BossMeter, (float)boss.Hp / boss.MaxHp);
        // 血条右侧按内容声明的行动点数画实心菱（默认 1 枚）。
        var pips = Math.Max(1, boss.ActionPoints);
        for (var i = 0; i < pips; i++)
            InkDraw.Jewel(this, PortraitLayout.BossPipCenter(i, pips), 9f, InkStyle.Line);
    }

    private void BuildEnemyGeometry(Battle battle)
    {
        _enemies.Clear();
        var foes = battle.Members.Where(m => m.Alive && m.Side != battle.ControlledSide).ToArray();
        var front = new HashSet<int>();
        for (var column = 1; column <= 4; column++)
        {
            var unit = foes.Where(m => m.Column <= column && column < m.Column + m.Size)
                .OrderByDescending(m => m.ThreatTier).ThenBy(m => m.Id).FirstOrDefault();
            if (unit != null)
                front.Add(unit.Id);
        }
        // 遮挡只按空间前后：靠后（威胁档低）先画，靠前（威胁档高）后画、盖住身后的一切。
        foreach (var unit in foes.OrderBy(m => m.ThreatTier).ThenBy(m => m.Column))
            _enemies.Add((unit, PortraitLayout.EnemyCard(unit), front.Contains(unit.Id)));
    }

    private void DrawEnemyField(Battle battle)
    {
        for (var column = 0; column <= 4; column++)
            InkDraw.InkLine(this, PortraitLayout.CombatGridPoint(column, 0), PortraitLayout.CombatGridPoint(column, 4),
                new Color(InkStyle.Line, 0.42f), PortraitLayout.LineHair);
        for (var depth = 0; depth <= 4; depth++)
            InkDraw.InkLine(this, PortraitLayout.CombatGridPoint(0, depth), PortraitLayout.CombatGridPoint(4, depth),
                new Color(InkStyle.Line, 0.42f), PortraitLayout.LineHair);

        if (PickColumn)
            for (var column = 1; column <= 4; column++)
            {
                var polygon = PortraitLayout.CombatColumnPolygon(column);
                if (_selectedColumn == column)
                    DrawColoredPolygon(polygon, InkStyle.Hover);
                var occupied = _enemies.Any(e => e.Unit.Column <= column && column < e.Unit.Column + e.Unit.Size);
                var columnBounds = PortraitLayout.CombatTouchRect(InkLayout.PolygonBounds(polygon).Grow(-2));
                _hits.Add(new PortraitWidget(columnBounds, PortraitAction.CombatColumn,
                    column, occupied, "", polygon));
            }

        foreach (var (unit, rect, front) in _enemies)
        {
            var targetable = _actor != null && Armed?.Target == SkillTarget.Enemy && front && !PickColumn;
            // 受击横向震颤（横版迁入）：命中/扑击时单位整体偏移。
            var shake = InkCombatFx.GetOffset(unit.Id);
            var shaken = new Rect2(rect.Position + shake, rect.Size);
            // 遮挡剔除：方区被更靠前的敌方方区完全包含时，立绘/名字/血条都不画（命中块照常注册）。
            var covered = _enemies.Any(e => e.Unit.Id != unit.Id && e.Unit.ThreatTier > unit.ThreatTier
                && e.Card.Encloses(rect));
            if (!covered)
            {
                var tex = UnitImageProvider?.Invoke(unit);
                if (tex != null)
                {
                    // 有立绘：取消卡片框，仅显示去背立绘，血量条压在立绘底缘，名字在立绘正下方。
                    DrawUnitImage(unit, new Rect2(shaken.Position + new Vector2(4, 4), shaken.Size - new Vector2(8, 8)));
                    PortraitFrame.Bar(this, new Rect2(shaken.Position.X + 6, shaken.End.Y - 14, shaken.Size.X - 12, 10),
                        (float)unit.Hp / unit.MaxHp);
                    InkDraw.TextBounded(this, new Rect2(shaken.Position.X, shaken.End.Y + 6f, shaken.Size.X, 34),
                        unit.Name, PortraitLayout.FontBody, 26, InkStyle.Line, "cm");
                }
                else
                {
                    DrawRect(shaken, InkStyle.Bg);
                    PortraitFrame.CardOutline(this, shaken, highlighted: targetable || unit.Id == _selectedTarget);
                    DrawUnitImage(unit, PortraitLayout.EnemyImage(shaken));
                    // 血量走血量条（满宽槽线），名字在顶；远排卡缩小后也不出现文字截断。
                    InkDraw.TextBounded(this, PortraitLayout.EnemyName(shaken), unit.Name,
                        PortraitLayout.FontBody, 26, InkStyle.Line, "cm");
                    PortraitFrame.Bar(this, PortraitLayout.EnemyMeter(shaken), (float)unit.Hp / unit.MaxHp);
                }
            }
            if (PickColumn)
            {
                for (var offset = 0; offset < unit.Size; offset++)
                    _hits.Add(new PortraitWidget(PortraitLayout.CombatTouchRect(
                        PortraitLayout.EnemyColumnCard(rect, offset, unit.Size)),
                        PortraitAction.CombatColumn, unit.Column + offset, _actor != null, unit.Name));
            }
            else
                // 画面照真透视缩，触控热区居中放大到 118px 保底（不可见，仅保隔离门禁与手指命中）。
                _hits.Add(new PortraitWidget(PortraitLayout.CombatTouchRect(rect),
                    PortraitAction.CombatTarget, unit.Id, targetable, unit.Name));
        }
    }

    /// <summary>我方卡一排：圆形头像、名字、血条、状态图标；正在等指令的行动者卡片浅填骨白描边。</summary>
    private void DrawAllies(Battle battle)
    {
        var allies = battle.Members.Where(m => m.Side == battle.ControlledSide).Take(4).ToArray();
        for (var i = 0; i < allies.Length; i++)
        {
            var unit = allies[i];
            var shake = InkCombatFx.GetOffset(unit.Id);
            var card = PortraitLayout.AllyCard(i);
            var rect = new Rect2(card.Position + shake, card.Size);
            PortraitFrame.Card(this, rect, _actor?.Id == unit.Id, 22f);
            PortraitFrame.Avatar(this, PortraitLayout.AllyAvatar(rect), 52f, UnitImageProvider?.Invoke(unit), unit.Name,
                dim: !unit.Alive);
            InkDraw.TextBounded(this, PortraitLayout.AllyName(rect), unit.Name,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, unit.Alive ? InkStyle.Line : InkStyle.Dim, "cm");
            PortraitFrame.Bar(this, PortraitLayout.AllyMeter(rect), (float)unit.Hp / unit.MaxHp);
            for (var s = 0; s < unit.Statuses.Count && s < 4; s++)
            {
                var status = unit.Statuses[s];
                var iconRect = PortraitLayout.StatusIcon(rect, s);
                InkIcon.Draw(this, status.Name, iconRect);
                // 右下数字角标显强度：StatMod 取幅度百分点，DoT 取每轮伤害，点数类取余量。
                var strength = Mathf.Abs(status.Kind switch
                {
                    StatusKind.StatMod => status.Percent,
                    StatusKind.Dot => status.Power,
                    _ => status.Points,
                });
                InkDraw.Text(this, new Vector2(iconRect.End.X - 1f, iconRect.End.Y + 3f), strength.ToString(), 26, InkStyle.Line, "rb");
            }
            _hits.Add(new PortraitWidget(card, PortraitAction.CombatAct, unit.Id,
                _actor != null && unit.Alive && Armed?.Target == SkillTarget.Ally, unit.Name));
        }
    }

    private void DrawUnitImage(Combatant unit, Rect2 rect)
    {
        var texture = UnitImageProvider?.Invoke(unit);
        if (texture == null || rect.Size.Y <= 0)
            return;
        var size = texture.GetSize();
        var scale = Mathf.Min(rect.Size.X / size.X, rect.Size.Y / size.Y);
        DrawTextureRect(texture, new Rect2(rect.GetCenter() - size * scale / 2, size * scale), false);
    }

    /// <summary>
    /// 行动面板：圆顶面板＋「某某 的行动」＋当前招式名；2×2 大钮（攻击 / 技能 / 道具 / 逃跑），
    /// 我方行动者就绪时点亮，跑条流动期全暗不可点。技能 / 道具弹分页弹窗，其余直接生效。
    /// </summary>
    private void DrawOpEntry(Battle battle)
    {
        var panel = PortraitLayout.CombatActions;
        PortraitFrame.Dock(this, new Rect2(panel.Position, panel.Size + new Vector2(0, 80f)));
        var enabled = _actor != null;
        if (_actor != null)
        {
            InkDraw.Text(this, new Vector2(PortraitLayout.Pad + 20f, panel.Position.Y + 70f), $"{_actor.Name} 的行动",
                PortraitLayout.FontBody, InkStyle.Line, "lm");
            InkDraw.TextBounded(this, new Rect2(PortraitLayout.CanvasWidth / 2f, panel.Position.Y + 40f,
                    PortraitLayout.CanvasWidth / 2f - PortraitLayout.Pad - 20f, 60f), battle.CurrentActionName,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "rm");
        }
        for (var slot = 0; slot < OpLabels.Length; slot++)
        {
            var rect = PortraitLayout.CombatButton(slot);
            var pressed = PortraitFrame.IsPressed(rect);
            var armed = enabled && slot == 0 && _armed == BattleSkills.AttackId;
            PortraitFrame.RoundRect(this, rect, 28f, pressed ? PortraitFrame.PressFill : new Color(InkStyle.Bg, 0.9f),
                enabled ? InkStyle.Line : InkStyle.WoodDark, 4f);
            if (enabled)
                PortraitFrame.RoundRect(this, rect.Grow(-9f), 19f, null, new Color(InkStyle.Dim, 0.6f), 2f);
            var ink = enabled ? InkStyle.Line : InkStyle.Dim;
            OpGlyphs[slot](this, rect.Position.X + 80f, rect.GetCenter().Y, 34f, ink);
            InkDraw.Text(this, new Vector2(rect.Position.X + 150f, rect.GetCenter().Y), OpLabels[slot], PortraitLayout.FontTitle, ink, "lm");
            if (armed)
                InkDraw.Jewel(this, new Vector2(rect.End.X - 50f, rect.GetCenter().Y), 10f, InkStyle.Line);
            _hits.Add(new PortraitWidget(rect, PortraitAction.CombatMenu, slot, enabled, OpLabels[slot]));
        }
    }

    private static readonly string[] OpLabels = { "攻击", "技能", "道具", "逃跑" };

    private static readonly Action<CanvasItem, float, float, float, Color>[] OpGlyphs =
    {
        PortraitGlyph.Swords, PortraitGlyph.Scroll, PortraitGlyph.Bag, PortraitGlyph.Flee,
    };

    /// <summary>
    /// 设置齿轮：右上角实心齿轮（战斗白名单外的浮件，无外框、无标题牌）。
    /// 点按弹出 设置 弹窗：保存进度 / 读取进度 / 回到主界面 / 返回。
    /// </summary>
    private void DrawSettingsGear()
    {
        var hit = PortraitLayout.CombatGearHit;
        _hits.Add(new PortraitWidget(hit, PortraitAction.CombatSettings, 0, true, "设置"));
        if (PortraitFrame.IsPressed(hit))
            PortraitFrame.RoundRect(this, hit.Grow(-10f), 49f, PortraitFrame.PressFill);
        PortraitGlyph.Gear(this, hit.GetCenter().X, hit.GetCenter().Y, 28f, InkStyle.Line);
    }

    /// <summary>设置弹窗：保存进度 / 读取进度 / 回到主界面 / 返回（战斗中随时可开）。</summary>
    private void OpenSettingsPopup()
    {
        _loadPage = 0;
        // 只入队不 Dismiss：Choose 回调尾部的 Advance 会顶掉当前页，链式换页靠队列完成。
        _modal.Show(new InkModalPage
        {
            Title = "设置",
            Choices = new List<InkModalChoice>
            {
                new() { Id = "save", Label = "保存进度", OnSelected = SaveProgress },
                new() { Id = "load", Label = "读取进度", OnSelected = ShowLoadPopup },
                new() { Id = "title", Label = "回到主界面", OnSelected = () => TitleRequested?.Invoke() },
                new() { Id = "back", Label = "返回", OnSelected = () => { } },
            },
        });
    }

    private void SaveProgress()
    {
        var hub = _vm.Hub;
        var saved = InkSaveStore.SaveNew(hub.State, hub, out _) != null;
        _modal.Show(new InkModalPage { Title = "设置", Body = saved ? "已保存。" : "保存失败。" });
    }

    /// <summary>读取进度弹窗：存档槽按页列出，每页五条，上一页 / 下一页 翻页。</summary>
    private void ShowLoadPopup()
    {
        const int perPage = 5;
        var slots = InkSaveStore.ListSaves();
        var pageSlots = slots.Skip(_loadPage * perPage).Take(perPage)
            .Select(s => new InkModalChoice
            {
                Id = s.FileName, Label = s.TerritoryName,
                OnSelected = () => LoadRequested?.Invoke(s.FilePath),
            })
            .ToList();
        if ((_loadPage + 1) * perPage < slots.Count)
            pageSlots.Add(new InkModalChoice { Id = "next", Label = "下一页", OnSelected = () => { _loadPage++; ShowLoadPopup(); } });
        if (_loadPage > 0)
            pageSlots.Insert(0, new InkModalChoice { Id = "prev", Label = "上一页", OnSelected = () => { _loadPage--; ShowLoadPopup(); } });
        pageSlots.Add(new InkModalChoice { Id = "back", Label = "返回", OnSelected = OpenSettingsPopup });
        _modal.Show(new InkModalPage { Title = "读取进度", Choices = pageSlots });
    }

    /// <summary>技能分页弹窗：每页五式，下一页 / 上一页 翻页，点选即装备该式。</summary>
    private void ShowSkillPopup()
    {
        const int perPage = 5;
        var pageSkills = _menu.Skip(_skillPage * perPage).Take(perPage)
            .Select(s => new InkModalChoice { Id = s.Id, Label = s.Name, OnSelected = () => ArmAndClose(s.Id) })
            .ToList();
        if ((_skillPage + 1) * perPage < _menu.Count)
            pageSkills.Add(new InkModalChoice { Id = "next", Label = "下一页", OnSelected = () => { _skillPage++; ShowSkillPopup(); } });
        if (_skillPage > 0)
            pageSkills.Insert(0, new InkModalChoice { Id = "prev", Label = "上一页", OnSelected = () => { _skillPage--; ShowSkillPopup(); } });
        pageSkills.Add(new InkModalChoice { Id = "back", Label = "返回", OnSelected = () => { } });
        _modal.Show(new InkModalPage { Title = "技能", Choices = pageSkills });
    }

    /// <summary>道具弹窗：参战我方背包汇总（名 ×数），每页五条。Core 尚无战斗道具结算，条目暂列不可选。</summary>
    private void ShowItemPopup()
    {
        const int perPage = 5;
        var bag = new Dictionary<string, int>();
        var battle = B;
        if (battle != null)
            foreach (var m in battle.Members.Where(m => m.Side == battle.ControlledSide))
            {
                var member = _vm.Hub.State.Roster.Find(m.Name);
                if (member == null)
                    continue;
                foreach (var pair in member.Bag.Items)
                    bag[pair.Key] = bag.GetValueOrDefault(pair.Key) + pair.Value;
            }
        var items = bag.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var pageItems = items.Skip(_itemPage * perPage).Take(perPage)
            .Select(p => new InkModalChoice { Id = p.Key, Label = $"{ItemLabel(p.Key)} ×{p.Value}", Enabled = false })
            .ToList();
        if ((_itemPage + 1) * perPage < items.Count)
            pageItems.Add(new InkModalChoice { Id = "next", Label = "下一页", OnSelected = () => { _itemPage++; ShowItemPopup(); } });
        if (_itemPage > 0)
            pageItems.Insert(0, new InkModalChoice { Id = "prev", Label = "上一页", OnSelected = () => { _itemPage--; ShowItemPopup(); } });
        pageItems.Add(new InkModalChoice { Id = "back", Label = "返回", OnSelected = () => { } });
        _modal.Show(new InkModalPage { Title = "道具", Choices = pageItems });
    }

    private string ItemLabel(string itemId)
    {
        var info = Items.Info(_vm.Hub.State.Territory.Weapons, itemId);
        return info != null && info.Value.Label.Length > 0 ? info.Value.Label : itemId;
    }

    /// <summary>
    /// 装备一式并收起弹窗；自身 / 全体目标立即结算，其余进入点选目标。
    /// 无就绪行动者时照常装备，结算由指令排队在下一名我方行动者就绪时执行。
    /// </summary>
    private void ArmAndClose(string skillId)
    {
        var skill = B!.Lookup(skillId);
        if (skill == null || _actor == null)
            return;
        _armed = skillId;
        B.CurrentActionName = skill.Name;
        if (skill.Target is SkillTarget.Self or SkillTarget.AllAllies or SkillTarget.AllEnemies)
            Submit(_actor.Id);
        _modal.Dismiss();
        QueueRedraw();
    }

    private void DoFlee()
    {
        _modal.Dismiss();
        if (B!.TryFlee())
            Finished?.Invoke();
        _actor = null;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            return;
        if (mb.Pressed)
        {
            _press = mb.Position;
            _pressed = true;
            _pressRect = HitRect(mb.Position);
            QueueRedraw();
            return;
        }
        if (!_pressed)
            return;
        _pressed = false;
        _pressRect = null;
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Contains(mb.Position))
            {
                if (_hits[i].Enabled && _hits[i].Contains(_press))
                    Act(_hits[i]);
                else
                    QueueRedraw();
                return;
            }
        QueueRedraw();
    }

    /// <summary>按下点到的块矩形（按下反馈用）；没点中任何块返回 null。</summary>
    private Rect2? HitRect(Vector2 at)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Contains(at))
                return _hits[i].Rect;
        return null;
    }

    public void DebugPress(PortraitAction action, int index)
    {
        var hit = _hits.Find(w => w.Action == action && w.Index == index);
        if (hit.Enabled)
            Act(hit);
    }

    private void Act(PortraitWidget widget)
    {
        // 设置随时可开：不依赖行动者就绪。
        if (widget.Action == PortraitAction.CombatSettings)
        {
            OpenSettingsPopup();
            return;
        }
        var battle = B;
        var actor = _actor;
        if (battle == null || actor == null)
            return;
        switch (widget.Action)
        {
            case PortraitAction.CombatMenu:
                switch (widget.Index)
                {
                    case 0: ArmAndClose(BattleSkills.AttackId); break;
                    case 1: _skillPage = 0; ShowSkillPopup(); break;
                    case 2: _itemPage = 0; ShowItemPopup(); break;
                    default: DoFlee(); break;
                }
                break;
            case PortraitAction.CombatTarget:
            case PortraitAction.CombatAct:
                _selectedTarget = widget.Index;
                Submit(widget.Index);
                break;
            case PortraitAction.CombatColumn:
                _selectedColumn = widget.Index;
                Submit(0, widget.Index);
                break;
            case PortraitAction.CombatFlee:
                if (battle.TryFlee())
                    Finished?.Invoke();
                _actor = null;
                break;
        }
        QueueRedraw();
    }

    private void Submit(int targetId, int column = 0)
    {
        var battle = B!;
        var action = new CombatAction { ActorId = _actor.Id, SkillId = _armed, TargetId = targetId, TargetColumn = column };
        if (battle.Act(action))
        {
            _actor = null;
            _armed = BattleSkills.AttackId;
            _skillPage = 0;
            RefreshMenu();
        }
    }
}
