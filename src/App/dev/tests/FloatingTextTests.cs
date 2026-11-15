using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class FloatingTextTests
{
    const string U = Layers.Umpire;
    const double Up = 0.8, Ahead = 0.3, Letters = 0.3;

    static void Unit(PackageStore store)
    {
        var pack = new PackManifest { Id = "labels", Name = "Labels" };
        store.RegisterDef(pack, new PieceDef
        {
            Id = "unit", Name = "Battalion", Size = new Size2(2, 1), Thickness = 0.2,
            TextLines = new()
            {
                new TextLineDef { Name = "Name", Default = "1st Bn", Float = new FloatPlace { Y = Up, Z = -Ahead, Size = Letters } },
                new TextLineDef { Name = "Note", Default = "kept to itself" },
            },
        });
    }

    static void Place(GameState g)
    {
        g.Layers[U].Add(new PieceInstance { Id = "a", Def = "unit", Pos = new Vec2(-4, 0) });
        g.Layers[U].Add(new PieceInstance { Id = "b", Def = "unit", Pos = new Vec2(4, 0), Text = { ["Name"] = "2nd Bn" } });
    }

    static List<Label3D> Labels(PieceNode n) => n.GetChildren().OfType<Label3D>().ToList();

    [ScreenTest]
    static async Task A_floating_text_line_stands_over_each_piece_in_that_piece_s_own_words_and_goes_when_it_is_blank_or_face_down(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2, prepare: Unit, arrange: Place);
        await r.Frames(10);
        var a = t.Node("a");
        var b = t.Node("b");
        Check.Equal("1st Bn", Labels(a).Single().Text, "a piece with no words of its own floats the line's start");
        Check.Equal("2nd Bn", Labels(b).Single().Text, "a piece with its own words floats those");
        var la = Labels(a).Single();
        var off = la.GlobalPosition - a.GlobalPosition;
        Check.That(Mathf.IsEqualApprox(off.Y, (float)Up, 0.01f), $"it floats as high as set: {off.Y}");
        Check.That(Mathf.IsEqualApprox(new Vector2(off.X, off.Z).Length(), (float)Ahead, 0.01f), $"and as far ahead: {off}");
        Check.That(la.Billboard == BaseMaterial3D.BillboardModeEnum.Enabled, "it turns to face whoever looks");
        float tall = la.PixelSize * (la.Font ?? ThemeDB.FallbackFont).GetHeight(la.FontSize);
        Check.That(Mathf.IsEqualApprox(tall, (float)Letters, 0.01f), $"its letters stand as tall as set: {tall}");

        t.S.Do(new SetText { Layer = U, Id = "b", Name = "Name", Value = "  " });
        await r.Frames(5);
        Check.Equal(0, Labels(t.Node("b")).Count, "blank words float nothing");

        t.S.Do(new FlipPiece { Layer = U, Id = "a", FaceDown = true });
        await r.Frames(5);
        Check.Equal(0, Labels(t.Node("a")).Count, "a piece turned face down keeps its words to itself");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    static Label3D? Shown(ModelStudio m) => m.Stand.FindChildren("*", nameof(Label3D), true, false).OfType<Label3D>().FirstOrDefault();

    [ScreenTest]
    static async Task Text_floated_in_the_studio_shows_over_the_model_follows_its_numbers_and_comes_down_again(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece("Grenadier", ModelImport.Primitive("box"), "box");
        var m = h.Studio.Model!;
        var d = h.Studio.Sel!;
        m.FloatLabel(-1);
        await r.Frames(2);
        Check.Equal(1, d.TextLines.Count, "a new text line floats");
        var line = d.TextLines[0];
        Check.Equal(("Label", "Grenadier"), (line.Name, line.Default), "named as a label, reading the kind's name");
        Check.That(line.Float!.Y > d.Thickness, $"it floats over the model: {line.Float.Y} over {d.Thickness}");
        Check.Equal(0, m.SelLabel, "and is the one chosen");
        Check.That(Shown(m) is { Text: "Grenadier" }, "the 3D view shows it");
        m.SetLabel(0, t => t.Float!.Size = 0.5, "the size");
        await r.Frames(2);
        var shown = Shown(m)!;
        float tall = shown.PixelSize * (shown.Font ?? ThemeDB.FallbackFont).GetHeight(shown.FontSize) * shown.GlobalTransform.Basis.Scale.Y;
        Check.That(Mathf.IsEqualApprox(tall, 0.5f, 0.02f), $"it grows as the size is set: {tall}");
        m.GroundLabel(0);
        await r.Frames(2);
        Check.That(d.TextLines.Count == 1 && d.TextLines[0].Float == null, "taken off, the text line stays, carried but not floating");
        Check.That(Shown(m) == null, "and the 3D view shows no text");
    }
}
