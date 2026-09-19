using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Editing;

/// <summary>
/// What an edit wants done: these entities out, those ones in.
/// </summary>
/// <remarks>
/// Trim, extend, fillet and chamfer all change the shape of what they touch,
/// and some of them change its type -- trimming a circle leaves an arc, and
/// trimming the middle out of a line leaves two lines. Expressing every one
/// of them as a swap avoids a separate command per operation, and avoids
/// each entity needing a way to have its geometry overwritten and put back.
/// <para>
/// A replacement inherits the original's <see cref="SceneEntity.SourceHandle"/>
/// where there is one to inherit, so a trimmed line is still the line the
/// file knows about and a save rewrites it rather than deleting it and
/// inventing another.
/// </para>
/// </remarks>
public readonly record struct EditPlan(
    IReadOnlyList<SceneEntity> Removed,
    IReadOnlyList<SceneEntity> Added)
{
    public static EditPlan Nothing => new([], []);

    public bool IsEmpty => Removed.Count == 0 && Added.Count == 0;

    /// <summary>One entity swapped for however many it became.</summary>
    public static EditPlan Replace(SceneEntity original, params SceneEntity[] replacements)
    {
        // The first replacement is the continuation of the original as far as
        // the file is concerned; anything beyond it is genuinely new.
        if (replacements.Length > 0) replacements[0].SourceHandle = original.SourceHandle;

        return new EditPlan([original], replacements);
    }

    public static EditPlan Erase(SceneEntity original) => new([original], []);

    /// <summary>
    /// Many entities swapped one for one, for an edit applied across a
    /// selection.
    /// </summary>
    /// <remarks>
    /// Kept as a single plan rather than one per entity so that setting the
    /// layer of forty objects is one step on the undo stack. Forty steps
    /// would be technically correct and unusable.
    /// </remarks>
    public static EditPlan Swap(IReadOnlyList<(SceneEntity Original, SceneEntity Replacement)> pairs)
    {
        var removed = new SceneEntity[pairs.Count];
        var added = new SceneEntity[pairs.Count];

        for (int i = 0; i < pairs.Count; i++)
        {
            var (original, replacement) = pairs[i];
            replacement.SourceHandle = original.SourceHandle;

            removed[i] = original;
            added[i] = replacement;
        }

        return new EditPlan(removed, added);
    }
}
