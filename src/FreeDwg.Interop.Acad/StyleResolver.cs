using ACadSharp;
using ACadSharp.Entities;
using FreeDwg.Core.Styling;
using AcadLayer = ACadSharp.Tables.Layer;
using SceneLayer = FreeDwg.Core.Scene.Layer;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Collapses the DWG styling chain -- entity, then layer, then block, plus
/// colour indices and true colour -- into a flat <see cref="DisplayStyle"/>.
/// This is the only place ByLayer/ByBlock and AutoCAD colour indices exist;
/// past here everything is concrete RGB and a concrete width.
/// </summary>
internal static class StyleResolver
{
    /// <summary>AutoCAD colour index 7: white on dark, black on light. The renderer flips it.</summary>
    private static readonly Rgb Default = Rgb.White;

    public static DisplayStyle Resolve(Entity entity, SceneLayer? layer)
    {
        return new DisplayStyle(
            ResolveColor(entity.GetActiveColor(), layer),
            ResolveLineweight(entity.GetActiveLineWeightType(), layer));
    }

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
        Color = ToRgb(layer.Color, Default),
        Lineweight = ToLineweight(layer.LineWeight, Lineweight.Default),
        SourceHandle = layer.Handle,
        IsOn = layer.IsOn,
        IsFrozen = layer.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen),
        IsLocked = layer.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Locked),
    };

    private static Rgb ResolveColor(Color color, SceneLayer? layer) =>
        ToRgb(color, layer?.Color ?? Default);

    private static Lineweight ResolveLineweight(LineWeightType weight, SceneLayer? layer) =>
        ToLineweight(weight, layer?.Lineweight ?? Lineweight.Default);
}
