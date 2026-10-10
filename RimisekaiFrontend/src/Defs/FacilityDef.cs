using System;
using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 设施定义（继承通用 Def 基类）。
/// 纯数据驱动，由 DefDatabase<FacilityDef> 统一管理，脱离与具体房间的嵌套。
/// </summary>
public sealed class FacilityDef : Def, IIdentifiedDef
{
    /// <summary>数值 Id（兼容旧逻辑按 Id 索引）。</summary>
    public int Id { get; init; }

    /// <summary>设施中文名称，默认与 Label 同步。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(Label) ? DefName : Label;
        init
        {
            Label = value;
            if (string.IsNullOrEmpty(DefName))
                DefName = !string.IsNullOrEmpty(value) ? value : $"Facility_{Id}";
        }
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

    /// <summary>
    /// 售价基准。设施只卖不买，这个价是卖给店时的依据。
    /// 0 表示按取得成本推算——设施自己带的料钱，没有料钱就按它产出的东西计价。
    /// 设施与房间是两张独立的表：一件设施可以摆进任何开着的房，定价不查房间。
    /// </summary>
    public int MarketValue { get; init; }

    /// <summary>实际基准价：显式价优先，否则按取得成本推算。</summary>
    public int Value()
    {
        if (MarketValue > 0)
            return MarketValue;

        var own = CostOf(MaterialCost);
        if (own > 0)
            return own;

        // 天然资源点（草药丛、矿脉）没有料钱，按它产出的东西计价。
        if (YieldItemId.Length > 0)
        {
            var yield = Items.Get(YieldItemId);
            if (yield != null && yield.MarketValue > 0)
                return yield.MarketValue;
        }
        return 1;
    }

    private static int CostOf(List<RecipeCost> costs)
    {
        if (costs == null || costs.Count == 0)
            return 0;
        var total = 0;
        foreach (var cost in costs)
            total += (Items.Get(cost.ItemId)?.MarketValue ?? 1) * cost.Count;
        return total;
    }

    /// <summary>这件设施能支撑的日常行动，与用途解耦。</summary>
    public List<ActionKind> Actions { get; init; } = new();

    /// <summary>是不是桌子。桌子本身无行动，只让同房吃饭的人不扣心情。</summary>
    public bool IsTable { get; init; }

    /// <summary>能不能存东西。物品只存在能存的设施里（或角色背包里），没有虚空库存。</summary>
    public bool Storage { get; init; }

    /// <summary>设施的仓储容量上限。</summary>
    public int StorageCapacity { get; init; } = 999;

    /// <summary>允许存入的品类或物品白名单（空表示不限）。</summary>
    public List<string> StorageFilter { get; init; } = new();

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
            StorageCapacity = StorageCapacity,
        };
        facility.MaterialCost.AddRange(MaterialCost);
        foreach (var filter in StorageFilter)
            facility.StorageFilter.Add(filter);
        foreach (var action in Actions)
            facility.Actions.Add(action);
        foreach (var pair in Contents)
            facility.Contents.Add(pair.Key, pair.Value);
        return facility;
    }
}
