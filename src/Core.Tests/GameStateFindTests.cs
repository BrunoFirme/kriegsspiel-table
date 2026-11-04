using KTable.Core.Model;

namespace KTable.Core.Tests;

public class GameStateFindTests
{
    const string U = Layers.Umpire;

    static PieceInstance P(int n) => new() { Id = $"p-{n:D6}", Def = "d" };

    static void Same(GameState g, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            var walk = g.Layer(U).Where(p => p.Id == id).ToList();
            if (walk.Count > 1) Assert.Equal(id, g.Find(U, id)?.Id);
            else Assert.Same(walk.FirstOrDefault(), g.Find(U, id));
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(2026)]
    public void Answers_as_a_walk_would_whatever_is_done_to_the_layer(int seed)
    {
        var g = new GameState();
        var l = g.Layer(U);
        for (int i = 0; i < 400; i++) l.Add(P(i));
        int next = 400;
        var rng = new Random(seed);
        var asked = Enumerable.Range(0, 1200).Select(i => $"p-{i:D6}").ToList();
        for (int step = 0; step < 3000; step++)
        {
            switch (rng.Next(9))
            {
                case 0:
                    l.Add(P(next++));
                    break;
                case 1:
                    l.Insert(rng.Next(l.Count + 1), P(next++));
                    break;
                case 2:
                    if (l.Count > 0) l.RemoveAt(rng.Next(l.Count));
                    break;
                case 3:
                    if (l.Count > 0) l[rng.Next(l.Count)] = P(next++);
                    break;
                case 4: if (l.Count > 1) { int a = rng.Next(l.Count), b = rng.Next(l.Count); (l[a], l[b]) = (l[b], l[a]); } break;
                case 5:
                    if (l.Count > 0) l[rng.Next(l.Count)].Id = $"p-{next++:D6}";
                    break;
                case 6:
                    l.RemoveAll(p => rng.Next(50) == 0);
                    break;
                case 7:
                    l.Reverse();
                    break;
                case 8:
                    if (l.Count > 0) l.Insert(rng.Next(l.Count), l[rng.Next(l.Count)]);
                    break;
            }
            Same(g, Enumerable.Range(0, 5).Select(_ => asked[rng.Next(asked.Count)]));
            if (next > asked.Count - 10) asked.AddRange(Enumerable.Range(asked.Count, 400).Select(i => $"p-{i:D6}"));
        }
        Same(g, asked);
    }

    [Fact]
    public void A_layer_replaced_whole_is_read_afresh()
    {
        var g = new GameState();
        for (int i = 0; i < 200; i++) g.Layer(U).Add(P(i));
        Assert.NotNull(g.Find(U, "p-000150"));
        g.Layers[U] = Enumerable.Range(1000, 200).Select(P).ToList();
        Assert.Null(g.Find(U, "p-000150"));
        Assert.Same(g.Layer(U)[42], g.Find(U, "p-001042"));
        var copy = new GameState();
        copy.CopyFrom(g);
        Assert.Equal("p-001042", copy.Find(U, "p-001042")?.Id);
        Assert.NotSame(g.Find(U, "p-001042"), copy.Find(U, "p-001042"));
    }

    [Fact]
    public void What_it_remembers_is_never_saved()
    {
        var g = new GameState();
        for (int i = 0; i < 200; i++) g.Layer(U).Add(P(i));
        g.Find(U, "p-000100");
        var json = Json.Serialize(g);
        Assert.DoesNotContain("index", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, Json.Serialize(g.DeepClone()));
    }
}
