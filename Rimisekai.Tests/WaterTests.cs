using System.Collections.Generic;
using Rimisekai.Housing;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 井水：取水不是工作（有气力消耗的才是工作，取水与移动等价）。
/// 井就是现成的水源——水放井的存货里，每日回满；做饭缺水由搬运行为去井里搬，
/// 玩家下厨也直接从井里扣。
/// </summary>
public sealed class WaterTests
{
    private static Facility NewWell(int id, int roomId = 1) => new()
    {
        Id = id, Name = "水井", RoomId = roomId, YieldItemId = "水", Built = true,
        // 井能存水由内容包声明（content 里 storage: true），测试装置与数据同口径。
        CanStore = true,
    };

    [Fact]
    public void New_well_comes_full_and_tops_up_daily()
    {
        var state = new GameState();
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });

        var well = NewWell(1);
        state.Territory.AddFacility(well);
        // 新落的井自带满水。
        Assert.Equal(Territory.WellWaterCap, well.Contents.Get("水"));

        // 用掉一部分，隔日回满。
        well.Contents.Add("水", -(Territory.WellWaterCap - 3));
        Assert.Equal(3, well.Contents.Get("水"));
        state.SettleDay(state.Clock.Season);
        Assert.Equal(Territory.WellWaterCap, well.Contents.Get("水"));
    }

    [Fact]
    public void Cooking_pays_water_from_the_well()
    {
        var state = new GameState();
        state.Territory.AddRoom(new Room { Id = 1, Name = "庭院", Open = true });
        state.Territory.AddRoom(new Room { Id = 2, Name = "灶房", Open = true });
        state.Territory.Link(1, 2);
        var well = NewWell(1, roomId: 1);
        state.Territory.AddFacility(well);
        var master = state.Roster.Add("你", master: true);

        // 做饭缺水时，备料/付料的查找能落到井上。
        var stock = state.Territory.FindStockOf("水", preferRoomId: 2);
        Assert.Same(well, stock);

        // 炖菜要 1 份水：背包没有，从井里扣。
        var costs = new List<RecipeCost> { new() { ItemId = "水", Count = 1 } };
        Assert.True(state.Territory.CanPayWith(master, costs));
        Assert.True(state.Territory.PayWith(master, costs));
        Assert.Equal(0, master.Bag.Get("水"));
        Assert.Equal(Territory.WellWaterCap - 1, well.Contents.Get("水"));
    }

    [Fact]
    public void Dry_facility_is_not_a_well()
    {
        var state = new GameState();
        state.Territory.AddRoom(new Room { Id = 1, Name = "库房", Open = true });
        var shelf = new Facility { Id = 1, Name = "货架", RoomId = 1, Built = true };
        state.Territory.AddFacility(shelf);

        state.SettleDay(state.Clock.Season);
        Assert.Equal(0, shelf.Contents.Get("水"));
    }
}
