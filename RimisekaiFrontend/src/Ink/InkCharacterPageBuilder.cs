using System;
using System.Collections.Generic;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 角色资料页：状态 / 技能 / 日程。
/// 状态与技能是只读展示；日程可派活——选中一段，再点任务把这段改成那件事。
/// </summary>
public static class InkCharacterPageBuilder
{
    /// <summary>按页类型构建；不是这三页时返回 null。</summary>
    public static InkPageModel? Build(InkViewModel vm, InkPage page, CharacterState? who, int selected = -1)
    {
        if (page is not (InkPage.Status or InkPage.Skills or InkPage.Schedule))
            return null;

        if (who == null)
            return EmptyPage(page);

        return page switch
        {
            InkPage.Status => Status(vm, who),
            InkPage.Skills => Skills(vm, who),
            InkPage.Schedule => Schedule(vm, who, selected),
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

    /// <summary>状态：体力/气力/疲劳/好感/心情，以及战斗面板与关系。</summary>
    private static InkPageModel Status(InkViewModel vm, CharacterState who)
    {
        var rows = new List<InkPageRow>();
        var c = who.Condition;

        rows.Add(Bar("体力", c.Stamina, c.MaxStamina));
        rows.Add(Bar("气力", c.Spirit, c.MaxSpirit));
        rows.Add(Bar("疲劳", c.Fatigue, Vitals.TiredAt));
        rows.Add(Value("好感", $"{c.Favor}", InkText.Bond(c.Bond)));
        rows.Add(Bar("心情", who.Affect.Mood, 100));

        var sheet = who.Combat;
        rows.Add(Value("攻击", $"{sheet.Attack}"));
        rows.Add(Value("血量", $"{sheet.MaxHp}"));
        rows.Add(Value("防御", $"{sheet.Defence}"));
        rows.Add(Value("闪避", $"{sheet.Dodge}"));
        rows.Add(Value("法术", $"{sheet.SpellPower}"));
        rows.Add(Value("威胁", $"{sheet.Threat}"));

        var relations = Relations(vm, who);
        if (relations.Length > 0)
            rows.Add(Value("关系", relations));

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
        };
    }

    // ---------- 技能 ----------

    /// <summary>技能：生活 / 武器 / 流派。等级由经验换算。</summary>
    private static InkPageModel Skills(InkViewModel vm, CharacterState who)
    {
        var rows = new List<InkPageRow>();

        rows.Add(Section("生活"));
        for (var i = 0; i < AttributeMap.LifeCount; i++)
        {
            var skill = (LifeSkill)i;
            var exp = who.LifeExp[i];
            // 有效值 = 对应核心 + 经验/100，与 Core 的 Life() 口径一致。
            rows.Add(Value(InkText.LifeSkill(skill), $"{who.Life(skill)}",
                exp > 0 ? $"经验 {exp}" : ""));
        }

        rows.Add(Section("武器"));
        foreach (var type in Enum.GetValues<WeaponType>())
        {
            var p = who.Weapons[(int)type];
            rows.Add(Value(InkText.Weapon(type), $"Lv{p.Level}",
                p.Exp > 0 ? $"经验 {p.Exp}" : ""));
        }

        rows.Add(Section("流派"));
        foreach (var style in Enum.GetValues<StyleType>())
        {
            var p = who.Styles[(int)style];
            rows.Add(Value(InkText.Style(style), $"Lv{p.Level}",
                p.Exp > 0 ? $"经验 {p.Exp}" : ""));
        }

        return new InkPageModel
        {
            Page = InkPage.Skills,
            Title = $"技能　{who.Name}",
            CharacterName = who.Name,
            PortraitPath = vm.PortraitPath(who.Name),
            Rows = rows,
            EmptyHint = "",
            Filters = Array.Empty<string>(),
            Sorts = Array.Empty<string>(),
        };
    }

    // ---------- 日程 ----------

    /// <summary>时段三类开关的显示名，顺序即按钮顺序。</summary>
    private static readonly SlotMode[] SlotModes =
    {
        SlotMode.Free,
        SlotMode.Work,
        SlotMode.Rest,
    };

    /// <summary>
    /// 日程：四段开关（空闲 / 工作 / 不干活）。选中一段后，右栏列三个开关，点一下改这段。
    /// 工作时段里具体干什么由工作页的优先级决定，不在这里选。
    /// 主角没有日程（自己不用被排班），只读展示。
    /// </summary>
    private static InkPageModel Schedule(InkViewModel vm, CharacterState who, int selected)
    {
        var rows = new List<InkPageRow>();
        var schedule = vm.Hub.ScheduleOf(who.Id);
        var editable = !who.IsMaster;

        for (var slot = 0; slot < WorkSlot.Count; slot++)
        {
            var mode = schedule.Slots[slot];
            rows.Add(new InkPageRow
            {
                Name = InkText.WorkSlot(slot),
                Value = InkText.SlotMode(mode),
                Note = mode == SlotMode.Work ? "按工作页的优先级挑活" : "",
                Enabled = editable,
            });
        }

        var sel = !editable ? -1 : selected < 0 ? 0 : System.Math.Min(selected, WorkSlot.Count - 1);
        var actions = new List<InkPageRow>();
        var title = "";
        var noteText = "";
        if (sel >= 0)
        {
            var current = schedule.Slots[sel];
            title = $"{InkText.WorkSlot(sel)}　{who.Name}";
            noteText = "选这段时间做什么。\n工作时段里干什么由工作页的优先级决定。";
            foreach (var mode in SlotModes)
            {
                actions.Add(new InkPageRow
                {
                    Name = InkText.SlotMode(mode),
                    Action = InkPageAction.AssignTask,
                    TargetNumber = sel,
                    TargetId = mode.ToString(),
                    Selected = mode == current,
                });
            }
        }

        return new InkPageModel
        {
            Page = InkPage.Schedule,
            Title = $"日程　{who.Name}",
            CharacterName = who.Name,
            PortraitPath = vm.PortraitPath(who.Name),
            Rows = rows,
            SelectedRow = sel,
            DetailTitle = title,
            DetailNote = noteText,
            DetailActions = actions,
            DetailActionGrid = true,
            EmptyHint = "",
            Filters = Array.Empty<string>(),
            Sorts = Array.Empty<string>(),
        };
    }

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
