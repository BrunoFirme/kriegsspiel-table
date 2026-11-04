using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using KTable.Client.Docs;

namespace KTable.App;

public static class TextFind
{
    static readonly string HitBg = "#" + new Color(Ui.Accent, 0.32f).ToHtml(true);
    static readonly string CurrentBg = "#" + Ui.AccentInk.ToHtml(false);
    static readonly string CurrentInk = "#" + Ui.OnAccent.ToHtml(false);

    public static string Mark(string bbcode, IReadOnlyList<string> terms, int current, ref int index, List<int> paragraphs)
    {
        if (terms.Count == 0) return bbcode;
        var sb = new StringBuilder(bbcode.Length + 64);
        int para = 0, i = 0;
        while (i < bbcode.Length)
        {
            if (bbcode[i] == '[')
            {
                int close = bbcode.IndexOf(']', i);
                if (close > i)
                {
                    sb.Append(bbcode, i, close - i + 1);
                    i = close + 1;
                    continue;
                }
            }
            int next = bbcode.IndexOf('[', i + 1);
            if (next < 0) next = bbcode.Length;
            var run = bbcode[i..next];
            int at = 0;
            foreach (var (start, length) in DocSearch.Matches(run, terms))
            {
                sb.Append(run, at, start - at);
                bool cur = index == current;
                paragraphs.Add(para + Count(run, '\n', 0, start));
                sb.Append("[bgcolor=").Append(cur ? CurrentBg : HitBg).Append(']');
                if (cur) sb.Append("[color=").Append(CurrentInk).Append(']');
                sb.Append(run, start, length);
                if (cur) sb.Append("[/color]");
                sb.Append("[/bgcolor]");
                at = start + length;
                index++;
            }
            sb.Append(run, at, run.Length - at);
            para += Count(run, '\n', 0, run.Length);
            i = next;
        }
        return sb.ToString();
    }

    static int Count(string s, char c, int from, int to)
    {
        int n = 0;
        for (int k = from; k < to; k++) if (s[k] == c) n++;
        return n;
    }

    public static int Matches(string bbcode, IReadOnlyList<string> terms)
    {
        int index = 0;
        Mark(bbcode, terms, -1, ref index, new List<int>());
        return index;
    }
}

public partial class FindBar : HBoxContainer
{
    readonly LineEdit _field = new() { PlaceholderText = "Find on this page  (Ctrl+F)", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(220, 0) };
    readonly Label _count = new() { Modulate = Ui.Muted, CustomMinimumSize = new Vector2(64, 0), HorizontalAlignment = HorizontalAlignment.Center };
    readonly Timer _debounce = new() { OneShot = true, WaitTime = 0.12 };

    public event Action? Changed;
    public event Action? Moved;

    public IReadOnlyList<string> Terms { get; private set; } = Array.Empty<string>();
    public int Current { get; private set; }
    public int Total { get; private set; }

    public FindBar()
    {
        AddThemeConstantOverride("separation", 2);
        AddChild(_field);
        AddChild(_count);
        AddChild(Ui.Bare(Ui.Btn("‹", () => Step(-1), "Previous match (Shift+Enter)")));
        AddChild(Ui.Bare(Ui.Btn("›", () => Step(1), "Next match (Enter)")));
        AddChild(_debounce);
        _debounce.Timeout += Apply;
        _field.TextChanged += _ => _debounce.Start();
        _field.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } k) return;
            if (k.Keycode is Key.Enter or Key.KpEnter)
            {
                if (_debounce.TimeLeft > 0) { _debounce.Stop(); Apply(); } Step(k.ShiftPressed ? -1 : 1);
                _field.AcceptEvent();
            }
            else if (k.Keycode == Key.Escape) { Close(); _field.AcceptEvent(); }
        };
    }

    public void Open(string? query = null)
    {
        Visible = true;
        if (query != null && query != _field.Text) { _field.Text = query; Apply(); }
        _field.GrabFocus();
        _field.SelectAll();
    }

    public void Close()
    {
        if (_field.Text.Length == 0 && Terms.Count == 0) return;
        _field.Text = "";
        Apply();
    }

    void Apply()
    {
        Terms = DocSearch.Terms(_field.Text);
        Current = 0;
        Changed?.Invoke();
    }

    public void SetTotal(int total)
    {
        Total = total;
        if (Current >= total) Current = Math.Max(0, total - 1);
        _count.Text = Terms.Count == 0 ? "" : total == 0 ? "None" : $"{Current + 1} / {total}";
    }

    public void SetCurrent(int current)
    {
        Current = Math.Clamp(current, 0, Math.Max(0, Total - 1));
        SetTotal(Total);
    }

    void Step(int by)
    {
        if (Total == 0) return;
        Current = ((Current + by) % Total + Total) % Total;
        SetTotal(Total);
        Moved?.Invoke();
    }

    public bool Handles(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return false;
        if (k.Keycode == Key.F && k.CtrlPressed) { Open(); return true; }
        if (k.Keycode == Key.F3)
        {
            if (Terms.Count == 0) Open();
            else Step(k.ShiftPressed ? -1 : 1);
            return true;
        }
        return false;
    }

    public static FindBar ForLabel(RichTextLabel label, Func<string> source)
    {
        var bar = new FindBar();
        void Render(bool scroll)
        {
            int index = 0;
            var paras = new List<int>();
            label.Text = TextFind.Mark(source(), bar.Terms, bar.Current, ref index, paras);
            bar.SetTotal(index);
            if (scroll && paras.Count > 0) label.ScrollToParagraph(paras[Math.Min(bar.Current, paras.Count - 1)]);
        }
        bar.Changed += () => Render(true);
        bar.Moved += () => Render(true);
        bar.SetMeta("render", Callable.From(() => Render(false)));
        return bar;
    }

    public void Refresh()
    {
        if (HasMeta("render")) GetMeta("render").AsCallable().Call();
    }
}
