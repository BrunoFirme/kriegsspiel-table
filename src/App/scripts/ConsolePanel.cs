using System.Linq;
using Godot;

namespace KTable.App;

sealed class ConsolePanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly ScriptConsoleView _view;

    public ConsolePanel(HudContext c, FloatPanel panel) : base(c)
    {
        Panel = panel;
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Panel.CustomMinimumSize = Ctx.PanelSize("console", 520, 240);
        _view = new ScriptConsoleView(S, prompt: true)
        {
            Target = () => T.SelectedNodes.Take(2).ToList() is { Count: 1 } one ? one[0].Piece.Id : "",
        };
        _view.SourceClicked += (id, line) => Ctx.ShowScriptWindow(id);
        Panel.Body.AddChild(_view);
    }

    public void FocusPrompt() => _view.FocusPrompt();

    public void Note(string text, bool bad) => _view.AddLocal(text, bad ? "#ff8a70" : "#9aa0a6");
}
