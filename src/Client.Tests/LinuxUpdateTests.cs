using System.Formats.Tar;
using System.IO.Compression;
using KTable.Client.Updates;
using KTable.Core;

namespace KTable.Client.Tests;

public sealed class LinuxUpdateTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ktable-linuxupdate-" + Guid.NewGuid().ToString("N"));

    public LinuxUpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => BestEffort.DeleteFolder(_dir);

    string Archive(params (string Name, string Text)[] files)
    {
        var path = Path.Combine(_dir, "update.tar.gz");
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gz, TarEntryFormat.Gnu);
        foreach (var (name, text) in files)
        {
            var entry = new GnuTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)) };
            tar.WriteEntry(entry);
        }
        return path;
    }

    [Fact]
    public void An_update_archive_unpacks_beside_and_names_its_install_script()
    {
        var archive = Archive(("kriegsspiel-table/install.sh", "#!/bin/sh\n"), ("kriegsspiel-table/KriegsspielTable.x86_64", "game"));
        var into = Path.Combine(_dir, "unpacked");
        var script = LinuxUpdate.Unpack(archive, into);
        Assert.Equal(Path.Combine(into, "kriegsspiel-table", "install.sh"), script);
        Assert.True(File.Exists(Path.Combine(into, "kriegsspiel-table", "KriegsspielTable.x86_64")));
    }

    [Fact]
    public void An_archive_without_the_install_script_is_refused()
    {
        var archive = Archive(("kriegsspiel-table/KriegsspielTable.x86_64", "game"));
        Assert.Throws<InvalidDataException>(() => LinuxUpdate.Unpack(archive, Path.Combine(_dir, "unpacked")));
    }

    [Fact]
    public void An_archive_reaching_outside_its_folder_is_refused_and_writes_nothing_there()
    {
        var archive = Archive(("../escaped.txt", "no"), ("kriegsspiel-table/install.sh", "#!/bin/sh\n"));
        Assert.ThrowsAny<IOException>(() => LinuxUpdate.Unpack(archive, Path.Combine(_dir, "unpacked")));
        Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
    }

    [Fact]
    public void Only_the_copy_install_sh_put_in_place_counts_as_installed()
    {
        var home = Path.Combine(_dir, "home");
        var installed = LinuxUpdate.InstalledDir(home);
        Directory.CreateDirectory(installed);
        Assert.False(LinuxUpdate.InstalledAt(installed, home));
        File.WriteAllText(Path.Combine(installed, "install.sh"), "#!/bin/sh\n");
        Assert.True(LinuxUpdate.InstalledAt(installed, home));
        Assert.True(LinuxUpdate.InstalledAt(installed + Path.DirectorySeparatorChar, home));

        var loose = Path.Combine(_dir, "Downloads", "kriegsspiel-table");
        Directory.CreateDirectory(loose);
        File.WriteAllText(Path.Combine(loose, "install.sh"), "#!/bin/sh\n");
        Assert.False(LinuxUpdate.InstalledAt(loose, home));
    }

    [Fact]
    public void The_new_copy_is_installed_once_the_game_has_closed_and_then_started()
    {
        var start = LinuxUpdate.AfterExit(4242, "/tmp/u/kriegsspiel-table/install.sh", "/tmp/u/install.log", "/home/a/.local/opt/kriegsspiel-table/KriegsspielTable.x86_64");
        Assert.Equal("/bin/sh", start.FileName);
        Assert.False(start.UseShellExecute);
        var args = start.ArgumentList.ToList();
        Assert.Equal("-c", args[0]);
        Assert.Contains("kill -0 \"$1\"", args[1]);
        Assert.Contains("&& exec \"$4\"", args[1]);
        Assert.Equal(new[] { "sh", "4242", "/tmp/u/kriegsspiel-table/install.sh", "/tmp/u/install.log", "/home/a/.local/opt/kriegsspiel-table/KriegsspielTable.x86_64" }, args.Skip(2));
    }
}
