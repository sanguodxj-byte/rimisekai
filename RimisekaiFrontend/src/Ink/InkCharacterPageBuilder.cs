using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 角色资料页：状态 / 技能 / 日程。
/// 状态页上排三块（状态/攻击/关系），下排三列（生活/武器/流派）；
/// 技能页按流派列战斗技能，选中一项在右栏展开完整面板；
/// 日程页上排四段开关、下排全员工作矩阵，选中一段或一格在下方详情里改。
/// </summary>
public static class InkCharacterPageBuilder
{
    /// <summary>按页类型构建；不是这三页时返回 null。</summary>
    public static InkPageModel? Build(InkViewModel vm, InkPage page, CharacterState? who,
        string selectedSkillId = "", int selected = -1, int roomId = -1, int facilityFirst = 0,
        int focusedSector = -1, float viewZoom = 1.0f, Godot.Vector2? viewPivot = null,
        float viewRotation = 0.0f, System.Collections.Generic.IReadOnlyList<bool>? abilityOpen = null,
        int abilityFirst = 0, int memberFirst = 0)
    {
        if (page is not (InkPage.Status or InkPage.Skills or InkPage.Schedule))
            return null;

        abilityOpen ??= new[] { false, false, false };

        if (who == null && page == InkPage.Schedule)
            who = vm.Hub.State.Roster.Master;

        if (who == null)
            return EmptyPage(page);

        return page switch
        {
            InkPage.Status => Status(vm, who, abilityOpen, abilityFirst),
            InkPage.Skills => Skills(vm, who, selectedSkillId, focusedSector, viewZoom, viewPivot ?? InkLayout.SkillDiscCenter, viewRotation),
            InkPage.Schedule => Schedule(vm, who, selected, roomId, facilityFirst, memberFirst),
            _ => null,
        };
    }

    /// <summary>没有对应角色时的空模型：提供标题与空提示，可直接关闭。</summary>
    private static InkPageModel EmptyPage(InkPage page)
    {
        var title = page switch
        {
            InkPage.Status => "状态",
            InkPage.Skills => "技能",
            InkPage.Schedule => "日程",
            _ => "角色",
        };

        return new InkPageModel
        {
            Page = page,
            Title = title,
            CharacterName = "",
            PortraitPath = "",
            Rows = Array.Empty<InkPageRow>(),
            EmptyHint = "没有可安排的人。",
            Filters = Array.Empty<string>(),
            Sorts = Array.Empty<string>(),
        };
    }

    // ---------- 状态 ----------

    /// <summary>
    /// 状态：左列「状态」+「攻击」，右列六个属性方块 + 能力条，中列上下左右居中的立绘。
    /// 能力每条一个独立方框；默认每段只报最高一条，点段头条才摊开本级以下。整页只读。
    /// </summary>
    private static InkPageModel Status(InkViewModel vm, CharacterState who, IReadOnlyList<bool> abilityOpen, int abilityFirst)
    {
        var rows = new List<InkPageRow>();
        var c = who.Condition;

        rows.Add(Bar("体力", c.Stamina, c.MaxStamina));
        rows.Add(Bar("气力", c.Spirit, c.MaxSpirit));
        rows.Add(Value("好感", $"{c.Favor}", InkText.Bond(c.Bond)));
        rows.Add(Bar("心情", who.Affect.Mood, 100));

        var sheet = who.Combat;
        rows.Add(Value("攻击", $"{sheet.Attack}"));
        rows.Add(Value("血量", $"{sheet.MaxHp}"));
        rows.Add(Value("防御", $"{sheet.Defence}"));
        rows.Add(Value("闪避", $"{sheet.Dodge}"));
        rows.Add(Value("法强", $"{sheet.SpellPower}"));
        rows.Add(Value("速度", $"{who[CoreStat.Speed]}"));

        // 六项核心属性：与技能盘的六个扇区同一套（速度是纯战斗项，不设扇区也不在这里列）。
        // 每项带当前经验，方块底部画成经验进度条（CoreExpPerPoint 点经验换 1 点属性）。
        foreach (var stat in StatusAttributes)
        {
            var exp = who.CoreExp[(int)stat];
            rows.Add(new InkPageRow
            {
                Name = InkText.CoreStat(stat),
                Value = $"{who[stat]}",
                Note = exp > 0 ? $"经验 {exp}" : "",
                MeterValue = exp,
                MeterMax = CharacterState.CoreExpPerPoint,
            });
        }

        // 下排：能力三列。等级由经验换算，有效值 = 核心属性 + 经验/100。
        rows.Add(Section("生活"));
        var life = new List<InkPageRow>();
        for (var i = 0; i < AttributeMap.LifeCount; i++)
        {
            var skill = (LifeSkill)i;
            var exp = who.LifeExp[i];
            life.Add(Value(InkText.LifeSkill(skill), $"{who.Life(skill)}",
                exp > 0 ? $"经验 {exp}" : ""));
        }
        MarkTop(life);
        rows.AddRange(life);

        rows.Add(Section("武器"));
        var weapons = new List<InkPageRow>();
        foreach (var type in Enum.GetValues<WeaponType>())
        {
            var p = who.Weapons[(int)type];
            weapons.Add(Value(InkText.Weapon(type), $"Lv{p.Level}",
                p.Exp > 0 ? $"经验 {p.Exp}" : ""));
        }
        MarkTop(weapons);
        rows.AddRange(weapons);

        rows.Add(Section("流派"));
        var styles = new List<InkPageRow>();
        foreach (var style in Enum.GetValues<StyleType>())
        {
            var p = who.Styles[(int)style];
            styles.Add(Value(InkText.Style(style), $"Lv{p.Level}",
                p.Exp > 0 ? $"经验 {p.Exp}" : ""));
        }
        MarkTop(styles);
        rows.AddRange(styles);

        return new InkPageModel
        {
            Page = InkPage.Status,
            Title = $"状态　{who.Name}",
            CharacterName = who.Name,
            PortraitPath = vm.PortraitPath(who.Name),
            Rows = rows,
            EmptyHint = "",
            // 状态页是纯展示，不给搜索/筛选/排序。
            Filters = Array.Empty<string>(),
            Sorts = Array.Empty<string>(),
            AbilityOpen = abilityOpen,
            StatusAbilityFirst = abilityFirst,
        };
    }

