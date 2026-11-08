using System.Linq;
using Godot;
using KTable.Client;

namespace KTable.App;

sealed class BackdropPick
{
    const string SolidName = "Solid colour", OwnName = "Your own picture";

    public readonly OptionButton Pick;
    public readonly ColorPickerButton Colour;
    readonly bool _own;

    public BackdropPick(string now, bool own, bool editable)
    {
        _own = own;
        Pick = new OptionButton { CustomMinimumSize = new Vector2(220, 0), Disabled = !editable, TooltipText = "A sky also sets the light", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Pick.AddItem(SolidName);
        foreach (var b in Backdrops.All) Pick.AddItem(b.Name);
        if (own) Pick.AddItem(OwnName);
        Pick.Selected = Shown(now);
        Colour = new ColorPickerButton { Color = TableBackdrop.ColourOf(now), EditAlpha = false, CustomMinimumSize = new Vector2(44, 0), Disabled = !editable, TooltipText = "The colour" };
        Colour.Visible = Pick.Selected == 0;
        Pick.ItemSelected += i => Colour.Visible = i == 0;
    }

    public int OwnIndex => Backdrops.All.Count + 1;

    public bool IsOwn(long i) => _own && i == OwnIndex;

    public int Shown(string backdrop) => backdrop.Length == 0 || Backdrops.IsColour(backdrop) ? 0
        : Backdrops.All.ToList().FindIndex(b => b.Id == backdrop) is var i and >= 0 ? i + 1 : _own ? OwnIndex : 0;

    public string Hex => "#" + Colour.Color.ToHtml(false);

    public static Backdrop? Chosen(long i) => i >= 1 && i <= Backdrops.All.Count ? Backdrops.All[(int)i - 1] : null;

    public HBoxContainer Row(params Control[] more)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Row);
        row.AddChild(Pick);
        row.AddChild(Colour);
        foreach (var c in more) row.AddChild(c);
        return row;
    }
}
