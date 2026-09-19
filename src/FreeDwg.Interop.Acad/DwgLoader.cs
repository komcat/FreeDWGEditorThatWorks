using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;
using AcadArc = ACadSharp.Entities.Arc;
using AcadCircle = ACadSharp.Entities.Circle;
using AcadLine = ACadSharp.Entities.Line;
using SceneLayer = FreeDwg.Core.Scene.Layer;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Translates an ACadSharp <see cref="CadDocument"/> into the editor's scene
/// model. This is the anti-corruption layer: it is the only assembly that
/// references ACadSharp, and nothing downstream of it can tell which library
/// (or hand-written parser) produced the drawing.
/// </summary>
public static class DwgLoader
{
    /// <summary>Reads a DWG or DXF file and converts it to a scene.</summary>
    public static Drawing Load(string path, ImportDiagnostics? diagnostics = null)
    {
        var diag = diagnostics ?? new ImportDiagnostics();

        CadDocument document = IsDxf(path)
            ? DxfReader.Read(path, (_, e) => diag.OnNotification(e))
            : DwgReader.Read(path, (_, e) => diag.OnNotification(e));

        var drawing = Convert(document, diag);
        drawing.SourcePath = path;
        return drawing;
    }

    private static bool IsDxf(string path) =>
        string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts an already-open document. Model space only, for now.</summary>
    public static Drawing Convert(CadDocument document, ImportDiagnostics diagnostics)
    {
        var drawing = new Drawing();

        var layerIndexByHandle = new Dictionary<ulong, int>();
        int fallbackLayer = -1;

        foreach (var acadLayer in document.Layers)
        {
            var layer = StyleResolver.ToSceneLayer(acadLayer);
            int index = drawing.AddLayer(layer);
            layerIndexByHandle[acadLayer.Handle] = index;

            // Layer "0" always exists in a well-formed drawing and is the
            // natural home for entities whose own layer we cannot resolve.
            if (fallbackLayer < 0 && layer.Name == "0") fallbackLayer = index;
        }

        if (fallbackLayer < 0) fallbackLayer = drawing.AddLayer(new SceneLayer("0"));

        foreach (var entity in document.Entities)
        {
            if (entity.IsInvisible) continue;

            int layerIndex = entity.Layer is not null &&
                             layerIndexByHandle.TryGetValue(entity.Layer.Handle, out int index)
                ? index
                : fallbackLayer;

            var converted = ConvertEntity(entity);
            if (converted is null)
            {
                diagnostics.Unsupported(entity);
                continue;
            }

            converted.LayerIndex = layerIndex;
            converted.Style = StyleResolver.Resolve(entity, drawing.Layers[layerIndex]);
            converted.SourceHandle = entity.Handle;
            drawing.Add(converted);
        }

        diagnostics.ImportedCount = drawing.Entities.Count;
        return drawing;
    }

    private static SceneEntity? ConvertEntity(Entity entity) => entity switch
    {
        // Arc derives from Circle in ACadSharp, so it has to be matched first.
        AcadArc arc => ConvertArc(arc),
        AcadCircle circle => new SCircle(ToVec2(circle.Center), circle.Radius),
        AcadLine line => new SLine(ToVec2(line.StartPoint), ToVec2(line.EndPoint)),
        LwPolyline polyline => ConvertLwPolyline(polyline),
        _ => null,
    };

    private static SArc ConvertArc(AcadArc arc)
    {
        // DWG stores start and end angles counter-clockwise; the arc is always
        // drawn CCW from start to end, so a wrapped end angle is normal.
        double sweep = ArcMath.Normalize(arc.EndAngle - arc.StartAngle);
        if (sweep < 1e-12) sweep = ArcMath.TwoPi;

        return new SArc(ToVec2(arc.Center), arc.Radius, arc.StartAngle, sweep);
    }

    private static SPolyline ConvertLwPolyline(LwPolyline polyline)
    {
        var vertices = new PolyVertex[polyline.Vertices.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = polyline.Vertices[i];
            vertices[i] = new PolyVertex(new Vec2(v.Location.X, v.Location.Y), v.Bulge);
        }
        return new SPolyline(vertices, polyline.IsClosed);
    }

    /// <summary>
    /// Projects to the XY plane. A 2D editor has no use for Z, and model-space
    /// drafting keeps everything at elevation zero anyway.
    /// </summary>
    private static Vec2 ToVec2(XYZ p) => new(p.X, p.Y);
}
