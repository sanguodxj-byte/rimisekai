using System.Collections.Generic;

namespace Rimisekai.Defs;

/// <summary>
/// 开局摆位：开局据点里已经摆好的那一件设施。
///
/// 这不是定义——设施定义只有 buildings.json 一份（<see cref="FacilityDef"/>）。
/// 摆位只回答三个问题：摆的是哪份定义、摆在哪个房间、开局时里面已存着什么。
/// 实例 Id 也在这里：定义表里的 Id 是建造目录的排序键，开局每个实例有自己的号。
/// </summary>
public sealed class AreaFacilityDef
{
    /// <summary>所引用设施定义的 DefName。</summary>
    public string FacilityDefName { get; init; } = "";

    /// <summary>开局实例的 Id（领地内唯一）。</summary>
    public int Id { get; init; }

    /// <summary>摆在哪间房（Room.Id）。</summary>
    public int RoomId { get; init; }

    /// <summary>开局时设施里已存着的东西。</summary>
    public Dictionary<string, int> Contents { get; init; } = new();

    /// <summary>这张床是主人的（开局卧室那张）。别人好感不够同床就不睡它，见 Territory.MasterBedId。</summary>
    public bool MasterBed { get; init; }
}
