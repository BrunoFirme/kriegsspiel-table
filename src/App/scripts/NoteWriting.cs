using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KTable.Core.Model;

namespace KTable.App;

static class NoteWriting
{
    public readonly record struct Stroke(string Text, MarkupRun Style, float Left, float Wide);

    static readonly MarkupRun Unmarked = new("", false, false, false, false, MarkupLevel.Line, null);

    public static List<MarkupRun> Raw(string text) => new() { Unmarked with { Text = text } };

    public static List<List<Stroke>> Lines(IEnumerable<MarkupRun> runs, float width, Func<string, MarkupRun, float> wide)
    {
        var lines = new List<List<Stroke>>();
        foreach (var paragraph in Paragraphs(runs)) lines.AddRange(Wrapped(paragraph, width, wide));
        return lines;
    }

    static List<List<List<MarkupRun>>> Paragraphs(IEnumerable<MarkupRun> runs)
    {
        var paragraphs = new List<List<List<MarkupRun>>> { new() { new() } };
        var letters = new StringBuilder();
        foreach (var run in runs)
        {
            void Close()
            {
                if (letters.Length > 0) paragraphs[^1][^1].Add(run with { Text = letters.ToString() });
                letters.Clear();
            }
            foreach (char c in run.Text.Replace("\r", ""))
            {
                if (c is not (' ' or '\n'))
                {
                    letters.Append(c);
                    continue;
                }
                Close();
                if (c == '\n') paragraphs.Add(new() { new() });
                else paragraphs[^1].Add(new());
            }
            Close();
        }
        return paragraphs;
    }

    static IEnumerable<List<Stroke>> Wrapped(List<List<MarkupRun>> words, float width, Func<string, MarkupRun, float> wide)
    {
        float space = wide(" ", Unmarked), used = 0;
        var line = new List<List<MarkupRun>>();
        foreach (var word in words.SelectMany(w => Fitting(w, width, wide)))
        {
            float across = word.Sum(f => wide(f.Text, f));
            if (line.Count > 0 && used + space + across > width)
            {
                yield return Laid(line, wide);
                line = new();
                used = 0;
            }
            used += (line.Count > 0 ? space : 0) + across;
            line.Add(word);
        }
        yield return Laid(line, wide);
    }

    static IEnumerable<List<MarkupRun>> Fitting(List<MarkupRun> word, float width, Func<string, MarkupRun, float> wide)
    {
        if (word.Sum(f => wide(f.Text, f)) <= width)
        {
            yield return word;
            yield break;
        }
        var part = new List<MarkupRun>();
        float used = 0;
        foreach (var f in word)
        {
            int from = 0;
            for (int to = 1; to <= f.Text.Length; to++)
            {
                if (used + wide(f.Text[from..to], f) <= width || to - from == 1 && part.Count == 0) continue;
                if (to - 1 > from) part.Add(f with { Text = f.Text[from..(to - 1)] });
                yield return part;
                part = new();
                used = 0;
                from = to - 1;
            }
            part.Add(f with { Text = f.Text[from..] });
            used += wide(f.Text[from..], f);
        }
        yield return part;
    }

    static List<Stroke> Laid(List<List<MarkupRun>> words, Func<string, MarkupRun, float> wide)
    {
        var joined = new List<MarkupRun>();
        void Join(MarkupRun piece)
        {
            if (joined.Count > 0 && joined[^1] with { Text = "" } == piece with { Text = "" }) joined[^1] = piece with { Text = joined[^1].Text + piece.Text };
            else joined.Add(piece);
        }
        string gap = "";
        for (int w = 0; w < words.Count; w++)
        {
            if (w > 0) gap += " ";
            foreach (var piece in words[w])
            {
                bool between = gap.Length > 0 && joined.Count > 0 && joined[^1] with { Text = "" } != piece with { Text = "" };
                if (between) Join(Unmarked with { Text = gap });
                Join(piece with { Text = (between ? "" : gap) + piece.Text });
                gap = "";
            }
        }
        var strokes = new List<Stroke>();
        float left = 0;
        foreach (var piece in joined)
        {
            float across = wide(piece.Text, piece);
            strokes.Add(new Stroke(piece.Text, piece, left, across));
            left += across;
        }
        return strokes;
    }
}
