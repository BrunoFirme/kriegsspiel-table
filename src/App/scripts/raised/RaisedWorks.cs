using Godot;

namespace KTable.App;

static class RaisedWorks
{
    const float SawPitch = 0.5f, PowerChimneys = 2, ElevatorM = 24, BinShare = 0.4f;

    public static bool Of(RaisedRoofs.Raise r, string cls, RaisedLook look)
    {
        switch (cls)
        {
            case "factory":
                Factory(r);
                return true;
            case "hangar":
                RaisedRoofs.Walls(r, r.F.Ring, r.Eaves, r.Wall);
                RaisedRoofs.Vault(r with { Roof = r.Roof with { Code = RaisedCodes.Roof + (int)Cover.Sheet } });
                return true;
            case "powerhouse":
                RaisedRoofs.Walls(r, r.F.Ring, r.Eaves, r.Wall);
                RaisedRoofs.Flat(r, true, false);
                for (int i = 0; i < PowerChimneys; i++)
                    RaisedBuildings.Chimney(r.M, r.F.At((i + 1) / (PowerChimneys + 1), 0.5f), r.Eaves, RaisedBuildings.FactoryChimneyM, r.Wall with { Code = (int)Fabric.Brick });
                return true;
            case "bunker":
                RaisedRoofs.Block(r.M, r.F.Ring, r.Bottom, r.Bottom + RaisedBuildings.LowBunkerM * r.M.PerMetre, r.Wall with { Code = (int)Fabric.Concrete });
                return true;
            case "elevator":
                RaisedRoofs.Block(r.M, r.F.Part(0, BinShare, 0, 1).Ring, r.Bottom, r.Bottom + ElevatorM * r.M.PerMetre, r.Wall with { Code = (int)Fabric.Concrete });
                RaisedRoofs.Walls(r, r.F.Part(BinShare, 1, 0, 1).Ring, r.Eaves, r.Wall);
                RaisedRoofs.Gable(r with { F = r.F.Part(BinShare, 1, 0, 1) });
                return true;
            case "station" or "dockshed" or "markethall" when look.Form is Form.Gable or Form.Hip or Form.Valley:
                RaisedRoofs.Walls(r, r.F.Ring, r.Eaves, r.Wall);
                RaisedRoofs.Gable(r);
                return true;
        }
        return false;
    }

    static void Factory(RaisedRoofs.Raise r)
    {
        RaisedRoofs.Walls(r, r.F.Ring, r.Eaves, r.Wall);
        foreach (var bay in RaisedBuildings.Bays(r.F, r.M.PerMetre)) RaisedRoofs.Lean(r with { F = bay, Pitch = SawPitch });
        RaisedBuildings.Chimney(r.M, r.F.At(0.92f, 0.85f), r.Bottom, RaisedBuildings.FactoryChimneyM, r.Wall with { Code = (int)Fabric.Brick });
    }
}
