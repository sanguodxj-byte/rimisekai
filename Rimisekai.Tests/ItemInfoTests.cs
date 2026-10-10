using Rimisekai.Defs;
using Rimisekai.Housing;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>背包与仓储里的装备实例只记 Id：界面取名必须解析到实例名字，不得露出「eqp_1」这类内部 Id。</summary>
public class ItemInfoTests
{
    [Fact]
    public void Equip_instance_resolves_to_its_generated_name()
    {
        var t = new Territory();
        var helmet = new EquipInstance { Id = "eqp_1", Slot = EquipSlot.Head, Kind = EquipKind.Armor, Name = "铁帽子" };
        t.Equips.Add(helmet);

        var info = Items.Info(t, "eqp_1");

        Assert.NotNull(info);
        Assert.Equal("铁帽子", info!.Value.Label);
        Assert.False(info.Value.IsWeaponInstance);
    }

    [Fact]
    public void Unknown_id_resolves_to_nothing()
    {
        Assert.Null(Items.Info(new Territory(), "eqp_404"));
    }
}
