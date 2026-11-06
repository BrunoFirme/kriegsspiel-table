using System;
using Godot;

namespace KTable.App;

static class TextPrompt
{
    public static void Open(Node parent, string title, string label, string initial, Action<string> done)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.AddChild(new Label { Text = label });
        var le = new LineEdit { Text = initial };
        box.AddChild(le);
        InPlace.Ask(parent, title, "OK", () => { if (le.Text.Trim().Length > 0) done(le.Text); }, null, box, le);
    }
}
