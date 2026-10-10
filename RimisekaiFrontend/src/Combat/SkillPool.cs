using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Defs;

namespace Rimisekai.Combat;

/// <summary>
/// 身份技能池（拟案，待主人核定）。技能池按身份取索引（<see cref="IdentitySkillPoolDef"/>）：本身份 20 式基础＋3 式核心，
/// 再并入风格对得上的通用技能（<see cref="IdentitySkillPoolDef.SharedSkills"/>）当基础技能。
/// 角色的池＝3 式核心全收（主人 2026-10-11 定），再从基础技能里不放回地抽 <see cref="BasicDraw"/> 式。
/// 池里的技能直接会用（不走流派门槛与派生学习），机制点核心是被动、不进菜单。
/// NPC 生成时按自己的身份抽一次；玩家角色特权：在领地里可随时自选身份重抽（<see cref="Hub.HubSession.RerollSkillPool"/>）。
/// </summary>
public static class SkillPool
{
    /// <summary>每池从基础技能里抽几式（提案值，调参先过主人）。</summary>
    public const int BasicDraw = 5;

    /// <summary>有技能池的身份（defName）。</summary>
    public static IReadOnlyList<IdentitySkillPoolDef> Pools
    {
        get
        {
            DefLoader.EnsureInitialized();
            return DefDatabase<IdentitySkillPoolDef>.All;
        }
    }

    public static IdentitySkillPoolDef? PoolOf(string identity)
    {
        DefLoader.EnsureInitialized();
        return DefDatabase<IdentitySkillPoolDef>.Get(identity);
    }

    /// <summary>按 Id 查身份技能（全部身份池里找）。</summary>
    public static SkillDef? Find(string id) =>
        Pools.SelectMany(p => p.Skills).FirstOrDefault(s => s.Id == id);

    /// <summary>一个身份的核心技能。</summary>
    public static IEnumerable<SkillDef> Cores(IdentitySkillPoolDef pool) =>
        pool.Skills.Where(s => s.Core != CoreKind.None);

    /// <summary>一个身份的基础技能：本身份的基础式＋并入的通用技能。</summary>
    public static IEnumerable<SkillDef> Basics(IdentitySkillPoolDef pool) =>
        pool.Skills.Where(s => s.Core == CoreKind.None).Concat(pool.SharedSkills.Select(id => SkillTable.Get(id)!));

    /// <summary>从某身份的池里抽：核心全收，基础技能抽 <see cref="BasicDraw"/> 式。</summary>
    public static List<string> Draw(IdentitySkillPoolDef pool, Random rng) =>
        Cores(pool).Concat(Basics(pool).OrderBy(_ => rng.Next()).Take(BasicDraw)).Select(s => s.Id).ToList();

    /// <summary>给角色换一池：记下按哪个身份抽的，再抽。身份没有技能池就清空。</summary>
    public static void Assign(CharacterState c, string identity, Random rng)
    {
        c.PoolIdentity = identity;
        c.SkillPool.Clear();
        if (PoolOf(identity) is { } pool)
            c.SkillPool.AddRange(Draw(pool, rng));
    }

    /// <summary>角色池里的技能定义（按池里的顺序）。</summary>
    public static IEnumerable<SkillDef> Skills(CharacterState c) => c.SkillPool.Select(id => SkillTable.Get(id)!);
}
