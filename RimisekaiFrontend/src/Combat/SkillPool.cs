using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Defs;

namespace Rimisekai.Combat;

/// <summary>
/// 身份技能池（拟案，待主人核定）。技能池按身份取索引（<see cref="IdentitySkillPoolDef"/>，每身份 20 基础＋3 核心）；
/// 角色的池是从中抽出的 <see cref="PoolSize"/> 式：先必中一式核心，其余从剩下的 22 式里不放回地抽（可能再中核心）。
/// 池里的技能直接会用（不走流派门槛与派生学习），机制点核心是被动、不进菜单。
/// NPC 生成时按自己的身份抽一次；玩家角色特权：在领地里可随时自选身份重抽（<see cref="Hub.HubSession.RerollSkillPool"/>）。
/// </summary>
public static class SkillPool
{
    /// <summary>一个角色的池有几式（提案值，调参先过主人）。</summary>
    public const int PoolSize = 6;

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

    /// <summary>从某身份的池里抽 <see cref="PoolSize"/> 式：必中一式核心，余下随机。</summary>
    public static List<string> Draw(IdentitySkillPoolDef pool, Random rng)
    {
        var cores = pool.Skills.Where(s => s.Core != CoreKind.None).ToList();
        var first = cores[rng.Next(cores.Count)];
        var rest = pool.Skills.Where(s => s != first).OrderBy(_ => rng.Next()).Take(PoolSize - 1);
        return new[] { first }.Concat(rest).Select(s => s.Id).ToList();
    }

    /// <summary>给角色换一池：记下按哪个身份抽的，再抽。身份没有技能池就清空。</summary>
    public static void Assign(CharacterState c, string identity, Random rng)
    {
        c.PoolIdentity = identity;
        c.SkillPool.Clear();
        if (PoolOf(identity) is { } pool)
            c.SkillPool.AddRange(Draw(pool, rng));
    }

    /// <summary>角色池里的技能定义（按池里的顺序）。</summary>
    public static IEnumerable<SkillDef> Skills(CharacterState c) => c.SkillPool.Select(id => Find(id)!);
}
