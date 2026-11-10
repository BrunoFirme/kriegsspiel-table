using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class FormerKindsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-former-kinds-conv", Guid.NewGuid().ToString("N"));

    public void Dispose() => BestEffort.DeleteFolder(_root);

    static PieceDef Kind(string id, string name, string? image = null, string? mesh = null) => new() { Id = id, Name = name, Image = image, Mesh = mesh };

    static PackManifest Pack(params PieceDef[] defs) => new() { Id = "tts.balkan", Name = "Balkan", Pieces = defs.ToList() };

    static FormerKinds.Carried Into(PackManifest fresh, PackManifest former) => FormerKinds.Into(fresh, former, _ => true);

    static PieceDef Of(PackManifest p, string id) => p.Pieces.Single(d => d.Id == id);

    [Fact]
    public void A_kind_whose_look_changed_takes_over_the_old_id_by_its_name()
    {
        var fresh = Pack(Kind("b.infantry-2222", "Infantry", "new"), Kind("b.gun-3333", "Gun", "gun"));
        var former = Pack(Kind("b.infantry-1111", "Infantry", "old"), Kind("b.gun-3333", "Gun", "gun"));

        var carried = Into(fresh, former);

        Assert.Equal((1, 0), (carried.Updated, carried.Kept));
        Assert.Equal(new[] { "b.infantry-1111" }, Of(fresh, "b.infantry-2222").Formerly);
        Assert.Null(Of(fresh, "b.gun-3333").Formerly);
        Assert.Equal(2, fresh.Pieces.Count);
    }

    [Fact]
    public void Kinds_sharing_a_name_pair_by_the_picture_or_model_they_kept()
    {
        var fresh = Pack(Kind("b.bn-aa", "Battalion", "red", "line"), Kind("b.bn-bb", "Battalion", "blue2", "column"));
        var former = Pack(Kind("b.bn-11", "Battalion", "red", "line-old"), Kind("b.bn-22", "Battalion", "blue", "column"));

        var carried = Into(fresh, former);

        Assert.Equal((2, 0), (carried.Updated, carried.Kept));
        Assert.Equal(new[] { "b.bn-22" }, Of(fresh, "b.bn-bb").Formerly);
        Assert.Equal(new[] { "b.bn-11" }, Of(fresh, "b.bn-aa").Formerly);
    }

    [Fact]
    public void Kinds_sharing_a_name_with_nothing_to_tell_them_apart_are_kept_rather_than_guessed()
    {
        var fresh = Pack(Kind("b.bn-aa", "Battalion", "x"), Kind("b.bn-bb", "Battalion", "y"));
        var former = Pack(Kind("b.bn-11", "Battalion", "p"), Kind("b.bn-22", "Battalion", "q"));

        var carried = Into(fresh, former);

        Assert.Equal((0, 2), (carried.Updated, carried.Kept));
        Assert.True(Of(fresh, "b.bn-11").Retired && Of(fresh, "b.bn-22").Retired);
        Assert.False(Of(fresh, "b.bn-aa").Retired);
    }

    [Fact]
    public void A_kind_gone_from_the_module_stays_retired_with_its_pictures_and_points_at_the_kinds_that_replaced_its_markers()
    {
        var fresh = Pack(Kind("b.flag-new", "Flag", "flag2"));
        fresh.Assets["flag2"] = new AssetEntry { File = "assets/flag2.png" };
        var gone = Kind("b.redoubt-1", "Redoubt", "redoubt");
        gone.Attachments.Add(new AttachmentDef { Piece = "b.flag-old" });
        gone.Attachments.Add(new AttachmentDef { Piece = "b.stake-1" });
        var former = Pack(gone, Kind("b.flag-old", "Flag", "flag1"), Kind("b.stake-1", "Stake", "stake"));
        former.Assets["redoubt"] = new AssetEntry { File = "assets/redoubt.png" };
        former.Assets["stake"] = new AssetEntry { File = "assets/stake.png" };
        former.Assets["flag1"] = new AssetEntry { File = "assets/flag1.png" };
        var copied = new List<string>();

        var carried = FormerKinds.Into(fresh, former, a => { copied.Add(a.File); return true; });

        Assert.Equal((1, 2), (carried.Updated, carried.Kept));
        var redoubt = Of(fresh, "b.redoubt-1");
        Assert.True(redoubt.Retired);
        Assert.Equal(new[] { "b.flag-new", "b.stake-1" }, redoubt.Attachments.Select(a => a.Piece));
        Assert.True(Of(fresh, "b.stake-1").Retired);
        Assert.Equal(new[] { "assets/redoubt.png", "assets/stake.png" }, copied.Order());
        Assert.True(fresh.Assets.ContainsKey("redoubt") && !fresh.Assets.ContainsKey("flag1"));
    }

    [Fact]
    public void A_picture_that_could_not_be_brought_over_is_not_listed()
    {
        var fresh = Pack();
        var former = Pack(Kind("b.redoubt-1", "Redoubt", "redoubt"));
        former.Assets["redoubt"] = new AssetEntry { File = "assets/redoubt.png" };

        FormerKinds.Into(fresh, former, _ => false);

        Assert.True(Of(fresh, "b.redoubt-1").Retired);
        Assert.Empty(fresh.Assets);
    }

    [Fact]
    public void Former_ids_carry_through_import_after_import_and_retired_kinds_stay_retired()
    {
        var first = Pack(Kind("b.inf-1", "Infantry", "a"), Kind("b.old-1", "Old", "o"));
        var second = Pack(Kind("b.inf-2", "Infantry", "b"));
        Into(second, first);
        var third = Pack(Kind("b.inf-3", "Infantry", "c"), Kind("b.old-new", "Old", "n"));

        var carried = Into(third, second);

        Assert.Equal(new[] { "b.inf-2", "b.inf-1" }, Of(third, "b.inf-3").Formerly);
        Assert.True(Of(third, "b.old-1").Retired);
        Assert.Null(Of(third, "b.old-new").Formerly);
        Assert.Equal((1, 1), (carried.Updated, carried.Kept));
    }

    [Fact]
    public void An_unchanged_kind_keeps_the_former_ids_it_already_answered_for()
    {
        var former = Pack(Kind("b.inf-2", "Infantry", "b"));
        former.Pieces[0].Formerly = new() { "b.inf-1" };
        var fresh = Pack(Kind("b.inf-2", "Infantry", "b"));

        var carried = Into(fresh, former);

        Assert.Equal((0, 0), (carried.Updated, carried.Kept));
        Assert.Equal(new[] { "b.inf-1" }, Of(fresh, "b.inf-2").Formerly);
    }

    [Fact]
    public void Carry_writes_the_pack_and_copies_the_pictures_of_kept_kinds_from_the_trashed_pack()
    {
        var packDir = Path.Combine(_root, "tts.balkan");
        var formerDir = Path.Combine(_root, "trash", "pack");
        Directory.CreateDirectory(Path.Combine(formerDir, "assets"));
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(formerDir, "assets", "redoubt.png"), "picture");
        var former = Pack(Kind("b.redoubt-1", "Redoubt", "redoubt"), Kind("b.inf-1", "Infantry", "a"));
        former.Assets["redoubt"] = new AssetEntry { File = "assets/redoubt.png" };
        Json.Save(Path.Combine(formerDir, "pack.json"), former);
        Json.Save(Path.Combine(packDir, "pack.json"), Pack(Kind("b.inf-2", "Infantry", "b")));
        var rep = new Report();

        var carried = FormerKinds.Carry(packDir, formerDir, rep);

        Assert.Equal((1, 1), (carried.Updated, carried.Kept));
        Assert.Equal("picture", File.ReadAllText(Path.Combine(packDir, "assets", "redoubt.png")));
        var saved = Json.Load<PackManifest>(Path.Combine(packDir, "pack.json"));
        Assert.Equal(new[] { "b.inf-1" }, Of(saved, "b.inf-2").Formerly);
        Assert.True(Of(saved, "b.redoubt-1").Retired);
        Assert.Equal(new FormerKinds.Carried(0, 0), FormerKinds.Carry(packDir, null, rep));
    }
}
