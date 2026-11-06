using System.Xml;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public sealed class ColladaFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ktable-collada-" + Guid.NewGuid().ToString("N")[..8]);

    public ColladaFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    string Write(string name, string text)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    const string Quad = """
        <source id="p"><float_array count="12">0 0 0  2 0 0  2 2 0  0 2 0</float_array><technique_common><accessor stride="3"/></technique_common></source>
        <vertices id="v"><input semantic="POSITION" source="#p"/></vertices>
        """;

    static string Document(string asset, string geometry, string scene, string library = "") => $"""
        <?xml version="1.0"?>
        <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
          <asset>{asset}</asset>
          {library}
          <library_geometries><geometry id="g"><mesh>{geometry}</mesh></geometry></library_geometries>
          <library_visual_scenes><visual_scene id="s">{scene}</visual_scene></library_visual_scenes>
        </COLLADA>
        """;

    [Fact]
    public void A_four_sided_polygon_reads_as_two_triangles()
    {
        var m = ColladaFile.Read(Write("quad.dae", Document("", Quad + """
            <polylist count="1"><input semantic="VERTEX" source="#v" offset="0"/><vcount>4</vcount><p>0 1 2 3</p></polylist>
            """, """<node><instance_geometry url="#g"/></node>""")));
        Assert.Equal(6, m.V.Count);
        Assert.Equal(2, m.Mat.Count);
        Assert.Single(m.Materials);
    }

    [Fact]
    public void A_node_s_moves_and_the_file_s_unit_place_the_mesh()
    {
        var m = ColladaFile.Read(Write("moved.dae", Document("""<unit meter="0.5"/>""", Quad + """
            <triangles count="2"><input semantic="VERTEX" source="#v" offset="0"/><p>0 1 2 0 2 3</p></triangles>
            """, """<node><translate>10 0 0</translate><scale>2 2 2</scale><instance_geometry url="#g"/></node>""")));
        Assert.Equal(5, m.V.Min(v => v.X), 4);
        Assert.Equal(7, m.V.Max(v => v.X), 4);
        Assert.Equal(2, m.V.Max(v => v.Y), 4);
    }

    [Fact]
    public void A_nested_node_takes_its_parent_s_move_too()
    {
        var m = ColladaFile.Read(Write("nested.dae", Document("", Quad + """
            <triangles count="1"><input semantic="VERTEX" source="#v" offset="0"/><p>0 1 2</p></triangles>
            """, """<node><translate>0 0 5</translate><node><translate>1 0 0</translate><instance_geometry url="#g"/></node></node>""")));
        Assert.All(m.V, v => Assert.Equal(5, v.Z, 4));
        Assert.Equal(1, m.V.Min(v => v.X), 4);
    }

    [Fact]
    public void A_material_bound_to_a_picture_beside_the_file_carries_that_picture()
    {
        File.WriteAllBytes(Path.Combine(_dir, "skin.png"), [1, 2, 3]);
        var library = """
            <library_images><image id="img"><init_from>skin.png</init_from></image></library_images>
            <library_effects><effect id="fx"><profile_COMMON>
              <newparam sid="surf"><surface type="2D"><init_from>img</init_from></surface></newparam>
              <newparam sid="samp"><sampler2D><source>surf</source></sampler2D></newparam>
              <technique><phong><diffuse><texture texture="samp" texcoord="uv"/></diffuse></phong></technique>
            </profile_COMMON></effect></library_effects>
            <library_materials><material id="mat"><instance_effect url="#fx"/></material></library_materials>
            """;
        var m = ColladaFile.Read(Write("dressed.dae", Document("", Quad + """
            <triangles count="1" material="sym"><input semantic="VERTEX" source="#v" offset="0"/><p>0 1 2</p></triangles>
            """, """
            <node><instance_geometry url="#g"><bind_material><technique_common>
              <instance_material symbol="sym" target="#mat"/>
            </technique_common></bind_material></instance_geometry></node>
            """, library)));
        Assert.Equal(Path.Combine(_dir, "skin.png"), Assert.Single(m.Materials).Picture);
    }

    [Fact]
    public void Geometry_no_scene_places_is_read_where_it_stands()
    {
        var m = ColladaFile.Read(Write("loose.dae", Document("", Quad + """
            <triangles count="1"><input semantic="VERTEX" source="#v" offset="0"/><p>0 1 2</p></triangles>
            """, "")));
        Assert.Equal(3, m.V.Count);
    }

    [Fact]
    public void A_file_with_no_triangles_or_with_a_doctype_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => ColladaFile.Read(Write("empty.dae", Document("", Quad, """<node><instance_geometry url="#g"/></node>"""))));
        var hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE COLLADA [ <!ENTITY x SYSTEM "file:///c:/windows/win.ini"> ]>
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema">&x;</COLLADA>
            """;
        Assert.Throws<XmlException>(() => ColladaFile.Read(Write("hostile.dae", hostile)));
    }
}
