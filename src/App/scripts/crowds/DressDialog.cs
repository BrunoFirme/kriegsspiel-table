using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using E = KTable.Core.Model.CrowdEntry;
using Slot = KTable.Core.Model.CrowdSlots;

namespace KTable.App;

sealed class DressDialog : HudPart
{
    const string None = "";
    const int SwatchSide = 22, MostSaved = 200;
    static readonly string[] FaceSlots = [Slot.Skin, Slot.Hair, Slot.Brow, Slot.Eye, Slot.Lip];
    static readonly string[] ClothSlots = [Slot.Outer, Slot.Under, Slot.Legs, Slot.Hat, Slot.Accent, Slot.Linen, Slot.Trim, Slot.Metal, Slot.Wood, Slot.Brass];
    static readonly string[] HeadParts = [Wardrobe.Hair, Wardrobe.Beard];

    readonly LookOffer _offer;
    readonly Action<CrowdLook> _apply;
    readonly Nationality? _bound;
    readonly string? _wealth;
    CrowdLook _look;
    AcceptDialog _dlg = null!;
    DialogPages _pages = null!;

    DressDialog(HudContext c, CrowdLook look, Nationality? bound, string? wealth, Action<CrowdLook> apply) : base(c)
    {
        (_look, _bound, _wealth, _apply) = (look.Clone(), bound, wealth, apply);
        _offer = CrowdWardrobe.Offer(bound, wealth);
    }

    public static void Piece(HudContext c, string layer, PieceInstance piece) =>
        new DressDialog(c, CrowdPiece.LookOf(piece, c.S.Store.Def(piece.Def)) ?? CrowdWardrobe.Fresh(), null, null,
            look => c.S.Do(new SetLook { Layer = layer, Id = piece.Id, Look = look })).Show(c.S.Store.Def(piece.Def)?.Name ?? "Body");

    public static void Person(HudContext c, string key, CrowdLook look, string name) =>
        new DressDialog(c, look, null, null, l => c.S.Do(new SetLook { Person = key, Look = l })).Show(name);

    public static void Own(HudContext c, string pieceId, PieceInstance piece)
    {
        var rights = c.S.View.RightsOf(c.S.Me.Name);
        var own = Nationalities.Of(c.S.View.Packs.Select(p => c.S.Store.Pack(p)).OfType<PackManifest>());
        var bound = rights.DressAs != null && CrowdDress.Find(rights.DressAs, own) is { } n ? CrowdDress.Flat(n, own) : null;
        new DressDialog(c, CrowdPiece.LookOf(piece, c.S.Store.Def(piece.Def)) ?? CrowdWardrobe.Fresh(), bound, rights.DressWealth, l => c.N.Dress(pieceId, l)).Show("Your body");
    }

    void Show(string name)
    {
        _dlg = new AcceptDialog { Title = $"Dress · {name}", OkButtonText = "Done" };
        _pages = new DialogPages("dress", new Vector2(620, 420));
        Fill();
        _dlg.AddChild(_pages.Root);
        Ui.Open(HudNode, _dlg, new Vector2I(660, 500));
        Ui.FreeOnClose(_dlg);
    }

    void Fill()
    {
        BodyPage(_pages.Add("Body"));
        FacePage(_pages.Add("Face and hair"));
        GarmentPage(_pages.Add("Garments"));
        ColourPage(_pages.Add("Colours"), ClothSlots);
        CarriedPage(_pages.Add("Carried"));
        SavedPage(_pages.Add("Saved"));
        _pages.ShowFirst();
    }

    void Refill()
    {
        _dlg.RemoveChild(_pages.Root);
        _pages.Root.QueueFree();
        _pages = new DialogPages("dress", new Vector2(620, 420));
        Fill();
        _dlg.AddChild(_pages.Root);
    }

    void Change(Action<CrowdLook> edit, bool refill = false)
    {
        edit(_look);
        _apply(_look.Clone());
        if (refill) Refill();
    }

    static (string, string, string, Texture2D?) Item(string key, string text) => (key, text, "", null);

