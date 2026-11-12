using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PanelBurstTests
{
    const string U = Layers.Umpire;
    const int Pieces = 24, Fields = 8, Rounds = 3;
    const double FrameSeconds = 0.02;

    static readonly string PanelScript = $$"""
        function burst()
          for round = 1, {{Rounds}} do
            for i = 1, {{Fields}} do
              self.ui.set_value("f" .. i, "v" .. round .. "-" .. i)
              self.ui.set_attribute("f" .. i, "color", round == {{Rounds}} and "#FF0000" or "#00FF00")
            end
          end
        end
        function on_load()
          local fields = ""
          for i = 1, {{Fields}} do fields = fields .. '<Text id="f' .. i .. '" width="400" height="40">-</Text>' end
          self.ui.set('<Panel width="400" height="400" rotation="0 0 180" color="#202020"><VerticalLayout>' .. fields .. '</VerticalLayout></Panel>')
          wait.frames(2, burst)
        end
        """;

    static IEnumerable<T> Under<T>(Node n) where T : Node
    {
        foreach (var c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var d in Under<T>(c)) yield return d;
        }
    }

    [NodeTest]
    static async Task Many_pieces_changing_their_panels_many_times_in_one_frame_build_each_panel_once_and_show_the_last_values(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: Pieces);
        await r.Frame();
        var ids = Enumerable.Range(1, Pieces).Select(i => $"p{i}").ToList();
        int ports = 0;
        void Count(Node n) { if (n is SubViewport) ports++; }
        r.GetTree().NodeAdded += Count;
        try
        {
            t.S.Do(new Batch { Items = ids.Select(id => (Command)new SetScript { Layer = U, Id = id, Text = PanelScript }).ToList() });
            for (int frame = 1; frame <= 10; frame++)
            {
                foreach (var o in t.Server.Scripts.Tick(frame * FrameSeconds))
                    if (o.Message is CommandMessage cm) t.S.OnCommand(cm.Command);
                await r.Frame();
            }
        }
        finally { r.GetTree().NodeAdded -= Count; }
        Check.That(ports >= Pieces, $"every panel is drawn: {ports} for {Pieces} pieces");
        Check.That(ports <= Pieces * 3, $"each panel is built a few times for {Rounds * Fields * 2} changes, not once a change: {ports} panels built for {Pieces} pieces");
        foreach (var id in ids)
        {
            var texts = Under<Control>(t.Node(id)).Select(c => c is Label l ? l.Text : c is RichTextLabel rt ? rt.Text : null).OfType<string>().ToList();
            Check.That(texts.Any(x => x.Contains("v3-8")), $"{id} shows its last values: {string.Join(", ", texts.Take(8))}");
            Check.That(!texts.Any(x => x.Contains("v1-") || x.Contains("v2-")), $"{id} shows nothing from earlier rounds");
        }
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