    /// <summary>状态页「属性」栏里列的六项核心属性。顺序即显示顺序。</summary>
    private static readonly CoreStat[] StatusAttributes =
    {
        CoreStat.Constitution, CoreStat.Dexterity, CoreStat.Intellect,
        CoreStat.Charm, CoreStat.Perception, CoreStat.Strength,
    };

    /// <summary>
    /// 标出一组里的最高项（按数值大小，等级取等级数）。收敛态下每列只报这一项。
    /// 并列时留第一个，次序不因标注而变。
    /// </summary>
    private static void MarkTop(List<InkPageRow> group)
    {
        var best = -1;
        var bestValue = int.MinValue;
        for (var i = 0; i < group.Count; i++)
        {
            var value = LeadingNumber(group[i].Value);
            if (value > bestValue)
            {
                bestValue = value;
                best = i;
            }
        }
        if (best >= 0)
        {
            var top = group[best];
            group[best] = new InkPageRow
            {
                Name = top.Name,
                Value = top.Value,
                Note = top.Note,
                Action = top.Action,
                TargetId = top.TargetId,
                TargetNumber = top.TargetNumber,
                Enabled = top.Enabled,
                Selected = top.Selected,
                MeterValue = top.MeterValue,
                MeterMax = top.MeterMax,
                IsHeading = top.IsHeading,
                IsTop = true,
            };
        }
    }

    /// <summary>从「12」「Lv3」「经验 200」这类串里取出领头数字；没有则 0。</summary>
    private static int LeadingNumber(string text)
    {
        var n = 0;
        foreach (var ch in text)
        {
            if (ch < '0' || ch > '9')
            {
                if (n > 0)
                    break;
                continue;
            }
            n = n * 10 + (ch - '0');
        }
        return n;
    }

    // ---------- 技能 ----------

