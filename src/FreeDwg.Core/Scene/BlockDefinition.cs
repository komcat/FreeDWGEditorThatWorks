using FreeDwg.Core.Geometry;

namespace FreeDwg.Core.Scene;

/// <summary>
/// A named group of entities stored once and drawn many times through
/// <see cref="Entities.SInsert"/>. Geometry is in block-local coordinates,
/// relative to <see cref="BasePoint"/>.
/// </summary>
public sealed class BlockDefinition
{
    public BlockDefinition(string name) { Name = name; }

    public string Name { get; }
    public Vec2 BasePoint { get; set; }
    public List<SceneEntity> Entities { get; } = new();
    public ulong SourceHandle { get; set; }

    /// <summary>True for the *D1/*U1 blocks that hold generated dimension and leader geometry.</summary>
    public bool IsAnonymous { get; set; }

    private Bounds2? _bounds;
    private bool _computing;

    /// <summary>Bounds in block-local coordinates.</summary>
    public Bounds2 Bounds
    {
        get
        {
            if (_bounds is not null) return _bounds.Value;

            // A block that (transitively) references itself is invalid but does
            // turn up; break the cycle rather than recursing forever.
            if (_computing) return Bounds2.Empty;

            _computing = true;
            try
            {
                var b = Bounds2.Empty;
                foreach (var e in Entities) b = b.Union(e.Bounds);
                _bounds = b;
                return b;
            }
            finally
            {
                _computing = false;
            }
        }
    }

    public void InvalidateBounds() => _bounds = null;

    public override string ToString() => $"{Name} ({Entities.Count} entities)";
}
