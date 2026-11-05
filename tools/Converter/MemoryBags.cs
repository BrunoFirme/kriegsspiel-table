using System.Text.Json;
using System.Text.Json.Nodes;

namespace KTable.Converter;

public sealed record Layout(TtsObj Bag, string Name, string Slug, string ExtrasPack);

public static class MemoryBags
{
    public static IEnumerable<TtsObj> Of(TtsSave save) => save.Objects.Where(IsMemoryBag);

    const int LeastLayout = 2;

    public static bool IsMemoryBag(TtsObj o) =>
        o.Name is "Bag" or "Custom_Model_Bag" && o.Lua.Contains("memoryList") && o.Lua.Contains("buttonClick_place") && Remembered(o).Count >= LeastLayout;

    public sealed record Spot(double X, double Y, double Z, double RotX, double RotY, double RotZ, bool Locked);

    public static Dictionary<string, Spot> Remembered(TtsObj bag)
    {
        var spots = new Dictionary<string, Spot>();
        try
        {
            if (JsonNode.Parse(bag.LuaState) is not JsonObject state || state["ml"] is not JsonObject ml) return spots;
            foreach (var (guid, v) in ml)
            {
                if (v is not JsonObject e || e["pos"] is not JsonObject pos) continue;
                var rot = e["rot"] as JsonObject;
                spots[guid] = new Spot(N(pos, "x"), N(pos, "y"), N(pos, "z"), N(rot, "x"), N(rot, "y"), N(rot, "z"), e["lock"] is JsonValue l && l.TryGetValue<bool>(out var b) && b);
            }
        }
        catch (JsonException) { }
        return spots;
    }

    public static TtsSave Laid(TtsSave save, TtsObj bag)
    {
        var spots = Remembered(bag);
        var objects = new List<TtsObj>();
        foreach (var o in save.Objects)
        {
            if (ReferenceEquals(o, bag)) continue;
            objects.Add(spots.TryGetValue(o.Guid, out var at) ? Moved(o, at) : o);
        }
        objects.AddRange(bag.Contained.Where(c => spots.ContainsKey(c.Guid)).Select(c => Moved(c, spots[c.Guid])));
        return new TtsSave
        {
            WorkshopId = save.WorkshopId, SaveName = save.SaveName, Objects = objects, Lua = save.Lua, LuaState = save.LuaState,
            SkyUrl = save.SkyUrl, Table = save.Table, Notes = save.Notes, Notebook = save.Notebook,
        };
    }

    static TtsObj Moved(TtsObj o, Spot at)
    {
        var j = (JsonObject)o.J.DeepClone();
        var t = j["Transform"] as JsonObject ?? new JsonObject();
        t["posX"] = at.X;
        t["posY"] = at.Y;
        t["posZ"] = at.Z;
        t["rotX"] = at.RotX;
        t["rotY"] = at.RotY;
        t["rotZ"] = at.RotZ;
        j["Transform"] = t;
        j["Locked"] = at.Locked;
        return new TtsObj(j);
    }

    static double N(JsonObject? o, string k) => o?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}