    /// <summary>
    /// 技能：战斗技能盘。盘分八个扇区（通用 + 七流派），每扇区由内向外五环，
    /// 每环分列、每列切两格——**一格就是一个技能节点**，盘面铺满三角格。
    /// 节点按门槛由低到高、由内向外、顺时针落在格上（空格子留给后续技能）。
    /// 通用两式人人自带；流派技能按门槛解锁
    /// （流派＋熟练，必要时还有属性／生活技能／素质／前置技能，见 SkillGate）。
    /// 选中一个节点在右栏展开完整面板。
    /// </summary>
    private static InkPageModel Skills(InkViewModel vm, CharacterState who, string selectedSkillId,
        int focusedSector = -1, float viewZoom = 1.0f, Godot.Vector2? viewPivot = null, float viewRotation = 0.0f)
    {
        var unlocked = new HashSet<string>();
        foreach (var known in SkillTable.Known(who))
            unlocked.Add(known.Id);

        var labels = new string[InkLayout.SkillDiscSectors];
        var byCell = new Dictionary<(int, int, int, bool), InkSkillTile>();

        // 每扇区先把格位排成一列（由内向外），技能依次落格。
        var order = new List<(int Ring, int Col, bool Upper)>(InkLayout.SkillDiscCellsInOrder());

        // 扇区 = 六属性。扇区序号即下面这张表的序号（速度是纯战斗项，不设扇区）。
        var attributeOrder = new[]
        {
            CoreStat.Constitution, CoreStat.Dexterity, CoreStat.Intellect,
            CoreStat.Charm, CoreStat.Perception, CoreStat.Strength,
        };
        for (var i = 0; i < attributeOrder.Length && i < labels.Length; i++)
            labels[i] = InkText.CoreStat(attributeOrder[i]);

        // 技能按 Gate.Attribute 归入对应属性扇区；无属性的（通用）归扇区 0。
        var byAttribute = new List<SkillDef>[InkLayout.SkillDiscSectors];
        for (var i = 0; i < byAttribute.Length; i++)
            byAttribute[i] = new List<SkillDef>();

        foreach (var skill in SkillTable.All)
        {
            var attr = skill.Gate.Attribute ?? CoreStat.Constitution;
            var sector = Array.IndexOf(attributeOrder, attr);
            if (sector < 0)
                sector = 0;
            byAttribute[sector].Add(skill);
        }

        for (var sector = 0; sector < InkLayout.SkillDiscSectors; sector++)
        {
            // 门槛低的排内圈、高的排外圈。
            byAttribute[sector].Sort((a, b) =>
            {
                var ra = a.Gate.Core != null && a.Gate.Core.Count > 0
                    ? a.Gate.Core[0].Min : a.Gate.StyleLevel;
                var rb = b.Gate.Core != null && b.Gate.Core.Count > 0
                    ? b.Gate.Core[0].Min : b.Gate.StyleLevel;
                return ra.CompareTo(rb);
            });
            PlaceSector(byCell, order, sector, byAttribute[sector], unlocked, alwaysUnlocked: false);
        }

        // 其余格位一律填成过路节点（无内容，只把技能连成网、铺满盘面）。
        FillPassageNodes(byCell, order);

        var tiles = new List<InkSkillTile>(byCell.Values);

        // 没点名或跨扇区聚焦时，优先选中聚焦扇区内的技能
        var selected = selectedSkillId;
        var picked = FindTile(tiles, selected);
        if (focusedSector >= 0 && (picked == null || picked.Sector != focusedSector))
        {
            selected = "";
            foreach (var tile in tiles)
            {
                if (tile.Sector == focusedSector && tile.Kind == InkSkillNodeKind.Skill && tile.Unlocked)
                {
                    selected = tile.Id;
                    break;
                }
            }
            if (selected.Length == 0)
            {
                foreach (var tile in tiles)
                {
                    if (tile.Sector == focusedSector && tile.Kind == InkSkillNodeKind.Skill)
                    {
                        selected = tile.Id;
                        break;
                    }
                }
            }
        }

        if (selected.Length == 0 || FindTile(tiles, selected) == null)
        {
            foreach (var tile in tiles)
            {
                if (tile.Kind != InkSkillNodeKind.Skill || !tile.Unlocked)
                    continue;
                selected = tile.Id;
                break;
            }
        }

        var detailTitle = "";
        var detailNote = "";
        var detailEffect = "";
        var detailRequirements = new List<InkDetailRequirement>();
        picked = FindTile(tiles, selected);
        if (picked != null)
        {
            var skill = SkillTable.Get(picked.Id);
            if (skill != null)
            {
                detailTitle = skill.Name;
                detailNote = SkillDetailNote(skill);
                detailEffect = SkillEffectText(skill);
                detailRequirements = GateLines(skill, who, unlocked);
            }
        }

        var equippedStyle = who.EquippedStyle;
        var styleLine = equippedStyle.HasValue
            ? $"当前流派　{InkText.Style(equippedStyle.Value)}"
            : "当前流派　无";

        return new InkPageModel
        {
            Page = InkPage.Skills,
            Title = $"技能　{who.Name}",
            CharacterName = who.Name,
            PortraitPath = vm.PortraitPath(who.Name),
            Rows = Array.Empty<InkPageRow>(),
            DetailTitle = detailTitle,
            DetailNote = detailNote,
            DetailActions = Array.Empty<InkPageRow>(),
            DetailEffect = detailEffect,
            DetailRequirements = detailRequirements,
            EmptyHint = "",
            Filters = Array.Empty<string>(),
            Sorts = Array.Empty<string>(),
            Disc = new InkSkillDiscModel
            {
                CharacterName = who.Name,
                StyleLine = styleLine,
                SectorLabels = labels,
                Tiles = tiles,
                SelectedId = selected,
                FocusedSector = focusedSector,
                ViewZoom = viewZoom,
                ViewPivot = viewPivot ?? InkLayout.SkillDiscCenter,
                ViewRotation = viewRotation,
            },
        };
    }

