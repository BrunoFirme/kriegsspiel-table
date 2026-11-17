using System.Text;
using KTable.Core;

namespace KTable.Client.Tests;

public sealed class PictureMentionsTests
{
    readonly string _dir = TestFolders.New();
    static readonly string Hash = new string('a', 60) + "0F9c";

    string Save(string name, byte[] bytes)
    {
        var file = Path.Combine(_dir, name);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    static byte[] Mentioning(string packId) => Encoding.UTF8.GetBytes($"{{\"packs\":[\"{packId}\"],\"face\":\"{Hash}.png\"}}");

    [Fact]
    public void A_picture_named_in_a_packed_save_is_found_as_in_a_plain_one()
    {
        var plain = new HashSet<string>();
        PictureMentions.InFile(Save("plain.kstable", Mentioning("user.blocks")), "user.blocks", plain);
        var packed = new HashSet<string>();
        PictureMentions.InFile(Save("packed.kstable", Json.Packed(Mentioning("user.blocks"))), "user.blocks", packed);
        Assert.Equal(new[] { Hash.ToLowerInvariant() }, plain);
        Assert.Equal(plain, packed);
    }

    [Fact]
    public void A_save_of_another_pack_or_one_that_is_gone_names_no_picture()
    {
        var found = new HashSet<string>();
        PictureMentions.InFile(Save("other.kstable", Json.Packed(Mentioning("user.flags"))), "user.blocks", found);
        PictureMentions.InFile(Path.Combine(_dir, "gone.kstable"), "user.blocks", found);
        Assert.Empty(found);
    }
}
