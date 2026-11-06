using System.Linq;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class InviteDialog : HudPart
{
    InviteDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c) => new InviteDialog(c).Show();


    void Show()
    {
        var dlg = new AcceptDialog { Title = "Invite", OkButtonText = "Copy invitation" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        box.AddThemeConstantOverride("separation", Ui.Gap.Group);
        box.AddChild(Header("Where they connect"));
        var lan = Net.LanAddresses();
        var port = Cfg.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string? lanAt = lan.Count > 0 ? $"{lan[0]}  port {port}" : null;
        box.AddChild(Fact("Your network", lanAt ?? $"port {port} (UDP)", lanAt != null ? Copy(lanAt) : null));
        string? netAt = N.UpnpStatus == "ok" && N.ExternalAddress != null ? $"{N.ExternalAddress}  port {port}" : null;
        box.AddChild(Fact("Internet", N.UpnpStatus switch
        {
            "ok" => netAt ?? $"port {port}",
            "trying" => "asking your router to open the port…",
            _ => $"{(N.ExternalAddress != null ? N.ExternalAddress + "  " : "")}port {port} not open",
        }, netAt != null ? Copy(netAt) : null));
        if (N.UpnpStatus == "trying") box.AddChild(Hint("Open this window again in a moment."));
        else if (N.UpnpStatus != "ok")
            box.AddChild(Hint($"Your router did not open the port. On its admin page, forward UDP {port} to this PC, then give people your public address."));
        box.AddChild(Header("Joining"));
        var pwRow = PasswordRow(Ctx, dlg);
        box.AddChild(pwRow);
        var pwField = pwRow.GetChild<LineEdit>(1);
        box.AddChild(Hint("Whoever knocks waits until you or a lead let them in, from the card under the top bar or the people at the top right; once in, their PC comes straight back next time."));
        if (S.IsHost) AddPacks(box, dlg);
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            var at = netAt ?? lanAt ?? $"port {port}";
            var pw = pwField.Text.Trim() is { Length: > 0 } p ? p : "none";
            AppClipboard.SetText($"Kriegsspiel Table · {S.View.Name}\nJoin a game → {at} · password: {pw}");
        };
        Ui.Open(HudNode, dlg, new Vector2I(520, 80));
        Ui.FreeOnClose(dlg);
    }

    void AddPacks(VBoxContainer box, AcceptDialog dlg)
    {
        var packs = S.View.Packs.Where(id => !StandardPacks.IsStandard(id)).Select(id => S.Store.Pack(id)).OfType<KTable.Core.Model.PackManifest>().ToList();
        if (packs.Count == 0) return;
        box.AddChild(Header("Packs"));
        foreach (var pack in packs)
            box.AddChild(Fact(pack.Name, $"{pack.Pieces.Count} piece{(pack.Pieces.Count == 1 ? "" : "s")}", Btn("Export…", () => { dlg.Hide(); ExportPack(pack.Id); }, "Save this pack as a .ktpack file to post before the game")));
        box.AddChild(Hint("Posted before a big game, they spare your upload: whoever installs them first downloads almost nothing from you at the door."));
    }

    void ExportPack(string id)
    {
        var dlg = new ExportDialog(S.Store, "pack", id);
        dlg.Done += Say;
        Ui.Open(HudNode, dlg);
    }

    static Button Copy(string text) => Btn("Copy", () => AppClipboard.SetText(text.Replace("  port ", ":")), "Copy the address and port");

    static HBoxContainer Fact(string name, string value, Button? copy) => Ui.Fact(name, value, Ui.DialogLabels, copy);


    public static Control PasswordRow(HudContext c, Window owner)
    {
        var S = c.S;
        var N = c.N;
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", Ui.Gap.Group);
        r.AddChild(Ui.RowLabel("Password", Ui.DialogLabels));
        var field = new LineEdit { Text = S.Access?.Password ?? "", Editable = S.IsLead, PlaceholderText = S.IsLead ? "none: the door is open" : "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        r.AddChild(field);
        r.AddChild(Btn("Copy", () => AppClipboard.SetText(field.Text), "Copy the password"));
        if (S.IsLead)
        {
            var sent = field.Text;
            void Commit()
            {
                if (!GodotObject.IsInstanceValid(field) || field.Text.Trim() == sent) return;
                sent = field.Text.Trim();
                N.Door(new SetPassword { Password = sent });
            }
            field.TextSubmitted += _ => Commit();
            field.FocusExited += Commit;
            owner.VisibilityChanged += () => { if (!owner.Visible) Commit(); };
        }
        return r;
    }
}
