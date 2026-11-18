using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public static class FirmeAdventurer
{
    public const string PackId = MovedKinds.Adventurer, PackName = "Firme's Adventurer Pack";
    public const string FreshBody = PackId + ".fresh-body";
    const int Version = 1;

    static string ShippedPart => Path.Combine(StandardPacks.ShippedRoot, "tavern-part");

    static string PartText => File.Exists(Path.Combine(ShippedPart, "part.json")) ? File.ReadAllText(Path.Combine(ShippedPart, "part.json")) : "";

    static string Stamp => _stamp ??= "firme-adventurer:" + Version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PartText)))[..16];
    static string? _stamp;

    public static PieceDef Body() => new()
    {
        Id = FreshBody, Name = "Fresh body", Kind = CrowdPiece.Def.Kind, Mesh = CrowdPiece.Made, Rig = CrowdPiece.Made, Size = CrowdPiece.Def.Size,
        Thickness = CrowdPiece.Def.Thickness, Tags = [PieceDef.LifeSizeTag, "avatar"],
    };

    public static PackManifest Pack(Dictionary<string, byte[]> files)
    {
        var pack = new PackManifest
        {
            Id = PackId, Name = PackName,
            Source = new PackSource { Authors = new() { "Firme" }, License = "standard pack, included with the app", Url = Stamp },
        };
        pack.Pieces.Add(Body());
        var face = Image.CreateEmpty((int)(RoundTracker.Width * RoundTracker.PixelsPerUnit), (int)(RoundTracker.Height * RoundTracker.PixelsPerUnit), false, Image.Format.Rgba8);
        face.Fill(Color.FromHtml(RoundTracker.Face));
        var bytes = face.SavePngToBuffer();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        files[$"assets/{sha}.png"] = bytes;
        pack.Assets[sha] = new AssetEntry { File = $"assets/{sha}.png", Kind = "image", W = face.GetWidth(), H = face.GetHeight() };
        var tracker = RoundTracker.Def(MovedKinds.Now(MovedKinds.OldTracker), sha);
        tracker.Formerly = [MovedKinds.OldTracker];
        pack.Pieces.Add(tracker);
        foreach (var (rel, data) in Tavern(pack)) files[rel] = data;
        return pack;
    }

    static IEnumerable<(string Rel, byte[] Bytes)> Tavern(PackManifest pack)
    {
        var list = Path.Combine(ShippedPart, "part.json");
        if (!File.Exists(list)) yield break;
        var part = Json.Load<PackManifest>(list);
        foreach (var d in part.Pieces)
        {
            d.Formerly = [d.Id];
            d.Id = MovedKinds.Now(d.Id);
            foreach (var p in d.States.SelectMany(s => s.Parts ?? [])) p.Def = MovedKinds.Now(p.Def);
            pack.Pieces.Add(d);
        }
        foreach (var (sha, asset) in part.Assets)
        {
            pack.Assets[sha] = asset;
            yield return (asset.File, File.ReadAllBytes(Path.Combine(ShippedPart, asset.File)));
        }
    }

    public static void Ensure(PackageStore store) => StandardPacks.Install(store, PackId, Stamp, Pack);
}
