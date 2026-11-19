using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class CrowdsPanel
{
    static readonly (string Name, double Men)[] Sexes = [("Men", 1), ("Women", 0), ("Both", 0.5)];
    static readonly (string Name, double Children, double Elders)[] Ages = [("Adults", 0, 0), ("With children", 0.3, 0), ("With elders", 0, 0.25), ("All ages", 0.25, 0.12)];
    static readonly string?[] Purses = [null, .. CrowdBaseline.Wealths];

    readonly MenuButton _add = Picker("Crowd ▾", "Mob, townsfolk, soldiers, pilgrims or refugees, of a nationality and recipe"),
        _share = Picker("All ▾", "How many of the people in a box are picked"),
        _calling = Picker("Every calling ▾", "Only people of this calling are picked"),
        _pace = Picker("Walk ▾", "How fast the picked people go to an order");
    readonly Label _picked = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    static MenuButton Picker(string text, string tip) => new() { Text = text, Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };

    GridContainer ToolRows()
    {
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Add" });
        form.AddChild(Left(_add));
        form.AddChild(new Label { Text = "Pick" });
        var pick = new HBoxContainer();
        pick.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        pick.AddChild(_share);
        pick.AddChild(_calling);
        form.AddChild(pick);
        form.AddChild(new Label { Text = "Picked" });
        form.AddChild(_picked);
        form.AddChild(new Label { Text = "Pace" });
        form.AddChild(Left(_pace));
        Ui.AlignForm(form);
        Menus();
        T.Crowds.Tool.Changed += SyncTool;
        Panel.TreeExiting += () => T.Crowds.Tool.Changed -= SyncTool;
        var tool = T.Crowds.Tool;
        tool.Take = (id, at) => T.Walk.TakeControl(id, at);
        tool.Assign = (name, at, yaw, id) => N.Place(name, at, yaw, id);
        tool.Dress = (key, look, name) => DressDialog.Person(Ctx, key, look, name);
        return form;
    }

    static HBoxContainer Left(Control c)
    {
        var row = new HBoxContainer();
        row.AddChild(c);
        return row;
    }

    void Menus()
    {
        var mixes = _add.GetPopup();
        var peoples = new PopupMenu();
        List<string?> offered = [];
        mixes.AboutToPopup += () =>
        {
            mixes.Clear(true);
            for (int i = 0; i < MadeMixes.All.Count; i++) mixes.AddItem(MadeMixes.All[i].Name, i);
            peoples = new PopupMenu();
            var own = S.View.Packs.Select(p => S.Store.Pack(p)).OfType<PackManifest>().ToList();
            offered = [null, .. Nationalities.Offered(own).Select(n => n.Id)];
            for (int i = 0; i < offered.Count; i++)
            {
                var n = offered[i] is { } id ? Nationalities.Of(own).FirstOrDefault(x => x.Id == id) : null;
                peoples.AddRadioCheckItem(n == null ? "Of the place" : n.Name ?? n.Id, i);
                peoples.SetItemChecked(i, offered[i] == T.Crowds.Tool.AddPeople);
            }
            peoples.IdPressed += id => Crowd(t => t.AddPeople = offered[(int)id]);
            mixes.AddSeparator();
            mixes.AddSubmenuNodeItem("Nationality", peoples);
            Recipe(mixes);
        };
        mixes.IdPressed += id => Crowd(t => t.Add(MadeMixes.All[(int)id].Id));
        var shares = _share.GetPopup();
        for (int i = 0; i < CrowdTool.Shares.Count; i++) shares.AddItem(CrowdTool.Shares[i].Name, i);
        shares.IdPressed += id => Crowd(t => t.Share = CrowdTool.Shares[(int)id].Share);
        var paces = _pace.GetPopup();
        for (int i = 0; i < CrowdPaces.All.Count; i++) paces.AddItem(Capital(CrowdPaces.All[i].Id), i);
        paces.IdPressed += id => Crowd(t => t.Pace = CrowdPaces.All[(int)id].Id);
        var callings = _calling.GetPopup();
        callings.AboutToPopup += () =>
        {
            callings.Clear();
            callings.AddItem("Every calling", 0);
            foreach (var (calling, k) in T.Crowds.Tool.Callings().Select((c, k) => (c, k))) callings.AddItem(Capital(calling), k + 1);
        };
        callings.IdPressed += id => Crowd(t => t.Calling = id == 0 ? null : T.Crowds.Tool.Callings().ElementAtOrDefault((int)id - 1));
    }

    void Recipe(PopupMenu mixes)
    {
        var recipe = T.Crowds.Tool.Recipe;
        var flat = T.Crowds.Tool.AddPeople is { } id && CrowdDress.Find(id, Nationalities.Of(S.View.Packs.Select(p => S.Store.Pack(p)).OfType<PackManifest>())) is { } n
            ? CrowdDress.Flat(n) : Nationalities.Baseline;
        var callings = (flat.Callings ?? new()).Keys.Where(k => k.StartsWith("m:")).Select(k => k[2..]).Distinct().ToList();
        Submenu(mixes, "Callings", callings.Select(k => (Capital(CrowdDress.CallingName(flat, k)), recipe.Callings.ContainsKey(k))), true, i =>
        {
            if (!recipe.Callings.Remove(callings[i])) recipe.Callings[callings[i]] = 1;
        });
        Submenu(mixes, "Wealth", Purses.Select(w => (w == null ? "Mixed" : Capital(w), recipe.Wealth == w)), false, i => recipe.Wealth = Purses[i]);
        Submenu(mixes, "Sex", Sexes.Select(x => (x.Name, Math.Abs(recipe.Men - x.Men) < 0.01)), false, i => recipe.Men = Sexes[i].Men);
        Submenu(mixes, "Ages", Ages.Select(x => (x.Name, Math.Abs(recipe.Children - x.Children) < 0.01 && Math.Abs(recipe.Elders - x.Elders) < 0.01)), false,
            i => (recipe.Children, recipe.Elders) = (Ages[i].Children, Ages[i].Elders));
        Submenu(mixes, "Appear", [("Here", !recipe.Edge), ("At the edge", recipe.Edge)], false, i => recipe.Edge = i == 1);
    }

    static void Submenu(PopupMenu parent, string name, IEnumerable<(string Name, bool On)> rows, bool many, Action<int> pick)
    {
        var menu = new PopupMenu { HideOnCheckableItemSelection = !many };
        int i = 0;
        foreach (var (row, on) in rows)
        {
            if (many) menu.AddCheckItem(row, i);
            else menu.AddRadioCheckItem(row, i);
            menu.SetItemChecked(i++, on);
        }
        menu.IdPressed += id =>
        {
            pick((int)id);
            if (many) menu.SetItemChecked((int)id, !menu.IsItemChecked((int)id));
        };
        parent.AddSubmenuNodeItem(name, menu);
    }

    void Crowd(Action<CrowdTool> set)
    {
        if (T.Tool != Tool.Crowds) T.SetTool(Tool.Crowds);
        set(T.Crowds.Tool);
        SyncTool();
        T.RefreshHint();
    }

    void SyncTool()
    {
        if (!GodotObject.IsInstanceValid(_picked)) return;
        var tool = T.Crowds.Tool;
        _picked.Text = tool.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _share.Text = CrowdTool.Shares.First(s => s.Share == tool.Share).Name + " ▾";
        _calling.Text = (tool.Calling is { } calling ? Capital(calling) : "Every calling") + " ▾";
        _pace.Text = Capital(tool.Pace) + " ▾";
    }
}
