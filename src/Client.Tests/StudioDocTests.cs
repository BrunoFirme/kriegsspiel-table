using System.Numerics;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class StudioDocTests
{
    static ArtDoc Doc(params string[] names)
    {
        var d = new ArtDoc();
        foreach (var n in names) d.Layers.Add(new ArtLayer { Id = n, Name = n });
        return d;
    }

    static string Names(ArtDoc d) => string.Join(",", d.Layers.Select(l => l.Name));

    [Fact]
    public void A_new_layers_name_is_the_first_of_its_stem_not_taken()
    {
        Assert.Equal("Paint", Doc().NextName("Paint"));
        Assert.Equal("Paint 2", Doc("Paint").NextName("Paint"));
        Assert.Equal("Paint 3", Doc("Paint", "Paint 2", "Paint 4").NextName("Paint"));
        Assert.Equal("Paint", Doc("Paint 2").NextName("Paint"));
        Assert.Equal("Text", Doc("Paint").NextName("Text"));
    }

    [Fact]
    public void A_layer_goes_in_over_the_selected_one_else_on_top_and_is_selected()
    {
        var d = Doc("a", "b", "c");
        d.Insert(new ArtLayer { Id = "x", Name = "x" });
        Assert.Equal("a,b,c,x", Names(d));
        Assert.Equal("x", d.Selected);
        d.Selected = "a";
        d.Insert(new ArtLayer { Id = "y", Name = "y" });
        Assert.Equal("a,y,b,c,x", Names(d));
        d.Insert(new ArtLayer { Id = "z", Name = "z" }, 0);
        Assert.Equal("z,a,y,b,c,x", Names(d));
        d.Insert(new ArtLayer { Id = "w", Name = "w" }, 99);
        Assert.Equal("z,a,y,b,c,x,w", Names(d));
        d.Selected = "gone";
        Assert.Null(d.Sel);
        d.Insert(new ArtLayer { Id = "v", Name = "v" });
        Assert.Equal("z,a,y,b,c,x,w,v", Names(d));
        Assert.Same(d.Layers[^1], d.Sel);
    }

    [Fact]
    public void Layers_move_one_at_a_time_and_not_past_the_ends()
    {
        var d = Doc("a", "b", "c");
        Assert.True(d.Swap(d.Layers[0], 1));
        Assert.Equal("b,a,c", Names(d));
        Assert.False(d.Swap(d.Layers[2], 1));
        Assert.False(d.Swap(d.Layers[0], -1));
        Assert.True(d.Swap(d.Layers[2], -1));
        Assert.Equal("b,c,a", Names(d));
    }

    [Fact]
    public void After_a_delete_the_layer_under_it_is_selected()
    {
        var d = Doc("a", "b", "c");
        Assert.Equal("a", d.Remove(d.Layers[1]));
        Assert.Equal("c", d.Remove(d.Layers[0]));
        Assert.Null(d.Remove(d.Layers[0]));
        Assert.Empty(d.Layers);
    }

    [Fact]
    public void A_copied_layer_comes_in_with_an_id_and_a_name_of_its_own_set_off_a_little()
    {
        var d = Doc("Pasted", "Pasted 2");
        var l = d.TakeIn(new ArtLayer { Id = "old", Name = "Pasted", X = 1, Y = 2 }, 12);
        Assert.NotEqual("old", l.Id);
        Assert.Equal(10, l.Id.Length);
        Assert.Equal("Pasted 3", l.Name);
        Assert.Equal((13f, 14f), (l.X, l.Y));
        var m = Doc("a", "b", "c");
        m.ReplacePair(2, new ArtLayer { Name = "bc" });
        Assert.Equal("a,bc", Names(m));
    }

    [Fact]
    public void A_document_is_written_compactly_without_what_is_unset()
    {
        var d = new ArtDoc { Selected = "l1" };
        d.Layers.Add(new ArtLayer { Id = "l1", Ops = { new PaintOp { P = PaintOp.Flat(new[] { new Vector2(1.234f, -5.678f), new Vector2(10, 0.005f) }) } } });
        var json = ArtJson.Serialize(d);
        Assert.Equal(
            "{\"Version\":1,\"W\":512,\"H\":512,\"Layers\":[{\"Id\":\"l1\",\"Name\":\"Layer\",\"Kind\":\"paint\",\"Visible\":true,\"Locked\":false,\"Opacity\":1,"
            + "\"X\":0,\"Y\":0,\"Rot\":0,\"Sx\":1,\"Sy\":1,\"SrcW\":0,\"SrcH\":0,\"Font\":\"Segoe UI\",\"Bold\":true,\"Italic\":false,\"Size\":48,"
            + "\"Color\":\"#ffffffff\",\"OutlineW\":0,\"Ops\":[{\"K\":\"path\",\"P\":[1.23,-5.68,10,0],\"C\":\"#000000ff\",\"W\":8,\"E\":false}]}],"
            + "\"Measures\":[],\"Selected\":\"l1\"}", json);
        var back = ArtJson.Deserialize(json)!;
        Assert.Equal("l1", back.Sel!.Id);
        Assert.Equal(json, ArtJson.Serialize(back));
        Assert.Null(ArtJson.Deserialize("{ torn"));
        var c = back.Layers[0].Ops[0].Clone();
        Assert.NotSame(back.Layers[0].Ops[0].P, c.P);
        Assert.Equal(back.Layers[0].Ops[0].P, c.P);
    }

    [Fact]
    public void A_merge_lays_the_upper_picture_over_the_lower_at_their_opacities()
    {
        var lower = new byte[] { 0, 0, 0, 0, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 0, 0 };
        var upper = new byte[] { 0, 0, 0, 0, 255, 0, 0, 255, 255, 0, 0, 128, 255, 0, 0, 255 };
        Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 0, 0, 255, 128, 0, 126, 255, 255, 0, 0, 255 }, LayerPixels.Over(lower, 1, upper, 1f));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 127, 0, 127, 255, 64, 0, 191, 255, 255, 0, 0, 127 }, LayerPixels.Over(lower, 1, upper, 0.5f));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 0, 0 }, LayerPixels.Over(lower, 1, upper, 0));
    }

    [Fact]
    public void Removing_the_background_clears_the_corners_colour_from_the_edges_in_and_softens_what_it_touched()
    {
        const int w = 4, h = 4;
        var d = new byte[w * h * 4];
        for (int k = 0; k < w * h; k++) { d[k * 4] = d[k * 4 + 1] = d[k * 4 + 2] = 240; d[k * 4 + 3] = 255; }
        void Set(int x, int y, byte v) { int k = (y * w + x) * 4; d[k] = d[k + 1] = d[k + 2] = v; }
        Set(1, 1, 20);
        Set(2, 1, 20);
        Set(1, 2, 20);
        Set(2, 2, 20);
        Set(3, 1, 225);
        Set(0, 2, 30);
        int n = LayerPixels.KeyOut(d, w, h, 0.01f);
        Assert.Equal(11, n);
        string A() => string.Join(",", Enumerable.Range(0, w * h).Select(k => d[k * 4 + 3]));
        Assert.Equal("0,0,0,0,0,127,127,0,127,127,127,0,0,0,0,0", A());

        var flat = Enumerable.Repeat((byte)90, 3 * 2 * 4).ToArray();
        Assert.Equal(6, LayerPixels.KeyOut(flat, 3, 2, 0.12f));
    }

    [Fact]
    public void A_waiting_save_is_due_after_the_changes_stop_or_at_the_latest_a_while_after_the_first()
    {
        var s = new Debounce();
        Assert.False(s.Pending);
        Assert.False(s.Tick(10));
        s.Arm(0.5, 3);
        Assert.True(s.Pending);
        Assert.False(s.Tick(0.4));
        Assert.True(s.Tick(0.2));
        s.Clear();
        Assert.False(s.Pending);

        s.Arm(0.5, 3);
        double t = 0;
        bool due = false;
        while (!due)
        {
            s.Arm(0.5, 3);
            due = s.Tick(0.25);
            t += 0.25;
        }
        Assert.Equal(3, t);
        s.Clear();
        s.In = 3;
        Assert.True(s.Pending);
        Assert.True(s.Tick(0.1));
    }
}
