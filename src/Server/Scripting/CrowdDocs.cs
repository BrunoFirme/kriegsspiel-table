using static KTable.Server.Scripting.ApiDoc;

namespace KTable.Server.Scripting;

static class CrowdDocs
{
    public const string Intro = "The towns' people and the made crowds as the host's table sees them now. Only people out of doors are listed; with the crowds off the lists are empty.";

    public static void Global()
    {
        F("global", "crowds", "table", "The crowds: towns, made crowds and the people out of doors, who can speak.");
        See("crowds.people", "person.speak");
    }

    public static void Calls()
    {
        M("crowds", "towns()", "The towns with people: { id =, name =, made = false, count =, out =, people = function(filter) }.", null, "table[]");
        Note("`count` is everyone the town shows; `out` those out of doors now. `people(filter)` lists the town's own, as crowds.people does.");
        See("crowds.made", "crowds.people");

        M("crowds", "made()", "The made crowds (refugees, a band, a mob): the same fields as a town, with made = true.", null, "table[]");
        See("crowds.towns");

        M("crowds", "people(filter)", "The people out of doors now, every town's and made crowd's, or those the filter keeps.", null, "table[]");
        O("filter", "table", "Any of: town = a town's or made crowd's id; place = \"market\", \"square\", \"well\", \"tavern\", \"worship\", \"fields\", \"street\", \"gate\", \"quay\", \"barracks\", \"work\" or \"home\"; "
            + "calling; sex = \"m\" or \"f\"; age = \"child\", \"youth\", \"adult\" or \"elder\"; wealth = \"poor\", \"middle\" or \"rich\"; staying = true or false; "
            + "near = a piece or { x =, z = } with within = metres (default 15); zone = a zone's id or name.");
        R("A list of people: { id =, name =, calling =, sex =, age =, wealth =, town =, place =, x =, z =, staying =, speak = function }. `place` is where they stay, nil while they walk.");
        X("""
            local talks = {
              { "Fresh bread, two pfennig!", "Too dear." },
              { "They say the Prussians crossed at dawn.", "Hush, not here." },
            }
            local function chatter()
              local folk = crowds.people{ place = "market", staying = true }
              if #folk >= 2 then
                local a, b = folk[math.random(#folk)], folk[math.random(#folk)]
                local t = talks[math.random(#talks)]
                a.speak(t[1])
                wait.seconds(3, function() b.speak(t[2]) end)
              end
              wait.seconds(20, chatter)
            end
            chatter()
            """);
        See("person.speak", "crowds.find");

        M("crowds", "find(id)", "One person by the id a list gave, as they are now.", null, "table?");
        P("id", "string", "The person's id.");
        R("The person, or nil when they are indoors, gone, dead or kept as a piece.");
        See("crowds.people");

        M("person", "speak(text, loudness, to)", "The person speaks where they stand: a bubble over them, and the pieces within earshot hear it as they hear a piece.", null);
        P("text", "string", "What they say.");
        O("loudness", "string", "\"whisper\", \"say\" or \"shout\". Default \"say\".");
        O("to", "piece", "With \"whisper\": the one piece that hears it.");
        Note("Players whose bodies are within earshot read it in their Chat log, as from a piece.");
        See("crowds.people", "self.speak", "on_heard");
    }
}
