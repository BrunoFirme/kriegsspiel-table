using static KTable.Server.Scripting.ApiDoc;

namespace KTable.Server.Scripting;

static class MapDocs
{
    public const string Intro = "The ground the map knows: how long a trip takes over the built ways and across the laid ground of one map sheet.";

    public static void Global()
    {
        F("global", "map", "table", "The map's ground: travel times and routes over the built ways and the laid ground.");
        See("map.travel", "map.route");
    }

    public static void Calls()
    {
        M("map", "travel(a, b, by)", "Seconds of game time a trip from a to b takes, over the built ways and across the laid ground of the sheet under a.", null, "number?");
        P("a", "Piece|Point", "Where the trip starts: a piece, or a { x =, z = } map point.");
        P("b", "Piece|Point", "Where it ends, on the same map sheet.");
        O("by", "string", "Who travels: \"foot\", \"rider\", \"cart\", \"coach\", or one the packs' paces add. Default \"foot\".");
        R("The seconds, or nil when there is no way: b on another sheet, a sheet with no built way and no laid ground, water with no crossing, a cart with no road.");
        Note("Off the ways the trip goes at a cross-country pace, slowed by woods and slopes. A pack sets its own paces per traveller and way.");
        Note("An unknown traveller is an error that names the ones there are. Nothing on the table shows the time: the script does what it likes with it.");
        X("""
            local s = map.travel(self, board.find("p-000123"), "rider")
            if s then game.say((self.name or "The courier") .. " rides there in " .. math.ceil(s / 60) .. " minutes") end
            """);
        See("map.route", "board.distance");

        M("map", "route(a, b, by)", "The way a trip from a to b takes, as points: { { x =, z =, seconds = }, … }.", null, "table[]?");
        P("a", "Piece|Point", "Where the trip starts: a piece, or a { x =, z = } map point.");
        P("b", "Piece|Point", "Where it ends, on the same map sheet.");
        O("by", "string", "Who travels, as for map.travel. Default \"foot\".");
        R("A list of map points from a to b; `seconds` is the time of the leg that ends at that point, 0 at the first. nil when map.travel gives nil.");
        X("""
            local way = map.route(self, { x = 40, z = -12 }, "cart")
            if way then board.draw{ points = way, color = "#8a6d3b", width = 0.2 } end
            """);
        See("map.travel", "board.draw");
    }
}
