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
        var (lanAt, netAt, port) = Where(N, Cfg);
        box.AddChild(Fact("Your network", lanAt ?? $"port {port} (UDP)", lanAt != null ? Copy(lanAt) : null));
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
        dlg.Confirmed += () => AppClipboard.SetText(Invitation(S.View.Name, netAt ?? lanAt ?? $"port {port}", pwField.Text.Trim()));
        Ui.Open(HudNode, dlg, new Vector2I(520, 80));
        Ui.FreeOnClose(dlg);
    }

    internal static (string? Lan, string? Internet, string Port) Where(Net n, Settings cfg)
    {
        var lan = Net.LanAddresses();
        var port = cfg.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (lan.Count > 0 ? $"{lan[0]}  port {port}" : null, n.UpnpStatus == "ok" && n.ExternalAddress != null ? $"{n.ExternalAddress}  port {port}" : null, port);
    }

    internal static string Invitation(string game, string at, string password) =>
        $"Kriegsspiel Table · {game}\nJoin a game → {at} · password: {(password.Length > 0 ? password : "none")}";

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
        var r = AgentFence.Mark(new HBoxContainer());
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
