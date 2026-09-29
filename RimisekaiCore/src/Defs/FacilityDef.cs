using System;
using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 设施定义（继承通用 Def 基类）。
/// 纯数据驱动，由 DefDatabase<FacilityDef> 统一管理，脱离与具体房间的嵌套。
/// </summary>
public sealed class FacilityDef : Def
{
    /// <summary>数值 Id（兼容旧逻辑按 Id 索引）。</summary>
    public int Id { get; init; }

    /// <summary>设施中文名称，默认与 Label 同步。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(Label) ? DefName : Label;
        init => Label = value;
    }

    public int RoomId { get; init; }
    public FacilityUsage Usage { get; init; }
    public int Capacity { get; init; } = 1;
    public int BuildCost { get; init; }
    public bool StartBuilt { get; init; } = true;
    public string EffectId { get; init; } = "";
    public string YieldItemId { get; init; } = "";
    public List<RecipeCost> MaterialCost { get; init; } = new();
    public bool Buildable { get; init; }

    /// <summary>这件设施能支撑的日常行动，与用途解耦。</summary>
    public List<ActionKind> Actions { get; init; } = new();

    /// <summary>是不是桌子。桌子本身无行动，只让同房吃饭的人不扣心情。</summary>
    public bool IsTable { get; init; }

    /// <summary>能不能存东西。物品只存在能存的设施里（或角色背包里），没有虚空库存。</summary>
    public bool Storage { get; init; }

    /// <summary>开局时这件设施里已存着的东西。</summary>
    public Dictionary<string, int> Contents { get; init; } = new();

    public Facility ToRuntime()
    {
        var facility = new Facility
        {
            Id = Id,
            Name = Name,
            RoomId = RoomId,
            Usage = Usage,
            Capacity = Capacity,
            BuildCost = BuildCost,
            Built = StartBuilt,
            EffectId = EffectId,
            YieldItemId = YieldItemId,
            Buildable = Buildable,
            IsTable = IsTable,
            CanStore = Storage,
        };
        facility.MaterialCost.AddRange(MaterialCost);
        foreach (var action in Actions)
            facility.Actions.Add(action);
        foreach (var pair in Contents)
            facility.Contents.Add(pair.Key, pair.Value);
        return facility;
    }
}
