using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class ModelGroupsTests
{
    [Fact]
    public void Material_runs_are_kept_short_and_come_back_the_same()
    {
        var mats = new[] { 0, 0, 0, 1, 1, 0, 2 };
        var kept = ModelGroups.Encode(mats, mats.Length);
        Assert.Equal("0*3,1*2,0*1,2*1", kept);
        Assert.Equal(mats, ModelGroups.Decode(kept!, mats.Length));
    }

    [Fact]
    public void One_material_needs_nothing_kept_and_a_wrong_count_reads_as_nothing()
    {
        Assert.Null(ModelGroups.Encode(new[] { 4, 4, 4 }, 3));
        Assert.Null(ModelGroups.Encode(new[] { 0, 1 }, 3));
        Assert.Null(ModelGroups.Decode("0*2,1*2", 5));
        Assert.Null(ModelGroups.Decode("0*2,1*9", 5));
        Assert.Null(ModelGroups.Decode("x*2", 2));
    }
}
