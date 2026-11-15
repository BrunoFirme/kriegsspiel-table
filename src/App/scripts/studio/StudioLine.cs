using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public partial class PieceStudio
{
    Task? _asking;
    KindLine? _several;

    internal bool Several { get; private set; }

    internal KindLine? Line => InPlace?.Line ?? _several;

    internal int SeveralPicked => T.SelectedNodes.Select(n => n.Piece.Def).Where(id => _pack.Pieces.Any(d => d.Id == id)).Distinct().Count();

    internal void ToggleSeveral()
    {
        if (InPlace != null) return;
        Several = !Several;
        _several = null;
        Paper.LayOutAll(force: true);
        RebuildList();
        if (Several)
        {
            SetView("table");
            Paper.FrameAll();
        }
        RefreshHint();
        Top.ShowLine();
    }

    internal async void LineUp()
    {
        try { await LineUpAsync(); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    internal async Task LineUpAsync()
    {
        if (!Several) return;
        var picked = T.SelectedNodes.Select(n => n.Piece.Def).ToHashSet();
        var kinds = _pack.Pieces.Where(d => picked.Contains(d.Id)).ToList();
        if (kinds.Count < 2) return;
        _several = new KindLine(kinds.Select(d => new LinePiece(Layers.Umpire, d.Id, d.Id, "")));
        _several.MoveTo(0, _pack);
        T.SetSelection(Paper.Instance(kinds[0]) is { } first ? new[] { first.Id } : System.Array.Empty<string>());
        Top.ShowLine();
        if (_view == "table") SetView("model");
        await SelectAsync(kinds[0], frame: true);
    }

    internal void EndLine()
    {
        _several = null;
        Top.ShowLine();
    }

    internal void Step(int by)
    {
        if (Line is { IsLine: true } line) _ = GoTo(line.At + by);
    }

    internal async Task GoTo(int index)
    {
        if (Line is not { IsLine: true } line || _leaving || _gone) return;
        if (await SettleWork() is { } problem) { Flash("Not moved on: " + problem.Replace("\n", "; ")); return; }
        FlushPack();
        if (_asking is { } asking) await asking;
        if (_gone || _leaving || InPlace is { Ended: true } || Line != line) return;
        if (InPlace is { } place) place.MoveTo(index, _pack);
        else line.MoveTo(index, _pack);
        Top.ShowLine();
        if (_pack.Pieces.Find(d => d.Id == line.Current.Kind) is { } d) await SelectAsync(d, frame: true);
        RefreshHint();
    }

    void CheckLine(PackManifest pack)
    {
        if (InPlace is not { Ended: true } && Line is { } line && line.FirstEdit(pack)) AskKind();
    }

    internal void AskKind()
    {
        if (_asking == null && Line is { } line) _asking = Answer(line);
    }

    async Task Answer(KindLine line)
    {
        try
        {
            await Frames(1);
            var item = line.Current;
            if (_gone || item.Answered || _pack.Pieces.Find(d => d.Id == item.Kind) is not { } d) return;
            var answer = line.Rest ?? await Question(line, d);
            if (_gone || InPlace is { Ended: true } || Line != line) return;
            if (await SettleWork() is { } problem) { Flash("Not kept apart: " + problem.Replace("\n", "; ")); return; }
            if (_gone || InPlace is { Ended: true } || Line != line) return;
            if (answer == LineAnswer.Own) Split(line, item, d);
            else if (answer == LineAnswer.Undo) Back(item, d);
            else item.Answered = true;
        }
        catch (Exception e) { GD.PushError(e.ToString()); }
        finally { _asking = null; }
    }

    Task<LineAnswer> Question(KindLine line, PieceDef d)
    {
        var tcs = new TaskCompletionSource<LineAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rest = line.Unanswered(line.Current) ? new CheckBox { Text = "Same for the rest of the line" } : null;
        void Done(LineAnswer a)
        {
            if (rest?.ButtonPressed == true && a != LineAnswer.Undo) line.Rest = a;
            tcs.TrySetResult(a);
        }
        int sharing = InPlace?.Sharing(d.Id) ?? 0;
        var own = InPlace != null ? "New kind for this piece" : "Save as a new kind";
        var share = InPlace == null ? "Change this kind" : sharing > 1 ? $"Change all {sharing}" : "Change the kind";
        var q = KTable.App.InPlace.Ask(_root, $"Change {d.Name}?", own, () => Done(LineAnswer.Own), null, rest, width: 420);
        q.Also(share, () => Done(LineAnswer.Share));
        q.CancelButtonText = "Undo the change";
        q.Canceled += () => Done(LineAnswer.Undo);
        q.Closed += () => tcs.TrySetResult(LineAnswer.Undo);
        return tcs.Task;
    }

    void Split(KindLine line, KindLine.Item item, PieceDef d)
    {
        var copy = Json.Clone(d);
        copy.Name = CopyName(line, item, d);
        copy.Id = PackEdit.NewDefId(_pack, copy.Name);
        Back(item, d);
        _pack.Pieces.Insert(_pack.Pieces.IndexOf(d) + 1, copy);
        Store.RegisterDef(_pack, copy);
        KindLine.Took(item, copy.Id);
        SavePack();
        Paper.LayOutAll(force: InPlace != null);
        RebuildList();
        Top.ShowLine();
        Select(copy, frame: false);
    }

    string CopyName(KindLine line, KindLine.Item item, PieceDef d)
    {
        if (InPlace != null) return $"{d.Name} · {(item.Name.Length > 0 ? item.Name : (line.At + 1).ToString())}";
        return Json.Deserialize<PieceDef>(item.Base)?.Name == d.Name ? d.Name + " copy" : d.Name;
    }

    void Back(KindLine.Item item, PieceDef d)
    {
        foreach (var (hash, asset) in item.BaseAssets) _pack.Assets.TryAdd(hash, asset);
        if (Json.Deserialize<PieceDef>(item.Base) is { } was) PieceHistory.Wear(this, d, was);
        Edits.Clear();
        Strip.RefreshUndo();
    }

    internal void Reorder(string kind, string? before)
    {
        int from = _pack.Pieces.FindIndex(d => d.Id == kind);
        if (from < 0 || kind == before) return;
        var was = Order();
        var d = _pack.Pieces[from];
        int to = before == null ? _pack.Pieces.Count - 1 : _pack.Pieces.FindIndex(p => p.Id == before);
        if (to < 0) return;
        _pack.Pieces.RemoveAt(from);
        _pack.Pieces.Insert(Math.Min(to, _pack.Pieces.Count), d);
        Edits.Ordered("Order", was, Order());
        Strip.RefreshUndo();
        Ordered();
    }

    string Order() => string.Join("\n", _pack.Pieces.Select(d => d.Id));

    internal void PutInOrder(string order)
    {
        var rank = order.Split('\n').Select((id, i) => (id, i)).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First().i);
        var sorted = _pack.Pieces.OrderBy(d => rank.GetValueOrDefault(d.Id, int.MaxValue)).ToList();
        _pack.Pieces.Clear();
        _pack.Pieces.AddRange(sorted);
        Ordered();
    }

    void Ordered()
    {
        SavePack();
        RebuildList();
        if (Several) Paper.LayOutAll(force: true);
    }
}