    /// <summary>
    /// 把一个扇区的技能依次落到该扇区的格位上（由内向外）。
    /// 技能多于格位时截断（盘就这么大），少于格位时剩下的留给过路节点。
    /// </summary>
    private static void PlaceSector(
        Dictionary<(int, int, int, bool), InkSkillTile> byCell,
        List<(int Ring, int Col, bool Upper)> order,
        int sector, List<SkillDef> skills, HashSet<string> unlocked, bool alwaysUnlocked)
    {
        var index = 0;
        foreach (var skill in skills)
        {
            if (index >= order.Count)
                break;
            var (ring, col, upper) = order[index++];
            byCell[(sector, ring, col, upper)] = new InkSkillTile
            {
                Id = skill.Id,
                Name = skill.Name,
                Sector = sector,
                Ring = ring,
                Col = col,
                Upper = upper,
                Kind = InkSkillNodeKind.Skill,
                Unlocked = alwaysUnlocked || unlocked.Contains(skill.Id),
            };
        }
    }

    /// <summary>
    /// 把还没有内容的格位填成过路节点——盘面因此是满的，
    /// 技能之间也有"格与格相邻"的连通感（同 Blade&Hex 的 pip/small 占位）。
    /// 只填到一定环数：最内圈几环留空，免得盘心一圈糊住。
    /// </summary>
    private static void FillPassageNodes(
        Dictionary<(int, int, int, bool), InkSkillTile> byCell,
        List<(int Ring, int Col, bool Upper)> order)
    {
        for (var sector = 0; sector < InkLayout.SkillDiscSectors; sector++)
            foreach (var (ring, col, upper) in order)
            {
                if (byCell.ContainsKey((sector, ring, col, upper)))
                    continue;
                byCell[(sector, ring, col, upper)] = new InkSkillTile
                {
                    Sector = sector,
                    Ring = ring,
                    Col = col,
                    Upper = upper,
                    Kind = InkSkillNodeKind.Filler,
                };
            }
    }

    /// <summary>在瓦片表里按 Id 找一枚瓦片；没有则 null。</summary>
    private static InkSkillTile? FindTile(IReadOnlyList<InkSkillTile> tiles, string id)
    {
        foreach (var tile in tiles)
            if (tile.Id == id)
                return tile;
        return null;
    }

    /// <summary>把一项技能的规格面板拼成说明行，按 \n 分行喂给右栏。</summary>
    private static string SkillDetailNote(SkillDef skill)
    {
        var sb = new StringBuilder();
        sb.Append($"种类　{InkText.SkillKind(skill.Kind)}");

        if (skill.Kind is SkillKind.Strike or SkillKind.Spell)
        {
            sb.Append($"\n威力　{skill.Power}%");
            if (skill.HitMod != 0)
                sb.Append($"\n命中　{(skill.HitMod > 0 ? "+" : "")}{skill.HitMod}");
        }
        else if (skill.Kind == SkillKind.Heal)
        {
            sb.Append($"\n威力　{skill.Power}%");
        }

        sb.Append($"\n目标　{InkText.SkillTarget(skill.Target)}");
        sb.Append($"\n射程　{InkText.SkillRange(skill.Range)}");

        if (skill.ChantRounds > 0)
            sb.Append($"\n咏唱　{skill.ChantRounds} 回合");
        if (skill.Control)
            sb.Append("\n控制　打断目标咏唱");

        if (skill.Status.HasValue)
        {
            sb.Append("\n附加状态　" + skill.Status.Value switch
            {
                StatusKind.StatMod =>
                    $"{InkText.StatusStat(skill.StatusStat)} {(skill.StatusPercent > 0 ? "+" : "")}{skill.StatusPercent}%，持续 {skill.StatusRounds} 回合",
                StatusKind.Dot =>
                    $"每回合 {skill.StatusPower} 点伤害，持续 {skill.StatusRounds} 回合",
                StatusKind.Points =>
                    $"点数护盾 {skill.StatusPower} 点，持续 {skill.StatusRounds} 回合",
                _ => "",
            });
        }

        // 需求条件清单不拼进 DetailNote——它由 DetailRequirements 单独承载，
        // 渲染端在清单前只画一次「需求」标签，避免每条都重复「需求」字样。
        return sb.ToString();
    }

