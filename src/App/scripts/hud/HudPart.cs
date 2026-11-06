using System;
using Godot;

namespace KTable.App;

abstract class HudPart
{
    protected readonly HudContext Ctx;

    protected HudPart(HudContext ctx) { Ctx = ctx; }

    protected Session S => Ctx.S;
    protected Table T => Ctx.T;
    protected Net N => Ctx.N;
    protected Settings Cfg => Ctx.Cfg;
    protected CanvasLayer HudNode => Ctx.Node;

    protected void Log(string bb) => Ctx.Log(bb);
    protected void Say(string text) => Ctx.Say(text);

    protected static Button Btn(string text, Action onPress, string tip = "") => Ui.Btn(text, onPress, tip);
    protected static Label Hint(string text) => Ui.Hint(text);
    protected static Label Header(string text) => Ui.Header(text);
}
