using System.Collections.Generic;
using System.Linq;

namespace Rimisekai.Defs;

/// <summary>
/// 游戏内容定义查询门面。底层全部从 content/defs/*.json 数据表由 DefLoader 载入 DefDatabase。
/// 本类不含任何硬编码设定或数据。
/// </summary>
public static class ContentDefs
{
    public static string TerritoryName
    {
        get
        {
            EnsureInitialized();
            return DefDatabase<WorldSeedDef>.GetNamed("World_Standard").TerritoryName;
        }
    }

    public static IReadOnlyList<string> Foods
    {
        get
        {
            EnsureInitialized();
            return DefDatabase<WorldSeedDef>.GetNamed("World_Standard").Foods;
        }
    }

    public static IReadOnlyList<RoomDef> BuildingRooms
    {
        get
        {
            EnsureInitialized();
            return DefLoader.BuildingRooms;
        }
    }

    public static IReadOnlyList<FacilityDef> BuildingFacilities
    {
        get
        {
            EnsureInitialized();
            return DefLoader.BuildingFacilities;
        }
    }

    public static IReadOnlyList<RoomDef> AreaRooms
    {
        get
        {
            EnsureInitialized();
            return DefLoader.AreaRooms;
        }
    }

    public static IReadOnlyDictionary<int, int[]> AreaLinks
    {
        get
        {
            EnsureInitialized();
            return DefLoader.AreaLinks;
        }
    }

    public static IReadOnlyList<AreaFacilityDef> AreaFacilities
    {
        get
        {
            EnsureInitialized();
            return DefLoader.AreaFacilities;
        }
    }

    public static NewGameSeedDef StandardSeed
    {
        get
        {
            EnsureInitialized();
            return DefDatabase<NewGameSeedDef>.GetNamed("Standard");
        }
    }

    public static NewGameSeedDef HardSeed
    {
        get
        {
            EnsureInitialized();
            return DefDatabase<NewGameSeedDef>.GetNamed("Hard");
        }
    }

    /// <summary>装配前调用一次：确保全部数据表装进数据库。重复调用无害。</summary>
    public static void EnsureInitialized()
    {
        DefLoader.EnsureInitialized();
    }
}
