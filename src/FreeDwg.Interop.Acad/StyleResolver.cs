using ACadSharp;
using ACadSharp.Entities;
using FreeDwg.Core.Styling;
using AcadLayer = ACadSharp.Tables.Layer;
using AcadLineType = ACadSharp.Tables.LineType;
using SceneLayer = FreeDwg.Core.Scene.Layer;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Collapses the DWG styling chain -- entity, then layer, then block, plus
/// colour indices, true colour and linetype references -- into a flat
/// <see cref="DisplayStyle"/>. This is the only place ByLayer/ByBlock and
/// AutoCAD colour indices exist; past here everything is concrete.
/// </summary>
internal sealed class StyleResolver
{
    /// <summary>AutoCAD colour index 7: white on dark, black on light. The renderer flips it.</summary>
    private static readonly Rgb DefaultColor = Rgb.White;

    private readonly Dictionary<ulong, Linetype> _linetypeByHandle;
    private readonly double _globalLinetypeScale;

    public StyleResolver(Dictionary<ulong, Linetype> linetypeByHandle, double globalLinetypeScale)
    {
        _linetypeByHandle = linetypeByHandle;
        _globalLinetypeScale = globalLinetypeScale > 0 ? globalLinetypeScale : 1.0;
    }

    /// <summary>
    /// Resolves an entity's style, reporting which parts are ByBlock and so
    /// must be supplied by the block reference at draw time instead.
    /// </summary>
    public (DisplayStyle Style, StyleInheritance Inherits) Resolve(Entity entity, SceneLayer? layer, Linetype? layerLinetype)
    {
        // Check the raw values first: GetActiveColor() resolves ByBlock away,
        // but for an entity inside a block definition that decision belongs to
        // whichever reference is drawing it, not to us.
        var inherits = StyleInheritance.None;
        if (entity.Color.IsByBlock) inherits |= StyleInheritance.Color;
        if (entity.LineWeight == LineWeightType.ByBlock) inherits |= StyleInheritance.Lineweight;
        if (IsByBlock(entity.LineType)) inherits |= StyleInheritance.Linetype;

        var style = new DisplayStyle(
            ToRgb(entity.GetActiveColor(), layer?.Color ?? DefaultColor),
            ToLineweight(entity.GetActiveLineWeightType(), layer?.Lineweight ?? Lineweight.Default),
            ResolveLinetype(entity.LineType, layerLinetype),
            EffectiveLinetypeScale(entity));

        return (style, inherits);
    }

    public double EffectiveLinetypeScale(Entity entity)
    {
        double entityScale = entity.LineTypeScale > 0 ? entity.LineTypeScale : 1.0;
        return entityScale * _globalLinetypeScale;
    }

    private Linetype? ResolveLinetype(AcadLineType? lineType, Linetype? layerLinetype)
    {
        if (lineType is null) return layerLinetype;
        if (IsByLayer(lineType)) return layerLinetype;
        if (IsByBlock(lineType)) return null;   // supplied by the block reference

        return _linetypeByHandle.TryGetValue(lineType.Handle, out var resolved) ? resolved : null;
    }

    public Linetype? LinetypeOf(AcadLineType? lineType) =>
        lineType is not null && _linetypeByHandle.TryGetValue(lineType.Handle, out var resolved)
            ? resolved
            : null;

    private static bool IsByLayer(AcadLineType lineType) =>
        string.Equals(lineType.Name, "ByLayer", StringComparison.OrdinalIgnoreCase);

    private static bool IsByBlock(AcadLineType? lineType) =>
        lineType is not null && string.Equals(lineType.Name, "ByBlock", StringComparison.OrdinalIgnoreCase);

    public static Rgb ToRgb(Color color, Rgb fallback)
    {
        // GetRgb() returns black for unresolved ByLayer/ByBlock, which would
        // silently paint entities invisible on a dark background.
        if (color.IsByLayer || color.IsByBlock) return fallback;

        var rgb = color.GetRgb();
        return rgb.Length >= 3 ? new Rgb(rgb[0], rgb[1], rgb[2]) : fallback;
    }

    public static Lineweight ToLineweight(LineWeightType weight, Lineweight fallback) => weight switch
    {
        LineWeightType.ByLayer or LineWeightType.ByBlock => fallback,
        LineWeightType.Default or LineWeightType.ByDIPs => Lineweight.Default,
        _ when (short)weight >= 0 => new Lineweight((short)weight),
        _ => Lineweight.Default,
    };

    public static SceneLayer ToSceneLayer(AcadLayer layer) => new(layer.Name)
    {
        Color = ToRgb(layer.Color, DefaultColor),
        Lineweight = ToLineweight(layer.LineWeight, Lineweight.Default),
        SourceHandle = layer.Handle,
        IsOn = layer.IsOn,
        IsFrozen = layer.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen),
        IsLocked = layer.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Locked),
    };

    /// <summary>Keeps only the dash lengths; text and shape elements are dropped.</summary>
    public static Linetype ToLinetype(AcadLineType lineType)
    {
        var pattern = new List<double>();

        foreach (var segment in lineType.Segments)
        {
            // A text or shape element occupies no length of its own in the
            // dash cycle, so skipping it leaves the dashes correctly spaced.
            if (segment.IsText || segment.IsShape) continue;
            pattern.Add(segment.Length);
        }

        return new Linetype(lineType.Name, pattern);
    }
}
