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

    /// <summary>Layers added since opening, which have no record in the file yet.</summary>
    public HashSet<Layer> CreatedLayers { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Handles of layer records that came from the file and are gone.</summary>
    public HashSet<ulong> DeletedLayers { get; } = new();

    /// <summary>Handles of layer records that came from the file and have changed.</summary>
    public HashSet<ulong> ModifiedLayers { get; } = new();

    public bool IsEmpty =>
        Created.Count == 0 && Deleted.Count == 0 && Modified.Count == 0 &&
        CreatedLayers.Count == 0 && DeletedLayers.Count == 0 && ModifiedLayers.Count == 0;

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

    // Layers are objects in the file like any other, so they are tracked the
    // same way and for the same reason: a drawing whose entities are written
    // onto a layer table that was never updated is a drawing full of
    // geometry on the wrong layer.

    public void LayerAdded(Layer layer)
    {
        if (layer.SourceHandle != 0 && DeletedLayers.Remove(layer.SourceHandle))
        {
            ModifiedLayers.Add(layer.SourceHandle);
            return;
        }

        CreatedLayers.Add(layer);
    }

    public void LayerRemoved(Layer layer)
    {
        if (CreatedLayers.Remove(layer)) return;
        if (layer.SourceHandle != 0) DeletedLayers.Add(layer.SourceHandle);
    }

    public void LayerEdited(Layer layer)
    {
        if (layer.SourceHandle != 0 && !CreatedLayers.Contains(layer))
            ModifiedLayers.Add(layer.SourceHandle);
    }

    public override string ToString() =>
        $"{Created.Count} created, {Deleted.Count} deleted, {Modified.Count} modified"
        + (CreatedLayers.Count + DeletedLayers.Count + ModifiedLayers.Count == 0
            ? ""
            : $"; layers {CreatedLayers.Count} created, {DeletedLayers.Count} deleted, {ModifiedLayers.Count} modified");
}