    /// <summary>
    /// 技能的效果描述：把这一招实际做什么用一句人话说清——
    /// 打击看威力与附加状态，法术看咏唱与威力，治疗看回复量，增益看加持。
    /// 只在规格栏底部占一行，不重复上面已列出的数值。
    /// </summary>
    private static string SkillEffectText(SkillDef skill)
    {
        var clauses = new List<string>();

        switch (skill.Kind)
        {
            case SkillKind.Strike:
                clauses.Add(skill.Target switch
                {
                    SkillTarget.AllEnemies => $"横扫{InkText.SkillTarget(skill.Target)}",
                    SkillTarget.FoesColumn => "贯穿整列敌人",
                    _ => $"命中{InkText.SkillTarget(skill.Target)}",
                });
                clauses.Add($"按 {skill.Power}% 的威力结算伤害");
                break;

            case SkillKind.Spell:
                clauses.Add(skill.ChantRounds > 0
                    ? $"咏唱 {skill.ChantRounds} 回合后放出法术"
                    : "立即放出法术");
                clauses.Add(skill.Target switch
                {
                    SkillTarget.AllEnemies => $"{skill.Power}% 威力打到全体敌人",
                    SkillTarget.FoesColumn => $"{skill.Power}% 威力贯穿整列",
                    _ => $"{skill.Power}% 威力打到{InkText.SkillTarget(skill.Target)}",
                });
                break;

            case SkillKind.Heal:
                clauses.Add($"回复{InkText.SkillTarget(skill.Target)}相当于 {skill.Power}% 的体力");
                break;

            case SkillKind.Buff:
                clauses.Add("为自己加持");
                break;

            default:
                clauses.Add($"对{InkText.SkillTarget(skill.Target)}生效");
                break;
        }

        if (skill.Control)
            clauses.Add("命中即打断目标咏唱");

        var status = StatusEffectPhrase(skill);
        if (status.Length > 0)
            clauses.Add(status);

        return string.Join("，", clauses) + "。";
    }

    /// <summary>附加状态的人话说法；没有状态返回空串。状态加在谁身上由技能目标决定。</summary>
    private static string StatusEffectPhrase(SkillDef skill)
    {
        if (!skill.Status.HasValue)
            return "";

        // 增益类把状态加在自己身上，其余加在被打中的目标身上。
        var subject = skill.Kind == SkillKind.Buff || skill.Target == SkillTarget.Self
            ? "自身"
            : "目标";

        return skill.Status.Value switch
        {
            StatusKind.StatMod =>
                $"让{subject}的{InkText.StatusStat(skill.StatusStat)}" +
                $"{(skill.StatusPercent >= 0 ? "提高" : "降低")} " +
                $"{System.Math.Abs(skill.StatusPercent)}%，持续 {skill.StatusRounds} 回合",
            StatusKind.Dot =>
                $"使{subject}每回合流失 {skill.StatusPower} 点体力，持续 {skill.StatusRounds} 回合",
            StatusKind.Points =>
                $"为{subject}张开 {skill.StatusPower} 点的护盾，持续 {skill.StatusRounds} 回合",
            _ => "",
        };
    }

