using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class AssetTransfersTests
{
    const int Chunk = AssetTransfers.ChunkSize;

    readonly string _root = TestFolders.New();
    readonly string _installed = TestFolders.New();
    readonly ClientSession _session;
    readonly List<(long to, Message m)> _sent = new();
    readonly List<string> _arrived = new(), _missing = new(), _warnings = new();
    GameServer? _server;
    double _now;
    readonly AssetTransfers _t;

    public AssetTransfersTests()
    {
        _session = new ClientSession(new PackageStore(_root));
        _t = new AssetTransfers(() => _session, () => _server)
        {
            Send = (to, m) => _sent.Add((to, m)),
            FileArrived = (p, r) => _arrived.Add(p + "/" + r),
            FileMissing = (p, r) => _missing.Add(p + "/" + r),
            Clock = () => _now,
            Log = new ClientLog { Warning = _warnings.Add },
        };
    }

    List<T> Sent<T>() where T : Message => _sent.Select(s => s.m).OfType<T>().ToList();

    static string Hash(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    static string HashName(byte[] data) => "assets/" + Hash(data) + ".png";

    string Here(string package, string rel) => Path.Combine(_root, package, rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Four_urgent_files_and_two_background_ones_are_asked_for_at_a_time()
    {
        foreach (var n in "abcdef") _t.QueueFetch("p", n + ".png");
        foreach (var n in "xyz") _t.Prefetch("p", n + ".png");
        var asks = Sent<NeedAsset>();
        Assert.Equal(new[] { "a.png", "b.png", "c.png", "d.png" }, asks.Where(a => a.Urgent).Select(a => a.Path));
        Assert.Equal(new[] { "x.png", "y.png" }, asks.Where(a => !a.Urgent).Select(a => a.Path));
        Assert.All(asks, a => Assert.Equal(0, a.Offset));
    }

    [Fact]
    public void A_fetched_file_lands_in_place_every_piece_is_acknowledged_and_the_next_file_is_asked_for()
    {
        foreach (var n in "abcde") _t.QueueFetch("p", n + ".bin");
        _sent.Clear();
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = "a.bin", Offset = 0, Total = 3, Data = [1, 2, 3] });
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Here("p", "a.bin")));
        Assert.False(File.Exists(Here("p", "a.bin.part")));
        Assert.Equal(new[] { "p/a.bin" }, _arrived);
        Assert.Equal(0, Assert.Single(Sent<ChunkAck>()).Offset);
        Assert.Equal("e.bin", Assert.Single(Sent<NeedAsset>()).Path);
    }

    [Fact]
    public void A_background_file_wanted_now_is_hurried_whether_asked_for_or_still_waiting()
    {
        foreach (var n in "xyz") _t.Prefetch("p", n + ".png");
        _sent.Clear();
        _t.QueueFetch("p", "x.png");
        _t.QueueFetch("p", "z.png");
        Assert.Equal(new[] { ("x.png", true), ("z.png", true) }, Sent<NeedAsset>().Select(a => (a.Path, a.Urgent)));
        _t.QueueFetch("p", "x.png");
        Assert.Equal(2, Sent<NeedAsset>().Count);
    }

    [Fact]
    public void A_file_the_host_lacks_is_given_up_with_a_warning()
    {
        _t.QueueFetch("p", "gone.png");
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = "gone.png", Total = -1 });
        Assert.Equal("host has no p/gone.png", Assert.Single(_warnings));
        Assert.Empty(_arrived);
        Assert.Equal(new[] { "p/gone.png" }, _missing);
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = "gone.png", Total = 1, Data = new byte[1] });
        Assert.Empty(_arrived);
    }

    [Fact]
    public void A_damaged_file_is_thrown_away_and_fetched_once_more_then_given_up()
    {
        var name = HashName([1, 2, 3]);
        _t.QueueFetch("p", name);
        _sent.Clear();
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = name, Total = 1, Data = [7] });
        Assert.Equal(name, Assert.Single(Sent<NeedAsset>()).Path);
        Assert.False(File.Exists(Here("p", name)));
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = name, Total = 1, Data = [7] });
        Assert.Equal($"p/{name} arrived damaged 2 times", Assert.Single(_warnings));
        Assert.Equal(new[] { "p/" + name }, _missing);
        Assert.False(File.Exists(Here("p", name)));
        Assert.False(File.Exists(Here("p", name) + ".part"));
    }

    [Fact]
    public void A_damaged_file_that_comes_right_the_second_time_is_kept()
    {
        byte[] data = [1, 2, 3];
        var name = HashName(data);
        _t.QueueFetch("p", name);
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = name, Total = 3, Data = [9, 9, 9] });
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = name, Total = 3, Data = data });
        Assert.Equal(new[] { "p/" + name }, _arrived);
        Assert.Equal(data, File.ReadAllBytes(Here("p", name)));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Leaving_the_table_drops_every_half_fetched_file()
    {
        _t.QueueFetch("p", "big.bin");
        _t.ReceiveChunk(new AssetChunk { Package = "p", Path = "big.bin", Total = Chunk * 2, Data = new byte[Chunk] });
        Assert.True(File.Exists(Here("p", "big.bin.part")));
        _t.Reset();
        Assert.False(File.Exists(Here("p", "big.bin.part")));
        Assert.Equal((0, 0), (_t.Asked, _t.Waiting));
    }

    void Settle(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until) { _t.Pump(); Thread.Sleep(5); }
        _t.Pump();
    }

    void Install(string package, string rel, byte[] data)
    {
        var path = Path.Combine(_installed, package, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    [Fact]
    public void A_file_this_pc_already_has_in_an_installed_pack_is_copied_instead_of_downloaded()
    {
        byte[] data = [4, 5, 6];
        var name = HashName(data);
        Install("p", name, data);
        _t.Local = new LocalCopies(_installed, _root);
        _t.QueueFetch("p", name);
        Settle(() => _arrived.Count > 0);
        Assert.Equal(new[] { "p/" + name }, _arrived);
        Assert.Empty(Sent<NeedAsset>());
        Assert.Equal(data, File.ReadAllBytes(Here("p", name)));
    }

    [Fact]
    public void A_file_with_the_same_name_in_another_installed_pack_is_found_once_the_names_are_read()
    {
        byte[] data = [8, 9];
        var name = HashName(data);
        Install("other.pack", name, data);
        var local = new LocalCopies(_installed, _root);
        local.Warm();
        Settle(() => local.Candidate("p", name) != null);
        _t.Local = local;
        _t.QueueFetch("p", name);
        Settle(() => _arrived.Count > 0);
        Assert.Empty(Sent<NeedAsset>());
        Assert.Equal(data, File.ReadAllBytes(Here("p", name)));
    }

    [Fact]
    public void An_installed_copy_that_does_not_match_its_name_is_downloaded_instead()
    {
        var name = HashName([1]);
        Install("p", name, [2]);
        _t.Local = new LocalCopies(_installed, _root);
        _t.QueueFetch("p", name);
        Settle(() => Sent<NeedAsset>().Count > 0);
        Assert.Equal(name, Assert.Single(Sent<NeedAsset>()).Path);
        Assert.Empty(_arrived);
    }

    [Fact]
    public void Map_tiles_are_copied_only_when_this_pc_s_map_has_the_same_tile_list()
    {
        var image = new string('c', 64);
        Install("sheets", $"tiles/{image}/index.json", [1]);
        Install("sheets", $"tiles/{image}/0/0_0.webp", [5]);
        var local = new LocalCopies(_installed, _root);
        Assert.Null(local.Candidate("scenarios/s", $"tiles/{image}/0/0_0.webp"));
        Directory.CreateDirectory(Here("scenarios/s", $"tiles/{image}"));
        File.WriteAllBytes(Here("scenarios/s", $"tiles/{image}/index.json"), [2]);
        Assert.Null(new LocalCopies(_installed, _root).Candidate("scenarios/s", $"tiles/{image}/0/0_0.webp"));
        File.WriteAllBytes(Here("scenarios/s", $"tiles/{image}/index.json"), [1]);
        local = new LocalCopies(_installed, _root);
        Assert.NotNull(local.Candidate("scenarios/s", $"tiles/{image}/0/0_0.webp"));
        Assert.Null(local.Candidate("scenarios/s", $"tiles/{image}/index.json"));
        Assert.Null(local.Candidate("scenarios/s", $"tiles/{image}/0/9_9.webp"));
    }

    [Fact]
    public void A_pack_s_piece_list_always_comes_from_the_host()
    {
        Install("p", "pack.json", [1]);
        Assert.Null(new LocalCopies(_installed, _root).Candidate("p", "pack.json"));
    }

    GameServer Host(params long[] players)
    {
        _server = new GameServer(new GameState(), _session.Store);
        _server.Join(1, new JoinRequest { Name = "Host" });
        foreach (var p in players)
        {
            _server.Join(p, new JoinRequest { Name = "P" + p, Protocol = ProtocolVersion.Current });
            _server.Control(1, new Admit { PeerId = p, Role = Role.Observer });
        }
        return _server;
    }

    string Shared(string package, string rel, int bytes)
    {
        var path = Here(package, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        if (!_server!.State.Packs.Contains(package)) _server.State.Packs.Add(package);
        return rel;
    }

    List<AssetChunk> ChunksTo(long peer) => _sent.Where(s => s.to == peer).Select(s => s.m).OfType<AssetChunk>().ToList();

    [Fact]
    public void The_host_sends_only_to_someone_let_in_and_only_from_the_game_s_packs()
    {
        var host = Host(2);
        Directory.CreateDirectory(Here("pack.a", ""));
        File.WriteAllBytes(Here("pack.a", "f.bin"), [1, 2]);
        _t.Serve(9, new NeedAsset { Package = "pack.a", Path = "f.bin" });
        Assert.Equal(-1, Sent<AssetChunk>()[^1].Total);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = "f.bin" });
        Assert.Equal(-1, Sent<AssetChunk>()[^1].Total);
        host.State.Packs.Add("pack.a");
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = "f.bin" });
        var c = Sent<AssetChunk>()[^1];
        Assert.Equal(2, c.Total);
        Assert.Equal(new byte[] { 1, 2 }, c.Data);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = "../pack.a/f.bin" });
        Assert.Equal(2, Sent<AssetChunk>()[^1].Total);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = "../../elsewhere.bin" });
        Assert.Equal(-1, Sent<AssetChunk>()[^1].Total);
    }

    [Fact]
    public void The_host_keeps_only_a_window_of_pieces_on_the_way_and_each_ack_lets_one_more_go()
    {
        Host(2);
        var rel = Shared("pack.a", "big.bin", Chunk * 10);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = rel });
        Assert.Equal(new long[] { 0, Chunk }, ChunksTo(2).Select(c => c.Offset));
        _t.OnAck(2, new ChunkAck { Package = "pack.a", Path = rel, Offset = 0 });
        Assert.Equal(new long[] { 0, Chunk, Chunk * 2 }, ChunksTo(2).Select(c => c.Offset));
        _t.OnAck(2, new ChunkAck { Package = "pack.a", Path = rel, Offset = 0 });
        Assert.Equal(3, ChunksTo(2).Count);
    }

    [Fact]
    public void An_urgent_file_goes_ahead_of_a_background_one_already_on_its_way()
    {
        Host(2);
        var big = Shared("pack.a", "big.bin", Chunk * 10);
        var now = Shared("pack.a", "now.bin", 5);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = big });
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = now, Urgent = true });
        _t.OnAck(2, new ChunkAck { Package = "pack.a", Path = big, Offset = 0 });
        Assert.Equal(now, ChunksTo(2)[^1].Path);
        _t.OnAck(2, new ChunkAck { Package = "pack.a", Path = now, Offset = 0 });
        Assert.Equal((big, Chunk * 2L), (ChunksTo(2)[^1].Path, ChunksTo(2)[^1].Offset));
    }

    [Fact]
    public void Every_player_gets_their_own_window()
    {
        Host(2, 3);
        var rel = Shared("pack.a", "big.bin", Chunk * 10);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = rel });
        _t.Serve(3, new NeedAsset { Package = "pack.a", Path = rel });
        Assert.Equal((2, 2), (ChunksTo(2).Count, ChunksTo(3).Count));
    }

    [Fact]
    public void Asking_twice_for_a_file_on_its_way_sends_it_once()
    {
        Host(2);
        var rel = Shared("pack.a", "f.bin", 10);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = Shared("pack.a", "big.bin", Chunk * 10) });
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = rel });
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = rel, Urgent = true });
        Assert.Equal(2, _t.Pending(2));
    }

    [Fact]
    public void A_file_that_shrinks_while_it_is_sent_is_reported_missing()
    {
        Host(2);
        var rel = Shared("pack.a", "big.bin", Chunk * 10);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = rel });
        using (var shrink = new FileStream(Here("pack.a", rel), FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) shrink.SetLength(Chunk);
        _t.OnAck(2, new ChunkAck { Package = "pack.a", Path = rel, Offset = 0 });
        Assert.Equal(-1, ChunksTo(2)[^1].Total);
        Assert.Equal(0, _t.Pending(2));
    }

    [Fact]
    public void A_player_who_leaves_is_sent_nothing_more()
    {
        Host(2);
        var rel = Shared("pack.a", "big.bin", Chunk * 10);
        _t.Serve(2, new NeedAsset { Package = "pack.a", Path = rel });
        _t.PeerLeft(2);
        _t.OnAck(2, new ChunkAck { Package = "pack.a", Path = rel, Offset = 0 });
        Assert.Equal(2, ChunksTo(2).Count);
        Assert.Equal(0, _t.Pending(2));
    }

    [Fact]
    public void An_upload_keeps_a_window_on_the_way_and_a_stalled_one_starts_again()
    {
        var dir = Path.Combine(_root, "user.me");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "big.png"), new byte[Chunk * 6 + 1]);
        _t.Upload("user.me", "big.png");
        Assert.Equal(new long[] { 0, 1 }, Sent<UploadAsset>().Select(u => u.Offset / Chunk));
        _t.OnAck(1, new ChunkAck { Package = "user.me", Path = "big.png", Offset = 0 });
        Assert.Equal(3, Sent<UploadAsset>().Count);
        _now = 11;
        _t.Pump();
        Assert.Equal(5, Sent<UploadAsset>().Count);
        Assert.Equal(Chunk * 6L + 1, Sent<UploadAsset>()[^1].Total);
    }

    [Fact]
    public void An_empty_file_is_uploaded_as_one_empty_piece()
    {
        var dir = Path.Combine(_root, "user.me");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "pack.json"), []);
        _t.Upload("user.me", "pack.json");
        var u = Assert.Single(Sent<UploadAsset>());
        Assert.Equal((0L, 0L, 0), (u.Offset, u.Total, u.Data.Length));
    }

    [Fact]
    public void An_upload_whose_hash_matches_its_name_is_kept_and_acknowledged()
    {
        Host();
        byte[] data = [4, 5, 6];
        var path = HashName(data);
        _t.ReceiveUpload(1, new UploadAsset { Package = "user.me", Path = path, Offset = 0, Total = 3, Data = data });
        Assert.Single(Sent<ChunkAck>());
        Assert.Equal(data, File.ReadAllBytes(Here("user.me", path)));
        Assert.Equal(0, _t.OpenUploads);
    }

    [Fact]
    public void An_upload_whose_hash_does_not_match_is_refused()
    {
        Host();
        var path = "assets/" + new string('a', 64) + ".png";
        _t.ReceiveUpload(1, new UploadAsset { Package = "user.me", Path = path, Offset = 0, Total = 1, Data = [1] });
        Assert.Equal($"upload of user.me/{path} refused: hash mismatch", Sent<ErrorMessage>().Single().Text);
        Assert.False(Directory.EnumerateFiles(Path.Combine(_root, "user.me"), "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void An_upload_stream_is_closed_and_its_part_file_removed_when_the_peer_leaves()
    {
        Host();
        var data = new byte[Chunk * 2];
        var path = HashName(data);
        _t.ReceiveUpload(1, new UploadAsset { Package = "user.me", Path = path, Offset = 0, Total = data.Length, Data = data[..Chunk] });
        Assert.Equal(1, _t.OpenUploads);
        _t.PeerLeft(2);
        Assert.Equal(1, _t.OpenUploads);
        _t.PeerLeft(1);
        Assert.Equal(0, _t.OpenUploads);
        Assert.False(File.Exists(Here("user.me", path) + ".part1"));
    }

    [Fact]
    public void Leaving_the_table_closes_every_upload_still_coming_in()
    {
        Host();
        var data = new byte[Chunk * 2];
        var path = HashName(data);
        _t.ReceiveUpload(1, new UploadAsset { Package = "user.me", Path = path, Offset = 0, Total = data.Length, Data = data[..Chunk] });
        _t.Reset();
        Assert.Equal(0, _t.OpenUploads);
        Assert.False(File.Exists(Here("user.me", path) + ".part1"));
    }
}
