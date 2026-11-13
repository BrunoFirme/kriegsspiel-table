using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core.Model;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    IEnumerable<AgentTool> ScenarioTools()
    {
        yield return new AgentTool("read_scenario",
            "How the scenario on the table is set, as Scenario properties shows it: its clock and turn length, the map's scale and the units distances are read in, the grid, the light and its preset, " +
            "the table under the map, the backdrop, the map style, contours, snow line, streams, the wind, whether umpires' pointers are always shown, the packs, the umpire zones and who owns each, " +
            "and how many map sheets, pieces, drawings and decals it holds.",
            Args.Schema(), ReadScenario);
        yield return new AgentTool("list_people",
            "Who is at the table and in which role (host, lead umpire, umpire, observer), who knocks at the door waiting to be let in, and who was let in before and is away.",
            Args.Schema(), ListPeople);
    }

    static string RoleWords(Role r) => r switch
    {
        Role.HostUmpire => "host",
        Role.LeadUmpire => "lead umpire",
        Role.Umpire => "umpire",
        _ => "observer",
    };

    Task<ToolResult> ReadScenario(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var v = s.View;
        var l = v.Lighting;
        var o = new JsonObject
        {
            ["name"] = v.Name,
            ["clock"] = new JsonObject { ["turn"] = v.Clock.Turn, ["time"] = v.Clock.GameTime, ["minutes_per_turn"] = v.Clock.IncrementMinutes },
            ["scale"] = new JsonObject { ["metres_per_unit"] = v.Calibration.UnitsPerMeter is { } u && u > 0 ? Math.Round(1 / u, 4) : null, ["units"] = v.Calibration.Units, ["north"] = v.Calibration.North },
            ["grid"] = new JsonObject { ["kind"] = v.Grid.Kind, ["cell"] = v.Grid.Pitch, ["divisions"] = v.Grid.Subdivisions, ["snap"] = v.Grid.Snap },
            ["light"] = new JsonObject
            {
                ["preset"] = LightPresets.All.FirstOrDefault(p => LightPresets.Same(l, p.Light)).Name, ["sun"] = l.SunEnergy, ["ambient"] = l.Ambient, ["warmth"] = l.Warmth,
                ["sun_from"] = Math.Round(l.SunFrom), ["sun_height"] = Math.Round(l.SunHeight),
                ["shadows"] = l.Shadows, ["from_clock"] = l.FromClock, ["sunrise"] = l.Sunrise ?? DayLight.UsualSunrise, ["sunset"] = l.Sunset ?? DayLight.UsualSunset, ["latitude"] = l.Latitude,
                ["table_shade"] = l.TableShade, ["backdrop"] = l.Backdrop.Length > 0 ? l.Backdrop : "none",
                ["map_style"] = l.MapStyle, ["contours"] = l.Contours, ["snow_line"] = l.SnowLine, ["streams"] = l.Streams,
            },
            ["wind"] = new JsonObject { ["from"] = v.Wind.From, ["strength"] = v.Wind.Strength, ["words"] = $"{Wind.Words(v.Wind.Strength)} from {Wind.Point(v.Wind.From)}" },
            ["pointers_always_shown"] = v.PointersAlwaysShown,
            ["packs"] = new JsonArray(v.Packs.Select(id => (JsonNode?)new JsonObject { ["id"] = id, ["name"] = s.Store.Pack(id)?.Name }).ToArray()),
            ["zones"] = new JsonArray(v.Zones.Select(z => (JsonNode?)new JsonObject { ["name"] = z.Name, ["owner"] = z.Owner, ["colour"] = z.Color, ["corners"] = z.Polygon.Count }).ToArray()),
            ["map_sheets"] = v.Sheets.Count,
            ["pieces"] = v.Layers.Values.Sum(x => x.Count),
            ["drawings"] = v.Drawings.Values.Sum(x => x.Count),
            ["decals"] = v.Decals.Values.Sum(x => x.Count),
            ["global_script"] = v.Script is { Length: > 0 },
            ["briefing"] = v.Briefing is { Length: > 0 } b ? $"{b.Length} characters (read_briefing)" : "none",
            ["password"] = s.Access == null ? null : s.Access.Password.Length > 0 ? "set" : "none",
        };
        return Task.FromResult(new ToolResult(o.ToJsonString(Json)));
    }

    Task<ToolResult> ListPeople(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var o = new JsonObject
        {
            ["you"] = new JsonObject { ["name"] = s.Me.Name, ["role"] = RoleWords(s.Me.Role) },
            ["at_the_table"] = new JsonArray(s.Peers.Select(p => (JsonNode?)new JsonObject { ["name"] = p.Name, ["role"] = RoleWords(p.Role), ["you"] = p.PeerId == s.Me.PeerId ? true : null }).ToArray()),
            ["at_the_door"] = new JsonArray(s.Waiting.Select(p => (JsonNode?)JsonValue.Create(p.Name)).ToArray()),
            ["away"] = new JsonArray(s.Away().Select(p => (JsonNode?)new JsonObject { ["name"] = p.Name, ["role"] = RoleWords(p.Role) }).ToArray()),
        };
        return Task.FromResult(new ToolResult(o.ToJsonString(Json)));
    }
}
