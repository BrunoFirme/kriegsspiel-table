using System.Globalization;
using KTable.Core.Panels;
using KTable.Core.Protocol;

namespace KTable.Client.Panels;

public sealed record PanelMove(List<int> Path, bool Showing, string Animation, float Duration, float Delay);

public static class PanelMoves
{
    const float DefaultDuration = 0.25f;

    static IEnumerable<Command> Flat(Command c) => c is Batch b ? b.Items.SelectMany(Flat) : new[] { c };

    public static List<PanelMove> Of(Command c, UiLayout? layout, string? piece)
    {
        var moves = new List<PanelMove>();
        if (layout == null) return moves;
        foreach (var one in Flat(c))
        {
            if (one is not SetUiAttributes { Kind: PanelOps.Show or PanelOps.Hide } sua || sua.Piece != piece || !layout.Ids.TryGetValue(sua.Id, out var path)) continue;
            if (layout.At(path) is not { } e) continue;
            bool showing = sua.Kind == PanelOps.Show;
            var animation = e.Get(showing ? "showAnimation" : "hideAnimation");
            if (string.IsNullOrEmpty(animation) || animation == "None") continue;
            float duration = Number(e.Get("animationDuration"), DefaultDuration);
            float delay = Number(e.Get(showing ? "showAnimationDelay" : "hideAnimationDelay"), 0);
            moves.Add(new PanelMove(path, showing, animation, duration, delay));
        }
        return moves;
    }

    static float Number(string? s, float fallback) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

}