    /// <summary>
    /// 技能的**学习需求**清单：只有等级类条件（流派熟练／核心属性／生活技能／素质／前置技能）。
    /// 「须装备某流派」是**使用条件**（要拿着对应武器才能出手），不属于学习需求，这里不列。
    /// 未达成的标在条上；渲染端在清单前只画一次「需求」标签，各条列在其下。
    /// </summary>
    private static List<InkDetailRequirement> GateLines(SkillDef skill, CharacterState who,
        IReadOnlySet<string> unlocked)
    {
        var lines = new List<InkDetailRequirement>();
        var gate = skill.Gate;
        var misses = gate.Unmet(who, unlocked);
        bool Missed(SkillGateKind kind, int subject) =>
            System.Linq.Enumerable.Any(misses, m => m.Kind == kind && m.Subject == subject);
        bool MissedPrereq(string id) =>
            System.Linq.Enumerable.Any(misses, m => m.Kind == SkillGateKind.Prerequisite && m.SubjectId == id);

        void Add(string text, bool missed) =>
            lines.Add(new InkDetailRequirement { Text = text, Unmet = missed });

        // 流派熟练等级：学习需求里唯一的「等级」项。
        if (gate.Style.HasValue && gate.StyleLevel > 0)
            Add($"{InkText.Style(gate.Style.Value)}熟练 {gate.StyleLevel}",
                Missed(SkillGateKind.StyleLevel, (int)gate.Style.Value));

        if (gate.Core != null)
            foreach (var requirement in gate.Core)
                Add($"{InkText.CoreStat(requirement.Stat)} {requirement.Min}",
                    Missed(SkillGateKind.CoreStat, (int)requirement.Stat));

        if (gate.Life != null)
            foreach (var requirement in gate.Life)
                Add($"{InkText.LifeSkill(requirement.Skill)} {requirement.Min}",
                    Missed(SkillGateKind.LifeSkill, (int)requirement.Skill));

        if (gate.Traits != null)
            foreach (var trait in gate.Traits)
                Add($"素质 {InkText.TraitName(trait)}",
                    Missed(SkillGateKind.Trait, (int)trait));

        if (gate.Prerequisites != null)
            foreach (var id in gate.Prerequisites)
                Add($"先会 {SkillTable.Get(id)?.Name ?? id}", MissedPrereq(id));

        if (lines.Count == 0)
            lines.Add(new InkDetailRequirement { Text = "通用（人人自带，无前置需求）" });

        return lines;
    }

    // ---------- 日程 ----------

