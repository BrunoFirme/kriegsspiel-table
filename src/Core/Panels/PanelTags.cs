using KTable.Core.Model;

namespace KTable.Core.Panels;

public static class PanelTags
{
    public const string Text = "Text", Dropdown = "Dropdown", Button = "Button", ToggleButton = "ToggleButton", Toggle = "Toggle", Panel = "Panel";

    public static readonly string[] All =
    {
        "Accordion", "AccordionSection", "AccordionSectionContent", "AccordionSectionHeader", "AccordionSectionHeaderIcon", "Animation", Button, "Canvas", "Cell",
        "ChildXmlLayout", "Color", "DataTable", "DataTableCellTemplate", "DataTableRowTemplate", Dropdown, "GridLayout", "HorizontalLayout", "HorizontalScrollView", "Image",
        "InputField", "Mask", Panel, "ProgressBar", "RawImage", "Row", "Slider", "SVGImage", "TableLayout", "TextMeshProDropdown", "TextMeshProInputField", "TextMeshProMaterial",
        "TextMeshPro", ToggleButton, "ToggleGroup", Toggle, "VerticalLayout", "VerticalScrollView", "XmlLayout", "TMP_DropdownLabel", "TMP_Option", "TMP_OptionTextTemplate",
        "TMP_Placeholder", "TMP_Text", Text,
    };

    static readonly string[] Unrendered = { "Animation", "Color", "DataTableCellTemplate", "DataTableRowTemplate", "TMP_DropdownLabel", "TMP_Option", "TMP_OptionTextTemplate" };

    public static string? Find(string name) => All.FirstOrDefault(t => t.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static bool ReadsOwnChildren(string tag) => tag is Text or Dropdown;

    public static bool Renders(string tag) => !Unrendered.Contains(tag);

    public static bool HasFormValue(string tag) => tag is Dropdown or "InputField" or "Slider" or ToggleButton or "ToggleGroup" or Toggle or "TextMeshProDropdown" or "TextMeshProInputField";

    public static void AfterApply(UiElement e, IReadOnlyDictionary<string, string> applied)
    {
        switch (e.Tag)
        {
            case Panel when e.Attributes.ContainsKey("image") && !e.Attributes.ContainsKey("raycastTarget"):
                PanelOps.SetOwn(e, "raycastTarget", "true");
                break;
            case Button or ToggleButton when e.Children.Count > 0 && applied.TryGetValue("text", out var text) && !e.Children[0].Attributes.ContainsKey("text"):
                PanelOps.SetAndApply(e.Children[0], "text", text);
                break;
            case Toggle when e.Children.Count > 0:
                ToggleChild(e, applied);
                break;
        }
    }

    static void ToggleChild(UiElement e, IReadOnlyDictionary<string, string> applied)
    {
        var child = e.Children[0];
        var add = UiAttributes.Fresh();
        if (child.Tag == Text && !child.Attributes.ContainsKey("alignment")) add["alignment"] = "MiddleLeft";
        if (child.Tag == "TextMeshPro" && !child.Attributes.ContainsKey("alignment")) add["alignment"] = "Left";
        if (!child.Attributes.ContainsKey("text"))
        {
            if (applied.TryGetValue("text", out var t)) add["text"] = t;
            else if (!e.Attributes.ContainsKey("text")) add["active"] = "false";
        }
        if (!child.Attributes.ContainsKey("padding")) add["padding"] = "23 5 2 1";
        if (!child.Attributes.ContainsKey("flexibleWidth")) add["flexibleWidth"] = "1";
        if (add.Count > 0) PanelOps.Apply(child, add);
    }
}
