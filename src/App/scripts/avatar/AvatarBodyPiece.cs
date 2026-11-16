using System;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class AvatarBodyPiece
{
    const double MinSide = 0.02;
    const int Places = 4;

    public static PieceDef Add(PackageStore store, PackManifest pack, string name, RigImport.Result body)
    {
        var b = body.Shape.Bounds();
        var def = new PieceDef
        {
            Id = PackEdit.NewDefId(pack, name), Name = name, Kind = "model", Tags = new() { "figure", PieceDef.LifeSizeTag },
            Size = new Size2(Side(b.Size.X), Side(b.Size.Z)), Thickness = Side(b.Size.Y),
            Mesh = PackEdit.AddMesh(store, pack, ModelImport.ToObj(body.Shape)),
            Texture = PackEdit.AddImage(store, pack, body.Picture),
            Rig = PackEdit.AddRig(store, pack, RigFile.Write(body.Rig)),
        };
        pack.Pieces.Add(def);
        store.RegisterDef(pack, def);
        return def;
    }

    static double Side(float metres) => Math.Round(Math.Max(MinSide, metres), Places);
}