    /// <summary>
    /// 日程：左侧全员成员列表（含玩家本人），右侧上排四段安排，
    /// 下半房间网格（选房间）＋该房间的设施列表（选设施）。
    /// </summary>
    private static InkPageModel Schedule(InkViewModel vm, CharacterState who, int selected, int roomId, int facilityFirst, int memberFirst = 0)
    {
        // 1. 左侧全员列表：玩家（领主）排第一行，随后列出所有成员
        var members = new List<InkPageRow>();
        var roster = vm.Hub.State.Roster;
        var allCharacters = new List<CharacterState>();
        if (roster.Master != null)
            allCharacters.Add(roster.Master);
        foreach (var m in roster.Members)
        {
            if (!m.IsMaster)
                allCharacters.Add(m);
        }

        var targetWho = who;
        if (!allCharacters.Exists(c => c.Id == targetWho.Id))
            targetWho = allCharacters.FirstOrDefault() ?? who;

        foreach (var c in allCharacters)
        {
            var isCurrent = c.Id == targetWho.Id;
            var name = c.IsMaster ? $"{c.Name} (领主)" : c.Name;
            members.Add(new InkPageRow
            {
                Name = name,
                // 工种取自这段人当前排到的设施；没排就是空闲。
                Value = JobOf(vm, c.Id),
                Note = isCurrent ? "排班中" : "",
                TargetNumber = c.Id,
                Selected = isCurrent,
                Enabled = true,
            });
        }

        var rows = new List<InkPageRow>();
        var schedule = vm.Hub.ScheduleOf(targetWho.Id);
        var editable = true;

	        for (var slot = 0; slot < WorkSlot.Count; slot++)
	        {
	            var assignment = schedule.Slots[slot];
	            var hasWork = assignment.Mode == SlotMode.Work && assignment.FacilityId >= 0;
	            rows.Add(new InkPageRow
	            {
	                // 第一行时间、第二行干什么、第三行 房间-设施-产出。
	                Name = InkText.WorkSlot(slot),
	                Value = hasWork ? "工作" : "空闲",
	                Note = hasWork ? ChainOf(vm, assignment.FacilityId) : "",
	                Enabled = editable,
	            });
	        }

        var sel = !editable ? -1 : selected < 0 ? 0 : System.Math.Min(selected, WorkSlot.Count - 1);

        // 下半：房间网格 + 选中房间的设施列表。
        var rooms = new List<InkDevRoomCell>();
        foreach (var room in vm.Hub.Map())
        {
            if (!room.Open || !InkLayout.InGrid(room.X, room.Y))
                continue;
            rooms.Add(new InkDevRoomCell
            {
                Id = room.Id,
                X = room.X,
                Y = room.Y,
                Name = room.Name,
                Selected = room.Id == roomId,
            });
        }

        var roomName = "";
        var facilities = new List<InkPageRow>();
        var currentFacility = sel >= 0 ? schedule.Slots[sel].FacilityId : -1;
        if (roomId >= 0)
        {
            foreach (var fixture in vm.Hub.Map())
            {
                if (fixture.Id != roomId)
                    continue;
                roomName = fixture.Name;
                break;
            }
            foreach (var fixture in vm.Hub.FixturesIn(roomId))
            {
                if (!fixture.Built)
                    continue;
                if (!vm.Hub.FacilityIsWorkbench(fixture.Id))
                    continue;
                facilities.Add(new InkPageRow
                {
                    Name = fixture.Name,
                    Value = fixture.Id == currentFacility ? "已排" : "",
                    Action = InkPageAction.AssignTask,
                    TargetNumber = fixture.Id,
                    Enabled = editable,
                });
            }
        }

        // 动作按钮栏清空：取消按钮直接落在时段卡上，底部仅展示可选产出。
        var actions = Array.Empty<InkPageRow>();

        // 下方详情：当前时段说明与该设施可采集/制作的物品。
        var outputs = new List<InkPageRow>();
        var title = "";
        var noteText = "";
        if (sel >= 0)
        {
            var assignment = schedule.Slots[sel];
            title = $"{InkText.WorkSlot(sel)}　{targetWho.Name}";
            var targetFacilityId = assignment.FacilityId >= 0 ? assignment.FacilityId : currentFacility;
            var targetFacility = targetFacilityId >= 0
                ? vm.Hub.State.Territory.Facilities.Find(f => f.Id == targetFacilityId)
                : null;

            outputs = OutputsOf(vm, targetFacility);
            var sb = new StringBuilder();
            if (targetFacility != null && vm.Hub.FacilityIsWorkbench(targetFacility.Id))
            {
                sb.AppendLine($"工作　{targetFacility.Name}");

                // 1. 可采集物品
                if (!string.IsNullOrEmpty(targetFacility.YieldItemId))
                {
                    sb.AppendLine($"可采集　{targetFacility.YieldItemId}");
                }

                // 2. 可制作物品
                var recipes = vm.Hub.State.Territory.Recipes
                    .Where(r => targetFacility.Supports(r.Station))
                    .ToList();
                if (recipes.Count > 0)
                {
                    sb.AppendLine("可制作");
                    foreach (var r in recipes)
                    {
                        var costs = string.Join(" ", r.Costs.Select(c => $"{c.ItemId}×{c.Count}"));
                        sb.AppendLine(costs.Length > 0 ? $"◇ {r.ItemId}　{costs}" : $"◇ {r.ItemId}");
                    }
                }
            }
            else if (targetFacility != null)
            {
                sb.AppendLine(targetFacility.Name);
            }
            else
            {
                sb.AppendLine("空闲");
            }
            noteText = sb.ToString().TrimEnd();
        }

        return new InkPageModel
        {
            Page = InkPage.Schedule,
            Title = $"日程　{targetWho.Name}",
            CharacterName = targetWho.Name,
            PortraitPath = vm.PortraitPath(targetWho.Name),
            Rows = rows,
            SelectedRow = sel,
            DetailTitle = title,
            DetailNote = noteText,
            DetailActions = actions,
            DetailActionGrid = true,
            EmptyHint = "",
            Filters = Array.Empty<string>(),
            Sorts = Array.Empty<string>(),
            Work = new InkWorkModel
            {
                Members = members,
                SelectedMemberId = targetWho.Id,
                MemberFirst = memberFirst,
                Rooms = rooms,
                RoomId = roomId,
                RoomName = roomName,
                Facilities = facilities,
                FacilityFirst = Math.Clamp(facilityFirst, 0, Math.Max(0, facilities.Count - 1)),
                AssignedFacilityId = currentFacility,
                DetailTitle = title,
                DetailNote = noteText,
                DetailActions = actions,
                Outputs = outputs,
            },
        };
    }

    /// <summary>
    /// 这个人的工种：看他排到的设施支持哪种工作行动（行动 → 工种）。
    /// 一段都没排就是空闲；玩家没有设施可排时同样报空闲。
    /// </summary>
    private static string JobOf(InkViewModel vm, int characterId)
    {
        var schedule = vm.Hub.ScheduleOf(characterId);
        foreach (var slot in schedule.Slots)
        {
            if (slot.FacilityId < 0)
                continue;
            var facility = vm.Hub.State.Territory.Facilities.Find(f => f.Id == slot.FacilityId);
            if (facility == null)
                continue;
            foreach (var action in facility.Actions)
            {
                var type = Rimisekai.Housing.ActionKindMap.TypeOf(action);
                if (type.HasValue)
                    return InkText.WorkType(type.Value);
            }
        }
        return "空闲";
    }

