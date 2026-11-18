using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

static class StudioNationalities
{
    const string Own = "own";

    public static string IdFor(PackManifest pack, string shipped) => $"{pack.Id}.{shipped}-{Own}";

    public static Nationality Copy(PackManifest pack, string shipped)
    {
        var from = Nationalities.Named(shipped) ?? throw new ArgumentException("no such nationality");
        var copy = Json.Clone(from);
        (copy.Id, copy.Name, copy.Base) = (IdFor(pack, shipped), $"{from.Name ?? from.Id} ({pack.Name})", from.Id);
        Put(pack, copy);
        return copy;
    }

    public static void Put(PackManifest pack, Nationality n)
    {
        var list = pack.Nationalities ??= new();
        list.RemoveAll(x => x.Id == n.Id);
        list.Add(n);
    }

    public static List<string> Problems(PackManifest pack, Nationality n) =>
        [.. CrowdDress.Problems(n), .. n.Id.StartsWith(pack.Id + ".", StringComparison.Ordinal) ? Array.Empty<string>() : [$"its id starts with {pack.Id}."]];

    public static void Open(PieceStudio s)
    {
        var dlg = new AcceptDialog { Title = "Nationalities", OkButtonText = "Done" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        dlg.AddChild(box);
        var form = Ui.Form(Ui.DialogLabels);
        box.AddChild(form);
        var shipped = Nationalities.All.Select(n => (n.Id, n.Name ?? n.Id, "", (Texture2D?)null)).ToList();
        string copyFrom = shipped[0].Id;
        var copyRow = new HBoxContainer();
        copyRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        copyRow.AddChild(Ui.Picker(shipped, copyFrom, k => copyFrom = k));
        var own = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var record = new TextEdit { CustomMinimumSize = Ui.Px(560, 300), WrapMode = TextEdit.LineWrappingMode.None };
        void List(string? pick)
        {
            own.Clear();
            var mine = s.Pack.Nationalities ?? [];
            foreach (var n in mine) own.AddItem(n.Name ?? n.Id);
            int at = Math.Max(0, mine.FindIndex(n => n.Id == pick));
            if (mine.Count > 0) own.Select(at);
            record.Text = mine.Count > 0 ? Json.Serialize(mine[at], indented: true) : "";
            record.Editable = mine.Count > 0;
        }
        own.ItemSelected += at => record.Text = Json.Serialize(s.Pack.Nationalities![(int)at], indented: true);
        copyRow.AddChild(Ui.Btn("Copy", () =>
        {
            var made = Copy(s.Pack, copyFrom);
            s.SavePack();
            List(made.Id);
        }, "A copy of it in this pack to change"));
        form.AddChild(new Label { Text = "Copy" });
        form.AddChild(copyRow);
        form.AddChild(new Label { Text = "This pack's" });
        form.AddChild(own);
        Ui.AlignForm(form);
        box.AddChild(record);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        buttons.AddChild(Ui.Btn("Save", () =>
        {
            Nationality? n = null;
            try { n = Json.Deserialize<Nationality>(record.Text); }
            catch (System.Text.Json.JsonException e) { s.Flash("The record does not read: " + e.Message); }
            if (n == null) return;
            if (Problems(s.Pack, n) is { Count: > 0 } problems)
            {
                s.Flash(string.Join("; ", problems.Take(3)));
                return;
            }
            Put(s.Pack, n);
            s.SavePack();
            List(n.Id);
            s.Flash($"Saved {n.Name ?? n.Id}");
        }, "Keep the record as written"));
        buttons.AddChild(Ui.Btn("Remove", () =>
        {
            if (s.Pack.Nationalities is not { Count: > 0 } mine || own.Selected < 0) return;
            mine.RemoveAt(own.Selected);
            if (mine.Count == 0) s.Pack.Nationalities = null;
            s.SavePack();
            List(null);
        }, "Take it out of this pack"));
        box.AddChild(buttons);
        List(null);
        Ui.Open(s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }
}
