namespace KTable.Client.Studio;

public static class FaceDocs
{
    public static ArtDoc Blank(double pieceW, double pieceH, int frontW, int frontH)
    {
        int w = 512, h = (int)Math.Clamp(Math.Round(512 * pieceH / Math.Max(0.01, pieceW)), 16, 2048);
        if (frontW > 0 && frontH > 0)
        {
            float k = Math.Min(1f, 2048f / Math.Max(frontW, frontH));
            w = Math.Max(8, (int)(frontW * k)); h = Math.Max(8, (int)(frontH * k));
        }
        var doc = new ArtDoc { W = w, H = h };
        doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
        return doc;
    }

    public static ArtLayer Unreadable(ArtDoc doc, string src, int knownW, int knownH)
    {
        float w = knownW > 0 ? knownW : doc.W, h = knownH > 0 ? knownH : doc.H;
        return new ArtLayer { Name = "Picture (could not be read)", Kind = "image", Src = src, SrcW = (int)w, SrcH = (int)h, X = doc.W / 2f, Y = doc.H / 2f, Sx = doc.W / w, Sy = doc.H / h, Locked = true };
    }

    public static string? ToolFor(char key) => char.ToUpperInvariant(key) switch
    {
        'V' => "move", 'B' => "brush", 'E' => "erase", 'U' => "shape", 'T' => "text", 'G' => "fill",
        'I' => "pick", 'C' => "crop", 'O' => "outline", 'M' => "measure", 'N' => "place", 'H' => "pan", _ => null,
    };
}
