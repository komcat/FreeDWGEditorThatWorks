namespace FreeDwg.Core.Styling;

/// <summary>
/// Everything the renderer needs to stroke an entity, with no lookups left to
/// do. Resolving ByLayer/ByBlock chains once at import keeps the render loop
/// free of indirection.
/// </summary>
public readonly record struct DisplayStyle(Rgb Color, Lineweight Lineweight)
{
    public static readonly DisplayStyle Default = new(Rgb.White, Lineweight.Default);
}
