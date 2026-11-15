using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class RendererHeapTests
{
    const string ResourceHeap = "rendering/rendering_device/d3d12/max_resource_descriptors";

    [NodeTest]
    static Task Direct3D_12_keeps_the_largest_resource_heap_the_engine_allows_so_tables_of_thousands_of_pieces_open(TestRunner r)
    {
        var setting = ProjectSettings.Singleton.GetPropertyList().FirstOrDefault(p => (string)p["name"] == ResourceHeap);
        Check.That(setting != null, "the engine still has the setting");
        int most = int.Parse(((string)setting!["hint_string"]).Split(',')[1]);
        Check.Equal(most, (int)ProjectSettings.GetSetting(ResourceHeap), "descriptors in the heap");
        return Task.CompletedTask;
    }
}