    static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].Replace('-', ' ');

    GridContainer Form(VBoxContainer page)
    {
        var form = Ui.Form(Ui.DialogLabels);
        page.AddChild(form);
        return form;
    }

    static void Row(GridContainer form, string label, Control control)
    {
        form.AddChild(new Label { Text = label });
        form.AddChild(control);
    }

    void BodyPage(VBoxContainer page)
    {
        var form = Form(page);
        var peoples = _bound != null ? [_bound] : Nationalities.Offered(S.View.Packs.Select(p => S.Store.Pack(p)).OfType<PackManifest>()).ToList();
        string people = peoples[0].Id, wealth = _wealth ?? E.Middle, calling = CrowdBaseline.Labourer;
        Row(form, "Nationality", Ui.Picker(peoples.Select(n => Item(n.Id, n.Name ?? Capital(n.Id))).ToList(), people, k => people = k));
        var wealths = _wealth != null ? [_wealth] : CrowdBaseline.Wealths;
        Row(form, "Wealth", Ui.Picker(wealths.Select(w => Item(w, Capital(w))).ToList(), wealth, k => wealth = k));
        Row(form, "Calling", Ui.Picker(CrowdBaseline.Person.Callings!.Keys.Order().Select(k => Item(k, Capital(k))).ToList(), calling, k => calling = k));
        Row(form, "", Ui.Btn("Dress as one of them", () => Change(l => Dressed(l, peoples.First(n => n.Id == people), wealth, calling), refill: true),
            "A fresh outfit as that nationality, wealth and calling dress"));
        Row(form, "Sex", Ui.Picker([Item(E.Man, "Man"), Item(E.Woman, "Woman")], _look.Sex, k => Change(l => l.Sex = k)));
        Row(form, "Age", Ui.Picker(CrowdBaseline.Bands.Select(b => Item(b, Capital(b))).ToList(), _look.Band, k => Change(l => l.Band = k)));
        Row(form, "Height", Slider(CrowdWardrobe.LeastHeightM, CrowdWardrobe.MostHeightM, 0.01, _look.HeightM, v => $"{v:0.00} m", v => Change(l => l.HeightM = v)));
        Row(form, "Build", Slider(CrowdWardrobe.LeastBuild, CrowdWardrobe.MostBuild, 0.05, _look.Build, v => $"{v:0.00}", v => Change(l => l.Build = v)));
        Row(form, "Stoop", Slider(0, CrowdWardrobe.MostStoop, 0.05, _look.Stoop, v => $"{v:0.00}", v => Change(l => l.Stoop = v)));
        Ui.AlignForm(form);
    }

    static void Dressed(CrowdLook look, Nationality people, string wealth, string calling)
    {
        var flat = CrowdDress.Flat(people);
        var entry = CrowdDress.Person(flat, 0, Random.Shared.Next(), 0, look.Sex, look.Band, calling);
        entry.Wealth = wealth;
        var made = CrowdDress.Dress(entry, flat, CrowdDress.ArmyCoat(flat, entry.Seed));
        (look.Worn, look.Colours, look.Item) = (made.Worn, made.Colours, made.Item);
    }

    static HBoxContainer Slider(double least, double most, double step, double now, Func<double, string> say, Action<double> set)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        var slider = new HSlider
        {
            MinValue = least, MaxValue = most, Step = step, Value = now, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.DragEnded += changed => { if (changed) set(slider.Value); };
        row.AddChild(slider);
        row.AddChild(Ui.ValueBox(slider, say));
        return row;
    }

    void FacePage(VBoxContainer page)
    {
        var form = Form(page);
        Row(form, "Face", Ui.Picker(CrowdLook.Faces.Select(f => Item(f, Capital(f))).ToList(), _look.Face, k => Change(l => l.Face = k)));
        foreach (var part in HeadParts) Row(form, Capital(part), PartPicker(part));
        foreach (var slot in FaceSlots) Row(form, Capital(slot), ColourRow(slot));
        Ui.AlignForm(form);
    }

    void GarmentPage(VBoxContainer page)
    {
        var form = Form(page);
        foreach (var part in Wardrobe.Parts.Except(HeadParts)) Row(form, Capital(part), PartPicker(part));
        Ui.AlignForm(form);
    }

    OptionButton PartPicker(string part)
    {
        var kinds = _offer.Kinds.GetValueOrDefault(part) ?? [];
        var items = new List<(string, string, string, Texture2D?)> { Item(None, "None") };
        items.AddRange(kinds.Select(k => Item(k, Capital(k))));
        var picker = Ui.Picker(items, _look.Part(part)?.Kind ?? None, k => Change(l =>
        {
            if (k == None) l.TakeOff(part);
            else l.Wear(new Garment { Part = part, Kind = k, Slot = l.Part(part)?.Slot });
        }));
        picker.Disabled = kinds.Count == 0;
        return picker;
    }

    void ColourPage(VBoxContainer page, IEnumerable<string> slots)
    {
        var form = Form(page);
        foreach (var slot in slots) Row(form, Capital(slot), ColourRow(slot));
        Ui.AlignForm(form);
    }

    Control ColourRow(string slot)
    {
        var now = _look.Colours.GetValueOrDefault(slot) ?? "#808080";
        if (_offer.Colours?.GetValueOrDefault(slot) is not { } allowed)
        {
            var pick = Ui.SwatchPicker(Coords.ParseColor(now, Colors.Gray), new Vector2(SwatchSide * 2, SwatchSide), Capital(slot));
            pick.PopupClosed += () => Change(l => l.Colours[slot] = "#" + pick.Color.ToHtml(false));
            return pick;
        }
        var row = new HFlowContainer();
        foreach (var hex in allowed)
        {
            var swatch = Ui.Swatch(Coords.ParseColor(hex, Colors.Gray), new Vector2(SwatchSide, SwatchSide), hex, string.Equals(hex, now, StringComparison.OrdinalIgnoreCase));
            swatch.Pressed += () => Change(l => l.Colours[slot] = hex, refill: true);
            row.AddChild(swatch);
        }
        return row;
    }

    void CarriedPage(VBoxContainer page)
    {
        var form = Form(page);
        var items = new List<(string, string, string, Texture2D?)> { Item(None, "Nothing") };
        items.AddRange(_offer.Items.Select(i => Item(i, Capital(i))));
        Row(form, "Carries", Ui.Picker(items, _look.Item ?? None, k => Change(l => l.Item = k == None ? null : k)));
        Ui.AlignForm(form);
    }

    void SavedPage(VBoxContainer page)
    {
        var form = Form(page);
        var name = new LineEdit { PlaceholderText = "Name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var save = new HBoxContainer();
        save.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        save.AddChild(name);
        save.AddChild(Ui.Btn("Save", () =>
        {
            var key = name.Text.Trim();
            if (key.Length == 0 || (Cfg.Looks?.Count ?? 0) >= MostSaved && Cfg.Looks?.ContainsKey(key) != true) return;
            (Cfg.Looks ??= new())[key] = _look.Clone();
            Cfg.Save();
            Refill();
        }, "Keep this look on this PC for any table"));
        Row(form, "This look", save);
        foreach (var (key, look) in (Cfg.Looks ?? new()).OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase).Where(l => CrowdWardrobe.Fits(l.Value, _offer)))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
            row.AddChild(Ui.Btn("Wear", () => Change(l => Worn(l, look), refill: true), "Dress in this saved look"));
            row.AddChild(Ui.Btn("Forget", () =>
            {
                Cfg.Looks?.Remove(key);
                Cfg.Save();
                Refill();
            }, "Drop it from this PC"));
            Row(form, key, row);
        }
        Ui.AlignForm(form);
    }

    static void Worn(CrowdLook look, CrowdLook saved)
    {
        var copy = saved.Clone();
        (look.Sex, look.Band, look.Face, look.HeightM, look.Build, look.Stoop) = (copy.Sex, copy.Band, copy.Face, copy.HeightM, copy.Build, copy.Stoop);
        (look.Worn, look.Colours, look.Item) = (copy.Worn, copy.Colours, copy.Item);
    }
}
