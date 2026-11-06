using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

static class PictureLinks
{
    public static void Offer(FileDialog fd, string packagesRoot, Action<string> got)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Row);
        var link = new LineEdit { PlaceholderText = "or a web link", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
        var use = Ui.Btn("Use link", () => { }, "Downloads the picture and uses it");
        var said = Ui.Hint("");
        row.AddChild(link);
        row.AddChild(use);
        fd.GetVBox().AddChild(row);
        fd.GetVBox().AddChild(said);
        async void Fetch()
        {
            var text = link.Text.Trim();
            if (text.Length == 0) return;
            use.Disabled = true;
            said.Text = "Downloading…";
            var name = await new WebPictures(packagesRoot).Fetch(text);
            if (!GodotObject.IsInstanceValid(fd)) return;
            use.Disabled = false;
            if (name == null) { said.Text = "No picture came from that link (PNG, JPEG, WebP or BMP)."; return; }
            said.Text = "";
            fd.Hide();
            got(new WebPictures(packagesRoot).PathOf(name));
        }
        use.Pressed += Fetch;
        link.TextSubmitted += _ => Fetch();
    }

    public static async Task<(List<string> Files, string? Error)> Local(IEnumerable<string> pathsOrLinks, string packagesRoot)
    {
        var files = new List<string>();
        var failed = new List<string>();
        foreach (var p in pathsOrLinks)
            if (!KTable.Server.Scripting.ScriptHost.IsWebLink(p)) files.Add(p);
            else if (await new WebPictures(packagesRoot).Fetch(p) is { } name) files.Add(new WebPictures(packagesRoot).PathOf(name));
            else failed.Add(p);
        return (files, failed.Count > 0 ? "No picture came from " + string.Join(", ", failed) : null);
    }

    public static string? Keep(string packagesRoot, string path)
    {
        var bytes = BestEffort.Read<byte[]?>(() => File.ReadAllBytes(path), null);
        if (bytes == null) return null;
        if (WebPictures.KindOf(bytes) == null) bytes = ImageFiles.Load(path)?.SavePngToBuffer();
        return bytes == null ? null : new WebPictures(packagesRoot).Keep(bytes);
    }
}
