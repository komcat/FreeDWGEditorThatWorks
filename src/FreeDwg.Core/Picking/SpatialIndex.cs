using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Picking;

/// <summary>
/// A bounding volume hierarchy over a layout's entities, so that "what is in
/// this rectangle" stops being a scan of everything.
/// </summary>
/// <remarks>
/// A BVH rather than a grid because CAD drawings are the worst case for a
/// grid: a title block sits in one corner, the model is in another, and the
/// entities range from a millimetre of hatch to a kilometre of setting-out
/// line. A hierarchy over the entity bounds adapts to that with no cell size
/// to guess.
/// <para>
/// Queries yield positions into <see cref="Entities"/>, which is the entity
/// order the index was built from -- painting order. Sorting the positions of
/// a query restores it, which matters: a hatch drawn over a line and a line
/// drawn over a hatch are different pictures.
/// </para>
/// </remarks>
public sealed class SpatialIndex
{
    /// <summary>Entities past this in a node are split; below it, scanned.</summary>
    private const int LeafSize = 8;

    private readonly struct Node
    {
        public readonly Bounds2 Bounds;

        /// <summary>First position in the permutation, for a leaf.</summary>
        public readonly int Start;

        /// <summary>Entity count for a leaf; zero for an internal node.</summary>
        public readonly int Count;

        /// <summary>Right child, for an internal node. The left child is the next node.</summary>
        public readonly int Right;

        public Node(Bounds2 bounds, int start, int count, int right)
        {
            Bounds = bounds; Start = start; Count = count; Right = right;
        }

        public bool IsLeaf => Count > 0;
    }

    private readonly SceneEntity[] _entities;
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private readonly int[] _unbounded;

    private SpatialIndex(SceneEntity[] entities, int[] order, Node[] nodes, int[] unbounded)
    {
        _entities = entities;
        _order = order;
        _nodes = nodes;
        _unbounded = unbounded;
    }

    /// <summary>The entities as they were when the index was built.</summary>
    public IReadOnlyList<SceneEntity> Entities => _entities;

    public int Count => _entities.Length;

    /// <summary>Nodes in the tree, for tests that care about the shape of it.</summary>
    public int NodeCount => _nodes.Length;

    public SceneEntity this[int position] => _entities[position];

    public static SpatialIndex Build(IReadOnlyList<SceneEntity> entities)
    {
        var snapshot = new SceneEntity[entities.Count];
        for (int i = 0; i < snapshot.Length; i++) snapshot[i] = entities[i];

        var order = new List<int>(snapshot.Length);
        var unbounded = new List<int>();

        for (int i = 0; i < snapshot.Length; i++)
        {
            // An entity with no bounds cannot be culled or indexed, and the
            // renderer already draws those unconditionally.
            if (snapshot[i].Bounds.IsEmpty) unbounded.Add(i);
            else order.Add(i);
        }

        var nodes = new List<Node>();
        var permutation = order.ToArray();
        if (permutation.Length > 0) BuildNode(snapshot, permutation, 0, permutation.Length, nodes);

        return new SpatialIndex(snapshot, permutation, nodes.ToArray(), unbounded.ToArray());
    }

    private static int BuildNode(SceneEntity[] entities, int[] order, int start, int count, List<Node> nodes)
    {
        var bounds = Bounds2.Empty;
        for (int i = start; i < start + count; i++) bounds = bounds.Union(entities[order[i]].Bounds);

        int self = nodes.Count;

        if (count <= LeafSize)
        {
            nodes.Add(new Node(bounds, start, count, -1));
            return self;
        }

        nodes.Add(default);   // placeholder; the children decide where right is

        // Split down the longer axis at the median centroid. Median rather
        // than midpoint so that clustered geometry -- which is all of it --
        // still halves, instead of piling into one child.
        bool splitOnX = bounds.Width >= bounds.Height;
        Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
        {
            var ca = entities[a].Bounds.Center;
            var cb = entities[b].Bounds.Center;
            return splitOnX ? ca.X.CompareTo(cb.X) : ca.Y.CompareTo(cb.Y);
        }));

        int half = count / 2;
        BuildNode(entities, order, start, half, nodes);
        int right = BuildNode(entities, order, start + half, count - half, nodes);

        nodes[self] = new Node(bounds, start, 0, right);
        return self;
    }

    /// <summary>
    /// Appends the positions of every entity whose bounds meet
    /// <paramref name="area"/>, plus every entity with no bounds at all.
    /// Unordered; sort for painting order.
    /// </summary>
    public void Query(Bounds2 area, List<int> results)
    {
        results.AddRange(_unbounded);
        if (_nodes.Length == 0 || area.IsEmpty) return;

        Span<int> stack = stackalloc int[64];
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            int index = stack[--top];
            var node = _nodes[index];
            if (!node.Bounds.Intersects(area)) continue;

            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int position = _order[i];
                    if (_entities[position].Bounds.Intersects(area)) results.Add(position);
                }
                continue;
            }

            // The left child is always the next node; the right one is stored.
            // Depth is log2(n) for a median split, so the stack is generous.
            if (top + 2 > stack.Length) continue;
            stack[top++] = index + 1;
            stack[top++] = node.Right;
        }
    }

    public List<int> Query(Bounds2 area)
    {
        var results = new List<int>();
        Query(area, results);
        return results;
    }
}
