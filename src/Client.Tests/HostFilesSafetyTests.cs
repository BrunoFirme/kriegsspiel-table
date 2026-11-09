using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class HostFilesSafetyTests
{
    readonly string _root = Path.Combine(TestFolders.New(), "cache", "packages");
    readonly ClientSession _session;
    readonly List<(long to, Message m)> _sent = new();
    readonly List<string> _missing = new(), _warnings = new();
    GameServer? _server;
    readonly AssetTransfers _t;

    public HostFilesSafetyTests()
    {
        Directory.CreateDirectory(_root);
        _session = new ClientSession(new PackageStore(_root));
        _t = new AssetTransfers(() => _session, () => _server)
        {
            Send = (to, m) => _sent.Add((to, m)),
            FileMissing = (p, r) => _missing.Add(p + "/" + r),
            Log = new ClientLog { Warning = _warnings.Add },
        };
    }

    List<T> Sent<T>() where T : Message => _sent.Select(s => s.m).OfType<T>().ToList();

    static string HashName(byte[] data) => "assets/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant() + ".png";

    string Above => Path.GetFullPath(Path.Combine(_root, ".."));

    [Theory]
    [InlineData("user.x", "../../escaped.cmd")]
    [InlineData("user.x", "..\\..\\escaped.cmd")]
    [InlineData("..", "escaped.cmd")]
    [InlineData("../..", "escaped.cmd")]
    [InlineData("user.x", "assets/../../../escaped.cmd")]
    [InlineData("user.x", "C:escaped.cmd")]
    [InlineData("user.x", "")]
    public void A_file_the_host_names_outside_this_pc_s_game_files_is_never_asked_for_or_written(string package, string rel)
    {
        _t.QueueFetch(package, rel);
        Assert.Empty(Sent<NeedAsset>());
        Assert.Equal(new[] { package + "/" + rel }, _missing);
        Assert.Single(_warnings);
        _t.ReceiveChunk(new AssetChunk { Package = package, Path = rel, Offset = 0, Total = 4, Data = "echo"u8.ToArray() });
        Assert.Empty(Directory.EnumerateFiles(Above, "escaped*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_file_named_by_a_full_path_is_never_asked_for()
    {
        var target = Path.Combine(Above, "escaped.cmd");
        _t.QueueFetch("user.x", target);
        Assert.Empty(Sent<NeedAsset>());
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void A_file_inside_a_pack_s_folder_is_still_asked_for()
    {
        _t.QueueFetch("tts.Some_Module", "tiles\\map\\index.json");
        Assert.Equal("tiles\\map\\index.json", Assert.Single(Sent<NeedAsset>()).Path);
        Assert.Empty(_missing);
    }

    GameServer Host(Role role, string name = "Eve")
    {
        _server = new GameServer(new GameState(), _session.Store);
        _server.Join(1, new JoinRequest { Name = "Host" });
        _server.Join(2, new JoinRequest { Name = name, Protocol = ProtocolVersion.Current });
        _server.Control(1, new Admit { PeerId = 2, Role = role });
        Assert.Equal(role, _server.Peer(2)!.Role);
        return _server;
    }

    string HostPack(params string[] authors)
    {
        var dir = Path.Combine(_root, "user.mine");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pack.json");
        Json.Save(path, new PackManifest { Id = "user.mine", Name = "the host's own", Source = new PackSource { Authors = authors.ToList() } });
        return path;
    }

    void Upload(PackManifest pack)
    {
        var bytes = Json.SerializeBytes(pack);
        _t.ReceiveUpload(2, new UploadAsset { Package = pack.Id, Path = "pack.json", Offset = 0, Total = bytes.Length, Data = bytes });
    }

    [Fact]
    public void An_observer_s_upload_is_dropped()
    {
        Host(Role.Observer);
        var path = HostPack();
        Upload(new PackManifest { Id = "user.mine", Name = "replaced" });
        byte[] data = [7, 8, 9];
        _t.ReceiveUpload(2, new UploadAsset { Package = "user.other", Path = HashName(data), Offset = 0, Total = 3, Data = data });
        Assert.Equal("the host's own", Json.Load<PackManifest>(path).Name);
        Assert.False(Directory.Exists(Path.Combine(_root, "user.other")));
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_host_pack_that_names_no_author_is_kept_against_an_umpire_s_upload()
    {
        Host(Role.Umpire);
        var path = HostPack();
        Upload(new PackManifest { Id = "user.mine", Name = "replaced" });
        Assert.Equal("the host's own", Json.Load<PackManifest>(path).Name);
        Assert.Equal("upload of user.mine/pack.json refused: user.mine is already on this host and names no author, so it stays as it is: the host can remove it from this PC in Piece studio, then you send it again",
            Assert.Single(Sent<ErrorMessage>()).Text);
    }

    [Fact]
    public void An_umpire_who_sent_a_pack_with_no_author_sends_new_versions_of_it_at_the_same_sitting()
    {
        Host(Role.Umpire);
        Upload(new PackManifest { Id = "user.mine", Name = "first" });
        Upload(new PackManifest { Id = "user.mine", Name = "second" });
        Upload(new PackManifest { Id = "user.mine", Name = "third", Source = new PackSource { Authors = ["Eve"] } });
        Assert.Equal("third", Json.Load<PackManifest>(Path.Combine(_root, "user.mine", "pack.json")).Name);
        Assert.Empty(Sent<ErrorMessage>());
    }

    [Fact]
    public void Sending_back_the_host_s_own_piece_list_unchanged_gives_no_right_to_change_it()
    {
        Host(Role.Umpire);
        var path = HostPack();
        var bytes = File.ReadAllBytes(path);
        _t.ReceiveUpload(2, new UploadAsset { Package = "user.mine", Path = "pack.json", Offset = 0, Total = bytes.Length, Data = bytes });
        Upload(new PackManifest { Id = "user.mine", Name = "replaced" });
        Assert.Equal("the host's own", Json.Load<PackManifest>(path).Name);
        Assert.Contains("names no author", Assert.Single(Sent<ErrorMessage>()).Text);
    }

    [Fact]
    public void A_new_sitting_forgets_who_sent_which_pack()
    {
        Host(Role.Umpire);
        Upload(new PackManifest { Id = "user.mine", Name = "first" });
        _t.Reset();
        Upload(new PackManifest { Id = "user.mine", Name = "second" });
        Assert.Equal("first", Json.Load<PackManifest>(Path.Combine(_root, "user.mine", "pack.json")).Name);
        Assert.Single(Sent<ErrorMessage>());
    }

    [Fact]
    public void A_host_pack_by_someone_else_is_kept_against_an_umpire_s_upload()
    {
        Host(Role.Umpire);
        var path = HostPack("Host");
        Upload(new PackManifest { Id = "user.mine", Name = "replaced" });
        Assert.Equal("the host's own", Json.Load<PackManifest>(path).Name);
        Assert.Equal("upload of user.mine/pack.json refused: user.mine belongs to Host: join as Host to send a new version, or the host can remove it from this PC in Piece studio first",
            Assert.Single(Sent<ErrorMessage>()).Text);
    }

    [Fact]
    public void An_umpire_brings_a_new_version_of_their_own_pack()
    {
        Host(Role.Umpire, "Eve");
        var path = HostPack("Eve");
        Upload(new PackManifest { Id = "user.mine", Name = "newer", Source = new PackSource { Authors = ["Eve"] } });
        Assert.Equal("newer", Json.Load<PackManifest>(path).Name);
        Assert.Empty(Sent<ErrorMessage>());
    }

    [Fact]
    public void The_same_piece_list_again_is_taken_whoever_wrote_it()
    {
        Host(Role.Umpire);
        var path = HostPack();
        var bytes = File.ReadAllBytes(path);
        _t.ReceiveUpload(2, new UploadAsset { Package = "user.mine", Path = "pack.json", Offset = 0, Total = bytes.Length, Data = bytes });
        Assert.Empty(Sent<ErrorMessage>());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void An_umpire_sends_no_more_than_the_limit_at_one_sitting()
    {
        Host(Role.Umpire);
        long most = UploadAsset.MaxFileBytes;
        int started = 0;
        for (int i = 0; i < AssetTransfers.MaxUploadPerPeer / most + 1; i++)
        {
            var path = "assets/" + i.ToString("x64") + ".png";
            _t.ReceiveUpload(2, new UploadAsset { Package = "user.p" + i, Path = path, Offset = 0, Total = most, Data = [1] });
            if (Sent<ErrorMessage>().Count == 0) started++;
        }
        Assert.Equal(AssetTransfers.MaxUploadPerPeer / most, started);
        Assert.Contains("at one sitting", Assert.Single(Sent<ErrorMessage>()).Text);
        _t.Reset();
    }
}
