namespace FreeDwg.Core.Styling;

/// <summary>
/// Everything the renderer needs to stroke an entity, with no lookups left to
/// do. Resolving ByLayer chains once at import keeps the render loop free of
/// indirection.
/// </summary>
/// <param name="LinetypeScale">
/// The entity's own linetype scale already multiplied by the drawing's LTSCALE.
/// </param>
public readonly record struct DisplayStyle(
    Rgb Color,
    Lineweight Lineweight,
    Linetype? Linetype = null,
    double LinetypeScale = 1.0)
{
    public static readonly DisplayStyle Default = new(Rgb.White, Lineweight.Default);

    public bool IsDashed => Linetype is { IsContinuous: false } && LinetypeScale > 0;
}
