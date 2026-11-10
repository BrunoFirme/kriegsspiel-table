using Godot;

namespace KTable.App;

static class AgentFence
{
    const string Meta = "agent_fenced";

    public static T Mark<T>(T node) where T : Node
    {
        node.SetMeta(Meta, true);
        return node;
    }

    public static bool Marked(Node node) => node.HasMeta(Meta);

    public static bool Fenced(Node? node)
    {
        for (var n = node; n != null; n = n.GetParent())
            if (n.HasMeta(Meta)) return true;
        return false;
    }
}
