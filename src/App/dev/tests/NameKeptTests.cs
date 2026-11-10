using System.IO;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class NameKeptTests
{
    [NodeTest]
    static async Task The_name_on_the_title_screen_is_written_to_settings_when_the_field_is_left_or_entered(TestRunner r)
    {
        var file = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N") + ".json");
        var place = Settings.FileAt;
        Settings.FileAt = () => file;
        try
        {
            var edit = TitleScreen.NameEdit(new Settings());
            r.AddChild(edit);
            await r.Frames(1);
            edit.EmitSignal(LineEdit.SignalName.TextChanged, "Karl");
            Check.That(!File.Exists(file), "typing alone writes nothing yet");
            edit.EmitSignal(Control.SignalName.FocusExited);
            Check.Equal("Karl", Settings.Load().LastName, "the name kept after leaving the field");
            edit.EmitSignal(LineEdit.SignalName.TextChanged, "Anna");
            edit.EmitSignal(LineEdit.SignalName.TextSubmitted, "Anna");
            Check.Equal("Anna", Settings.Load().LastName, "the name kept after Enter");
            edit.QueueFree();
        }
        finally
        {
            Settings.FileAt = place;
            File.Delete(file);
        }
    }
}