    /// <summary>
    /// 时段卡第三行：房间-设施-产出。只有真正的工作设施才拼产出链；
    /// 非工作设施（如水井、床、沙发等纯摆设或起居设施）绝不报工作链。
    /// </summary>
    private static string ChainOf(InkViewModel vm, int facilityId)
    {
        var facility = vm.Hub.State.Territory.Facilities.Find(f => f.Id == facilityId);
        if (facility == null || !vm.Hub.FacilityIsWorkbench(facility.Id))
            return "";
        var room = vm.Hub.State.Territory.Rooms.Find(r => r.Id == facility.RoomId);
        var parts = new List<string>();
        if (room != null && room.Name.Length > 0)
            parts.Add(room.Name);
        if (facility.Name.Length > 0)
            parts.Add(facility.Name);
        var output = OutputOf(vm, facility);
        if (output.Length > 0)
            parts.Add(output);
        return string.Join("-", parts);
    }

    /// <summary>工作设施的产出：采集物，或它支持制作的第一种成品。非工作设施无产出。</summary>
    private static string OutputOf(InkViewModel vm, Facility facility)
    {
        if (!vm.Hub.FacilityIsWorkbench(facility.Id))
            return "";
        if (!string.IsNullOrEmpty(facility.YieldItemId))
            return facility.YieldItemId;
        foreach (var recipe in vm.Hub.State.Territory.Recipes)
        {
            if (facility.Supports(recipe.Station))
                return recipe.ItemId;
        }
        return "";
    }

    /// <summary>
    /// 选中设施的可选产出：直接采集物一条，加上它支持制作的每条配方成品。
    /// 只报成品本体与材料，不另加任何说明性文字。
    /// 仅工作设施有可选产出，非工作设施（水井、床等）绝对不产出。
    /// </summary>
    private static List<InkPageRow> OutputsOf(InkViewModel vm, Facility? facility)
    {
        var list = new List<InkPageRow>();
        if (facility == null || !vm.Hub.FacilityIsWorkbench(facility.Id))
            return list;
        if (!string.IsNullOrEmpty(facility.YieldItemId))
        {
            list.Add(new InkPageRow
            {
                Name = facility.YieldItemId,
                Value = "",
            });
        }
        foreach (var recipe in vm.Hub.State.Territory.Recipes)
        {
            if (!facility.Supports(recipe.Station))
                continue;
            var costs = string.Join(" ", recipe.Costs.Select(c => $"{c.ItemId}×{c.Count}"));
            list.Add(new InkPageRow
            {
                Name = recipe.ItemId,
                Value = costs,
            });
        }
        return list;
    }

    /// <summary>时段安排的一行小注：点了设施的写设施名，空闲写空。</summary>
    private static string SlotNote(InkViewModel vm, SlotAssignment assignment) =>
        assignment.FacilityId < 0
            ? ""
            : vm.Hub.FacilityName(assignment.FacilityId);

    // ---------- 行构造小工具 ----------

    private static InkPageRow Bar(string name, int value, int max)
    {
        var safeMax = max <= 0 ? 1 : max;
        return new InkPageRow
        {
            Name = name,
            Value = $"{value} / {safeMax}",
            Note = "",
            MeterValue = value,
            MeterMax = safeMax,
            Enabled = false,
        };
    }

    private static InkPageRow Value(string name, string value, string note = "") => new()
    {
        Name = name,
        Value = value,
        Note = note,
        Enabled = false,
    };

    private static InkPageRow Section(string title) => new()
    {
        Name = title,
        IsHeading = true,
        Enabled = false,
    };

    /// <summary>把关系标记拼成一行文本。关系表存的是对方 Id，界面显示名字。</summary>
    private static string Relations(InkViewModel vm, CharacterState who)
    {
        if (who.Relations.Count == 0)
            return "无";

        var parts = new List<string>(who.Relations.Count);
        foreach (var pair in who.Relations)
        {
            var other = vm.NameOf(pair.Key);
            foreach (var flag in pair.Value)
                parts.Add($"{other}:{InkTextRelation(flag)}");
        }
        return parts.Count == 0 ? "无" : string.Join("　", parts);
    }

    private static string InkTextRelation(RelationFlag flag) => flag switch
    {
        RelationFlag.Acquainted => "相识",
        RelationFlag.Trusted => "信任",
        RelationFlag.Sworn => "誓约",
        RelationFlag.Rival => "敌对",
        RelationFlag.Marked => "刻印",
        _ => "?",
    };
}
