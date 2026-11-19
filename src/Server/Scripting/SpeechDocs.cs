using static KTable.Server.Scripting.ApiDoc;

namespace KTable.Server.Scripting;

static class SpeechDocs
{
    public static void Calls()
    {
        M("piece", "speak(text, loudness, to)", "The piece speaks: pieces within earshot hear it, and the players whose bodies heard it read it.");
        P("text", "string", "What it says.");
        O("loudness", "string", "\"whisper\", \"say\" or \"shout\": about 2, 15 and 60 metres by the map's scale. Default \"say\".");
        O("to", "piece", "With \"whisper\": the one piece, or its id, that hears it.");
        X("""
            function on_heard(speaker, text)
              if text:lower():find("password") then self.speak("Pass, friend.") end
            end
            """);
        See("on_heard");
    }

    public static void Events()
    {
        E("event", "on_heard(speaker, text, loudness)", "A piece or a person of the crowds within earshot of this one spoke.");
        P("speaker", "piece|person", "The piece that spoke, or the person of the crowds (see crowds.people).");
        P("text", "string", "What it said.");
        P("loudness", "string", "\"whisper\", \"say\" or \"shout\".");
        See("self.speak");
    }
}
