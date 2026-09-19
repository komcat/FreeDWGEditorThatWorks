using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Commands;

/// <summary>
/// What has changed since the drawing was opened, in the terms a writer
/// needs: entities that did not come from the file, handles of objects that
/// did and are now gone, and handles of objects that were edited in place.
/// </summary>
/// <remarks>
/// Derived by replaying the undo stack rather than tracked as edits happen.
/// That sounds wasteful and is not: it is a few hundred entries at worst, it
/// runs when someone asks rather than on every mouse move, and it means undo
/// cannot leave the log claiming a change that is no longer there. The
/// alternative -- maintaining the sets incrementally -- needs every command
/// to know how to take itself back out of them, which is where this kind of
/// bookkeeping usually goes wrong.
/// </remarks>
public sealed class ChangeLog
{
    /// <summary>Entities added since opening; they have no handle in the file yet.</summary>
    public HashSet<SceneEntity> Created { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Handles of objects that came from the file and have been deleted.</summary>
    public HashSet<ulong> Deleted { get; } = new();

    /// <summary>Handles of objects that came from the file and have been edited.</summary>
    public HashSet<ulong> Modified { get; } = new();

    public bool IsEmpty => Created.Count == 0 && Deleted.Count == 0 && Modified.Count == 0;

    /// <summary>
    /// Records an entity as created, or as deleted if it came from the file.
    /// An entity created and then deleted in the same session leaves nothing
    /// behind: there is no handle to delete, so it simply never existed.
    /// </summary>
    public void Removed(SceneEntity entity)
    {
        if (Created.Remove(entity)) return;
        if (entity.SourceHandle != 0) Deleted.Add(entity.SourceHandle);
    }

    public void Added(SceneEntity entity)
    {
        // Re-adding something the file already holds is an edit, not a birth.
        if (entity.SourceHandle != 0 && Deleted.Remove(entity.SourceHandle))
        {
            Modified.Add(entity.SourceHandle);
            return;
        }

        Created.Add(entity);
    }

    public void Edited(SceneEntity entity)
    {
        if (entity.SourceHandle != 0 && !Created.Contains(entity))
            Modified.Add(entity.SourceHandle);
    }

    public override string ToString() =>
        $"{Created.Count} created, {Deleted.Count} deleted, {Modified.Count} modified";
}
