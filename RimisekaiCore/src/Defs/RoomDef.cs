using System;
using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 房间定义（继承通用 Def 基类）。
/// 纯数据驱动，由 DefDatabase<RoomDef> 统一管理，不再与具体格子实例绑定。
/// </summary>
public sealed class RoomDef : Def
{
    /// <summary>数值 Id（兼容旧逻辑按 Id 索引）。</summary>
    public int Id { get; init; }

    /// <summary>房间中文名称，默认与 Label 同步。</summary>
    public string Name
    {
        get => string.IsNullOrEmpty(Label) ? DefName : Label;
        init => Label = value;
    }

    /// <summary>建造需要的领地等级（默认 1 级即可）。</summary>
    public int MinTerritoryLevel { get; init; } = 1;

    public int RegionId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int OpenCost { get; init; }
    public RoomPermission Permission { get; init; } = RoomPermission.Public;
    public bool StartOpen { get; init; }
    public List<RecipeCost> MaterialCost { get; init; } = new();
    public bool Buildable { get; init; }

    /// <summary>房间细分标签（至少 1 个，无上限，如室内/室外/工作间/娱乐室/卧室）。</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>预置/推荐在该房间内的设施 DefName 列表（解耦建筑模板与具体设施实例）。</summary>
    public List<string> FacilityDefs { get; init; } = new();

    public Room ToRuntime()
    {
        var room = new Room
        {
            Id = Id,
            Name = Name,
            RegionId = RegionId,
            X = X,
            Y = Y,
            Open = StartOpen,
            OpenCost = OpenCost,
            Permission = Permission,
            Buildable = Buildable,
        };
        room.MaterialCost.AddRange(MaterialCost);
        foreach (var tag in Tags)
            room.AddTag(tag);
        room.EnsureDefaultTag();
        return room;
    }
}
