using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class GroundNamesTests
{
    [Fact]
    public void A_name_takes_its_hand_and_its_baseline_from_the_ground_under_the_click()
    {
        const int n = 80;
        var g = new ReliefGrid(n, n);
        byte grass = GroundKinds.Of("grass")!.Value, wood = GroundKinds.Of("wood")!.Value;
        Array.Fill(g.Kind, grass);
        for (int y = 10; y < 20; y++)
            for (int x = 10; x < 60; x++) g.Kind[y * n + x] = wood;
        for (int y = 30; y < 70; y++)
            for (int x = 38 + (y - 30) / 4; x < 41 + (y - 30) / 4; x++) g.Kind[y * n + x] = GroundKinds.Water;
        g.Kind[75 * n + 5] = GroundKinds.Town;
        g.Kind[75 * n + 70] = GroundKinds.None;
        var (kind, line) = GroundNames.Place(g, 30 / 79.0, 15 / 79.0, 10, 0.1);
        Assert.Equal(NameKind.Area, kind);
        Assert.Equal(2, line.Count);
        Assert.True(Math.Abs(line[0].v - line[1].v) < 0.02, "along the wood's long axis");
        Assert.InRange(Math.Abs(line[1].u - line[0].u) * 79, 25, 35);
        var (wk, wl) = GroundNames.Place(g, 44 / 79.0, 50 / 79.0, 5, 0.1);
        Assert.Equal(NameKind.Water, wk);
        Assert.True(wl.Count >= 5, "the baseline follows the river");
        Assert.All(wl, p => Assert.Equal(GroundKinds.Water, g.Kind[(int)Math.Round(p.v * 79) * n + (int)Math.Round(p.u * 79)]));
        Assert.True(Math.Abs(wl[^1].v - wl[0].v) > 0.1, "two name-lengths each way");
        Assert.Equal(NameKind.Place, GroundNames.Place(g, 5 / 79.0, 75 / 79.0, 5, 0.1).Kind);
        Assert.Equal(NameKind.Plain, GroundNames.Place(g, 70 / 79.0, 75 / 79.0, 5, 0.1).Kind);
        var (lake, pond) = GroundNames.Place(g, 2 / 79.0, 2 / 79.0, 3, 0.1);
        Assert.Equal(NameKind.Area, lake);
        Assert.True(Math.Abs(pond[1].u - pond[0].u) * 79 <= 3 * 3 * GroundNames.AreaLetter * GroundNames.Advance / 0.1 + 1, "a short name spreads only so far on a wide patch");
        Assert.Equal(5 * GroundNames.PlaceLetter * GroundNames.Advance, GroundNames.Length("Boggs", NameKind.Place));
    }
}
