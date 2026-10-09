using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;
using static FreeDwg.Interop.Acad.CadGeometry;
using AcadArc = ACadSharp.Entities.Arc;
using AcadCircle = ACadSharp.Entities.Circle;
using AcadEllipse = ACadSharp.Entities.Ellipse;
using AcadLayer = ACadSharp.Tables.Layer;
using AcadLayout = ACadSharp.Objects.Layout;
using AcadLine = ACadSharp.Entities.Line;
using SceneLayer = FreeDwg.Core.Scene.Layer;
using SceneLayout = FreeDwg.Core.Scene.Layout;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// What a save has to remember from one save to the next.
/// </summary>
/// <remarks>
/// The change log is replayed from the moment the file was opened, so a
/// second save sees the first save's edits again -- and an undo after a save
/// takes back an edit the file already has. Neither is a problem as long as
/// writing is "make the file match the scene" rather than "apply this edit",
/// and as long as everything a save has touched is looked at again by the
/// next one. That is what this holds.
/// </remarks>
internal sealed class SaveState
{
    /// <summary>Entity handles any save has written, created or removed.</summary>
    public HashSet<ulong> Entities { get; } = new();

    /// <summary>Layer handles any save has written, created or removed.</summary>
    public HashSet<ulong> Layers { get; } = new();

    /// <summary>
    /// Entities a save took out of the file, by the handle they had. An undo
    /// can bring one back, and a copy can be made of one, and in both cases
    /// the object the file had is the faithful thing to write.
    /// </summary>
    public Dictionary<ulong, (Entity Entity, BlockRecord Owner)> RemovedEntities { get; } = new();

    public Dictionary<ulong, AcadLayer> RemovedLayers { get; } = new();

    /// <summary>
    /// For the kinds written by moving the file's own object rather than by
    /// rewriting it -- a hatch, an imported dimension -- where the scene's
    /// copy stood when it was last written.
    /// </summary>
    public Dictionary<ulong, Mat3> Poses { get; } = new();
}

/// <summary>
/// Makes a loaded <see cref="CadDocument"/> match the scene, touching only
/// what the change log and earlier saves say has changed.
/// </summary>
/// <remarks>
/// This is the whole of delta-save. Everything the scene does not model --
/// xdata, extension dictionaries, proxies, the entities the reader skipped --
/// is never looked at, so it is written back exactly as it was read.
/// <para>
/// Each kind of entity is written one of two ways. Where the scene's geometry
/// says everything the file does -- a line, an arc, a polyline -- the file
/// object's fields are set from it, and only the ones that differ. Where it
/// does not -- a hatch whose boundary was flattened, a dimension drawn from
/// its picture block -- the file's own object is moved by however far the
/// scene's copy has moved, so nothing the scene threw away is lost.
/// </para>
/// </remarks>
internal sealed class SceneWriter
{
    private readonly CadDocument _doc;
    private readonly Drawing _drawing;
    private readonly SaveState _state;
    private readonly SaveReport _report;

    public SceneWriter(CadDocument document, Drawing drawing, SaveState state, SaveReport report)
    {
        _doc = document;
        _drawing = drawing;
        _state = state;
        _report = report;
    }

    public void Run(ChangeLog log)
    {
        // Layers first: an entity is written onto a layer by handle, so a
        // layer made in this session has to be in the file before anything
        // is put on it.
        SyncLayers(log);
        SyncEntities(log);
        SyncHeader();
    }

    // ---- layers ------------------------------------------------------------

    private void SyncLayers(ChangeLog log)
    {
        var dirty = new HashSet<ulong>(log.ModifiedLayers);
        dirty.UnionWith(log.DeletedLayers);
        dirty.UnionWith(_state.Layers);

        var inScene = _drawing.Layers.Select(layer => layer.SourceHandle).Where(h => h != 0).ToHashSet();

        // Removals before additions, so a layer made with the name of one
        // that was deleted does not collide with it.
        foreach (ulong handle in dirty)
        {
            if (inScene.Contains(handle)) continue;
            if (!_doc.TryGetCadObject(handle, out CadObject found) || found is not AcadLayer layer) continue;

            // The scene refuses to delete a layer it can see anything on, but
            // the file can hold things the scene never read. A layer record
            // with an entity still pointing at it cannot go.
            if (IsReferenced(layer))
            {
                _report.Note($"Layer {layer.Name} was kept: the file has objects on it this editor does not show.");
                continue;
            }

            _doc.Layers.Remove(layer.Name);
            _state.RemovedLayers[handle] = layer;
            _state.Layers.Add(handle);
            _report.LayersDeleted++;
        }

        var renames = new List<(SceneLayer Scene, AcadLayer File)>();

        foreach (var layer in _drawing.Layers)
        {
            if (layer.SourceHandle != 0 &&
                _doc.TryGetCadObject(layer.SourceHandle, out CadObject found) && found is AcadLayer existing)
            {
                if (!dirty.Contains(layer.SourceHandle)) continue;

                bool changed = WriteLayer(existing, layer);
                if (!string.Equals(existing.Name, layer.Name, StringComparison.Ordinal))
                {
                    renames.Add((layer, existing));
                    changed = true;
                }

                _state.Layers.Add(layer.SourceHandle);
                if (changed) _report.LayersModified++;
                continue;
            }

            // New here, or deleted by an earlier save and brought back by undo.
            var record = layer.SourceHandle != 0 && _state.RemovedLayers.Remove(layer.SourceHandle, out var removed)
                ? removed
                : new AcadLayer(layer.Name);

            if (_doc.Layers.TryGetValue(layer.Name, out var clash))
            {
                // Only a layer the save had to keep can be in the way; the
                // scene's own names are unique. Share it rather than fail.
                layer.SourceHandle = clash.Handle;
                WriteLayer(clash, layer);
                _state.Layers.Add(clash.Handle);
                continue;
            }

            record.Name = layer.Name;
            WriteLayer(record, layer);
            _doc.Layers.Add(record);

            layer.SourceHandle = record.Handle;
            _state.Layers.Add(record.Handle);
            _report.LayersCreated++;
        }

        // Renamed in two steps: swapping two layers' names in one would
        // collide half way through.
        foreach (var (scene, file) in renames)
        {
            if (string.Equals(file.Name, AcadLayer.DefaultName, StringComparison.OrdinalIgnoreCase))
            {
                _report.Note("Layer 0 cannot be renamed in a DWG; it kept its name.");
                continue;
            }

            file.Name = $"$freedwg-rename-{file.Handle:X}";
        }

        foreach (var (scene, file) in renames)
        {
            if (string.Equals(file.Name, AcadLayer.DefaultName, StringComparison.OrdinalIgnoreCase)) continue;
            file.Name = scene.Name;
        }
    }

    private bool WriteLayer(AcadLayer file, SceneLayer layer)
    {
        bool changed = false;

        if (StyleResolver.ToRgb(file.Color, Rgb.White) != layer.Color)
        {
            file.Color = ToColor(layer.Color);
            changed = true;
        }

        if (!StyleResolver.ToLineweight(file.LineWeight, Lineweight.Default).Equals(layer.Lineweight))
        {
            file.LineWeight = (LineWeightType)layer.Lineweight.Hundredths;
            changed = true;
        }

        string linetype = layer.Linetype?.Name ?? LineType.ContinuousName;
        if (!string.Equals(file.LineType?.Name, linetype, StringComparison.OrdinalIgnoreCase))
        {
            if (_doc.LineTypes.TryGetValue(linetype, out var found))
            {
                file.LineType = found;
                changed = true;
            }
        }

        if (file.IsOn != layer.IsOn)
        {
            file.IsOn = layer.IsOn;
            changed = true;
        }

        changed |= SetFlag(file, LayerFlags.Frozen, layer.IsFrozen);
        changed |= SetFlag(file, LayerFlags.Locked, layer.IsLocked);
        return changed;
    }

    private static bool SetFlag(AcadLayer layer, LayerFlags flag, bool on)
    {
        if (layer.Flags.HasFlag(flag) == on) return false;
        layer.Flags = on ? layer.Flags | flag : layer.Flags & ~flag;
        return true;
    }

    private bool IsReferenced(AcadLayer layer)
    {
        foreach (var record in _doc.BlockRecords)
            foreach (var entity in record.Entities)
                if (string.Equals(entity.Layer?.Name, layer.Name, StringComparison.OrdinalIgnoreCase))
                    return true;

        return false;
    }

    private AcadLayer FileLayer(int index)
    {
        var layer = (uint)index < (uint)_drawing.Layers.Count ? _drawing.Layers[index] : null;

        if (layer is not null && _doc.TryGetCadObject(layer.SourceHandle, out CadObject found) && found is AcadLayer file)
            return file;

        return _doc.Layers[AcadLayer.DefaultName];
    }

    private SceneLayer? SceneLayerAt(int index) =>
        (uint)index < (uint)_drawing.Layers.Count ? _drawing.Layers[index] : _drawing.Layers.FirstOrDefault();

    // ---- entities ----------------------------------------------------------

    private void SyncEntities(ChangeLog log)
    {
        var dirty = new HashSet<ulong>(log.Modified);
        dirty.UnionWith(log.Deleted);
        dirty.UnionWith(_state.Entities);

        // A MINSERT arrives as one scene insert per cell, all with the
        // MINSERT's handle; any handle seen twice is one of those.
        var counts = new Dictionary<ulong, int>();
        foreach (var entity in LayerTable.AllEntities(_drawing))
            if (entity.SourceHandle != 0)
                counts[entity.SourceHandle] = counts.GetValueOrDefault(entity.SourceHandle) + 1;

        foreach (var layout in _drawing.Layouts)
        {
            var owner = OwnerOf(layout);

            foreach (var entity in layout.Entities)
            {
                ulong handle = entity.SourceHandle;

                if (owner is null)
                {
                    if (handle == 0 || dirty.Contains(handle))
                        _report.Note($"Layout {layout.Name} is not in the file, so nothing on it was written.");
                    continue;
                }

                if (handle == 0)
                {
                    Create(entity, owner);
                    continue;
                }

                if (!dirty.Contains(handle)) continue;

                if (counts[handle] > 1)
                {
                    _report.Note($"An array insert ({handle:X}) was edited cell by cell; MINSERT edits are not written.");
                    continue;
                }

                if (TryGetEntity(handle, out var file)) Update(file, entity, owner);
                else Restore(entity, owner);
            }
        }

        // Inside a block, only a restyle can have happened -- recolouring a
        // layer reaches the entities that follow it wherever they are.
        foreach (var block in _drawing.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                if (!dirty.Contains(entity.SourceHandle) || !TryGetEntity(entity.SourceHandle, out var file)) continue;
                if (WriteStyle(file, entity)) _report.Modified++;
                _state.Entities.Add(entity.SourceHandle);
            }
        }

        // Anything still dirty that the scene no longer holds is gone.
        foreach (ulong handle in dirty)
        {
            if (counts.ContainsKey(handle)) continue;
            if (!TryGetEntity(handle, out var file) || file.Owner is not BlockRecord owner) continue;

            owner.Entities.Remove(file);
            _state.RemovedEntities[handle] = (file, owner);
            _state.Entities.Add(handle);
            _report.Deleted++;
        }
    }

    private bool TryGetEntity(ulong handle, out Entity entity)
    {
        entity = null!;
        if (handle == 0 || !_doc.TryGetCadObject(handle, out CadObject found) || found is not Entity e) return false;

        entity = e;
        return true;
    }

    private BlockRecord? OwnerOf(SceneLayout layout)
    {
        if (!layout.IsPaperSpace) return _doc.ModelSpace;

        return _doc.TryGetCadObject(layout.SourceHandle, out CadObject found) && found is AcadLayout file
            ? file.AssociatedBlock
            : null;
    }

    private void Update(Entity file, SceneEntity entity, BlockRecord owner)
    {
        ulong handle = entity.SourceHandle;
        var outcome = Write(file, entity, handle);

        if (!outcome.Compatible)
        {
            // A trim turned a circle into an arc, or a polyline gained a
            // vertex the file's kind cannot hold. The object is replaced, as
            // AutoCAD itself does when an edit changes what something is.
            var replacement = Build(entity);
            if (replacement is null)
            {
                Unsupported(entity);
                return;
            }

            WriteStyle(replacement, entity);
            owner.Entities.Remove(file);
            owner.Entities.Add(replacement);

            _state.RemovedEntities[handle] = (file, owner);
            entity.SourceHandle = replacement.Handle;
            _state.Entities.Add(handle);
            _state.Entities.Add(replacement.Handle);
            _report.Modified++;
            return;
        }

        bool changed = outcome.Changed | WriteStyle(file, entity);
        if (outcome.Pose is { } pose) _state.Poses[handle] = pose;
        _state.Entities.Add(handle);
        if (changed) _report.Modified++;
    }

    /// <summary>
    /// An entity whose file object a save removed, back in the scene -- an
    /// erase undone after saving. The same object goes back, with whatever it
    /// was carrying; the file gives it a new handle, which the scene takes.
    /// </summary>
    private void Restore(SceneEntity entity, BlockRecord owner)
    {
        ulong handle = entity.SourceHandle;

        if (!_state.RemovedEntities.Remove(handle, out var removed))
        {
            Create(entity, owner);
            return;
        }

        var outcome = Write(removed.Entity, entity, handle);
        if (!outcome.Compatible)
        {
            Create(entity, owner);
            return;
        }

        WriteStyle(removed.Entity, entity);
        owner.Entities.Add(removed.Entity);

        entity.SourceHandle = removed.Entity.Handle;
        if (outcome.Pose is { } pose) _state.Poses[removed.Entity.Handle] = pose;
        _state.Entities.Add(removed.Entity.Handle);
        _report.Created++;
    }

    private void Create(SceneEntity entity, BlockRecord owner)
    {
        Entity? file = null;
        Mat3? pose = null;

        // A copy is written from the object it was copied from where there is
        // one, which keeps everything the scene cannot express.
        if (entity.CopiedFrom != 0 && Template(entity.CopiedFrom) is { } template)
        {
            var copy = CopyOf(template);
            var outcome = Write(copy, entity, entity.CopiedFrom);
            if (outcome.Compatible)
            {
                file = copy;
                pose = outcome.Pose;
            }
        }

        file ??= Build(entity);
        if (file is null)
        {
            Unsupported(entity);
            return;
        }

        WriteStyle(file, entity);
        owner.Entities.Add(file);

        entity.SourceHandle = file.Handle;
        pose ??= PoseOf(entity);
        if (pose is { } p) _state.Poses[file.Handle] = p;
        _state.Entities.Add(file.Handle);
        _report.Created++;
    }

    private Entity? Template(ulong handle)
    {
        if (TryGetEntity(handle, out var live)) return live;
        return _state.RemovedEntities.TryGetValue(handle, out var removed) ? removed.Entity : null;
    }

    private Entity CopyOf(Entity template)
    {
        var copy = (Entity)template.Clone();

        // A dimension's picture is its own block, not shared: the copy needs
        // a block of its own or moving it would move the original's picture.
        if (copy is Dimension { Block: { } block })
            block.Name = AnonymousName("*D");

        return copy;
    }

    private string AnonymousName(string prefix)
    {
        for (int n = 1; ; n++)
        {
            string name = $"{prefix}{n}";
            if (!_doc.BlockRecords.Contains(name)) return name;
        }
    }

    /// <summary>What a freshly written opaque entity's pose is recorded as.</summary>
    private static Mat3? PoseOf(SceneEntity entity) => entity switch
    {
        SHatch hatch => hatch.Moved,
        _ => null,
    };

    private void Unsupported(SceneEntity entity) =>
        _report.Note($"A {entity.GetType().Name.TrimStart('S').ToLowerInvariant()} could not be written in this version.");

    // ---- geometry ----------------------------------------------------------

    private readonly record struct Outcome(bool Compatible, bool Changed, Mat3? Pose = null)
    {
        public static Outcome Incompatible => new(false, false);
    }

    /// <summary>
    /// Brings an existing file object into line with the scene's.
    /// </summary>
    /// <param name="baseline">
    /// The handle whose recorded pose describes where <paramref name="file"/>
    /// currently stands, for the kinds that are moved rather than rewritten.
    /// </param>
    private Outcome Write(Entity file, SceneEntity entity, ulong baseline) => (entity, file) switch
    {
        (SLine line, AcadLine target) => WriteLine(line, target),
        (SArc arc, AcadArc target) => WriteArc(arc, target),
        (SCircle circle, AcadCircle target) when target is not AcadArc => WriteCircle(circle, target),
        (SEllipse ellipse, AcadEllipse target) => WriteEllipse(ellipse, target),
        (SPolyline polyline, LwPolyline target) => WriteLwPolyline(polyline, target),
        (SPolyline polyline, Polyline2D target) => WritePolyline2D(polyline, target),
        (SSpline spline, Spline target) => WriteSpline(spline, target),
        (SText text, MText target) => WriteMText(text, target),
        (SText text, TextEntity target) => WriteText(text, target),
        (SInsert insert, Insert target) => WriteInsert(insert, target),
        (SInsert insert, Dimension target) => WriteImportedDimension(insert, target, baseline),
        (SHatch hatch, Hatch or Solid) => WriteMoved(hatch, file, baseline),
        (SViewport viewport, Viewport target) => WriteViewport(viewport, target),
        (SDimension dimension, Dimension target) when Matches(dimension, target) => WriteDimension(dimension, target),
        _ => Outcome.Incompatible,
    };

    private static Outcome WriteLine(SLine line, AcadLine target)
    {
        if (Near(target.StartPoint, line.Start) && Near(target.EndPoint, line.End)) return new(true, false);

        target.StartPoint = ToXYZ(line.Start);
        target.EndPoint = ToXYZ(line.End);
        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    /// <summary>
    /// DWG arcs run counter-clockwise only, so one the scene holds with a
    /// negative sweep -- a mirror makes those -- is written from its far end.
    /// </summary>
    private static (double Start, double End) CounterClockwise(double start, double sweep) =>
        sweep < 0 ? (start + sweep, start) : (start, start + sweep);

    private static Outcome WriteArc(SArc arc, AcadArc target)
    {
        var (start, end) = CounterClockwise(arc.StartAngle, arc.Sweep);

        if (Near(target.Center, arc.Center) && Near(target.Radius, arc.Radius) &&
            SameAngle(target.StartAngle, start) && SameAngle(target.EndAngle, end))
            return new(true, false);

        target.Center = ToXYZ(arc.Center);
        target.Radius = arc.Radius;
        target.StartAngle = ArcMath.Normalize(start);
        target.EndAngle = ArcMath.Normalize(end);
        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    private static Outcome WriteCircle(SCircle circle, AcadCircle target)
    {
        if (Near(target.Center, circle.Center) && Near(target.Radius, circle.Radius)) return new(true, false);

        target.Center = ToXYZ(circle.Center);
        target.Radius = circle.Radius;
        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    /// <summary>
    /// The ellipse as DWG wants it: a ratio no more than one, and a span that
    /// runs counter-clockwise.
    /// </summary>
    private static (Vec2 Major, double Ratio, double Start, double End) EllipseForFile(SEllipse ellipse)
    {
        var major = ellipse.MajorAxis;
        double ratio = ellipse.Ratio;
        double start = ellipse.StartParameter;

        // A "major" axis shorter than the minor one is the other axis; the
        // parameter is then measured a quarter turn further round.
        if (ratio > 1)
        {
            major = new Vec2(-major.Y, major.X) * ratio;
            ratio = 1 / ratio;
            start -= Math.PI / 2;
        }

        var (s, e) = CounterClockwise(start, ellipse.Sweep);
        return (major, ratio, s, e);
    }

    private static Outcome WriteEllipse(SEllipse ellipse, AcadEllipse target)
    {
        var (major, ratio, start, end) = EllipseForFile(ellipse);
        bool closed = ellipse.IsClosed;

        if (Near(target.Center, ellipse.Center) && Near(target.MajorAxisEndPoint, major) &&
            Near(target.RadiusRatio, ratio) && SameAngle(target.StartParameter, start) &&
            (closed || SameAngle(target.EndParameter, end)))
            return new(true, false);

        target.Center = ToXYZ(ellipse.Center);
        target.MajorAxisEndPoint = ToXYZ(major);
        target.RadiusRatio = ratio;
        target.StartParameter = closed ? 0 : ArcMath.Normalize(start);
        target.EndParameter = closed ? ArcMath.TwoPi : ArcMath.Normalize(end);
        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    private static Outcome WriteLwPolyline(SPolyline polyline, LwPolyline target)
    {
        var vertices = polyline.Vertices;

        bool same = target.IsClosed == polyline.Closed && target.Vertices.Count == vertices.Length;
        for (int i = 0; same && i < vertices.Length; i++)
        {
            same = Near(ToVec2(target.Vertices[i].Location), vertices[i].Point) &&
                   Near(target.Vertices[i].Bulge, vertices[i].Bulge);
        }
        if (same) return new(true, false);

        if (target.Vertices.Count == vertices.Length)
        {
            // Same count: move them in place, which keeps any widths.
            for (int i = 0; i < vertices.Length; i++)
            {
                target.Vertices[i].Location = ToXY(vertices[i].Point);
                target.Vertices[i].Bulge = vertices[i].Bulge;
            }
        }
        else
        {
            target.Vertices.Clear();
            foreach (var v in vertices)
                target.Vertices.Add(new LwPolyline.Vertex(ToXY(v.Point)) { Bulge = v.Bulge });
        }

        target.IsClosed = polyline.Closed;
        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    private static Outcome WritePolyline2D(SPolyline polyline, Polyline2D target)
    {
        var vertices = polyline.Vertices;
        var existing = target.Vertices.ToList();

        // Vertices of the old-style polyline are objects with handles of their
        // own; adding or removing them is a different polyline, so a change of
        // count writes a lightweight one instead.
        if (existing.Count != vertices.Length) return Outcome.Incompatible;

        bool same = target.IsClosed == polyline.Closed;
        for (int i = 0; same && i < vertices.Length; i++)
            same = Near(existing[i].Location, vertices[i].Point) && Near(existing[i].Bulge, vertices[i].Bulge);
        if (same) return new(true, false);

        for (int i = 0; i < vertices.Length; i++)
        {
            existing[i].Location = ToXYZ(vertices[i].Point, existing[i].Location.Z);
            existing[i].Bulge = vertices[i].Bulge;
        }

        target.IsClosed = polyline.Closed;
        return new(true, true);
    }

    private static Outcome WriteSpline(SSpline spline, Spline target)
    {
        var points = spline.ControlPoints;
        var weights = spline.Weights;

        bool same = target.Degree == spline.Degree &&
                    target.ControlPoints.Count == points.Count &&
                    target.Knots.Count == spline.Knots.Count &&
                    target.IsClosed == spline.IsClosed;

        for (int i = 0; same && i < points.Count; i++) same = Near(target.ControlPoints[i], points[i]);
        for (int i = 0; same && i < spline.Knots.Count; i++) same = Near(target.Knots[i], spline.Knots[i]);

        if (same && weights is not null)
        {
            same = target.Weights.Count == weights.Count;
            for (int i = 0; same && i < weights.Count; i++) same = Near(target.Weights[i], weights[i]);
        }

        if (same) return new(true, false);

        target.ControlPoints.Clear();
        foreach (var p in points) target.ControlPoints.Add(ToXYZ(p));

        target.Knots.Clear();
        target.Knots.AddRange(spline.Knots);

        target.Weights.Clear();
        if (weights is not null) target.Weights.AddRange(weights);

        // The fit points described the old curve; left behind they would
        // describe a different one from the control points.
        target.FitPoints.Clear();

        target.Degree = spline.Degree;
        target.IsClosed = spline.IsClosed;
        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    /// <summary>The height a text actually draws at, as the reader works it out.</summary>
    private static double DrawnHeight(double own, TextStyle? style) =>
        style is { Height: > 0 } fixedHeight ? fixedHeight.Height : own;

    private Outcome WriteText(SText text, TextEntity target)
    {
        // TEXT is one line by definition. Text edited into several becomes
        // MTEXT, as AutoCAD would have had to make it; gluing the lines into
        // one would quietly lose the breaks.
        if (text.Lines.Count > 1) return Outcome.Incompatible;

        bool changed = WriteFont(target, text);

        // A new justification: which part of the text sits on its point. The
        // point itself is the scene's, so both of DWG's go there; AutoCAD
        // works the insertion point out again from the alignment point.
        if (DwgLoader.AnchorFor(target.HorizontalAlignment, target.VerticalAlignment) != (text.AnchorX, text.AnchorY))
        {
            (target.HorizontalAlignment, target.VerticalAlignment) = TextFactory.TextAlignmentFor(text.AnchorX, text.AnchorY);
            target.InsertPoint = ToXYZ(text.Position);
            target.AlignmentPoint = ToXYZ(text.Position);
            changed = true;
        }

        // DWG switches the anchor to the alignment point as soon as text is
        // anything but left-baseline; the reader follows it, so must this.
        bool aligned = target.HorizontalAlignment != TextHorizontalAlignment.Left ||
                       target.VerticalAlignment != TextVerticalAlignmentType.Baseline;

        var anchor = ToVec2(aligned ? target.AlignmentPoint : target.InsertPoint);
        double height = DrawnHeight(target.Height, target.Style);

        if (!Near(anchor, text.Position) || !SameAngle(target.Rotation, text.Rotation) || !Near(height, text.Height))
        {
            // Carry the other point along by the same move, turn and scale,
            // so fitted and aligned text keeps its proportions.
            double factor = height > 0 ? text.Height / height : 1;
            var move = Mat3.Translation(-anchor)
                * Mat3.Rotation(text.Rotation - target.Rotation)
                * Mat3.Scaling(factor)
                * Mat3.Translation(text.Position);

            if (aligned) target.InsertPoint = ToXYZ(move.Transform(ToVec2(target.InsertPoint)));
            else target.AlignmentPoint = ToXYZ(move.Transform(ToVec2(target.AlignmentPoint)));

            if (aligned) target.AlignmentPoint = ToXYZ(text.Position);
            else target.InsertPoint = ToXYZ(text.Position);

            target.Rotation = ArcMath.Normalize(text.Rotation);
            target.Normal = XYZ.AxisZ;
            SetHeight(target.Style, height, text.Height, h => target.Height = h);
            changed = true;
        }

        string value = string.Join(" ", text.Lines);
        if (!string.Equals(target.Value, value, StringComparison.Ordinal))
        {
            target.Value = value;
            changed = true;
        }

        return new(true, changed);
    }

    private Outcome WriteMText(SText text, MText target)
    {
        bool changed = WriteFont(target, text);

        if (DwgLoader.AnchorFor(target.AttachmentPoint) != (text.AnchorX, text.AnchorY))
        {
            target.AttachmentPoint = TextFactory.AttachmentFor(text.AnchorX, text.AnchorY);
            changed = true;
        }
        double height = DrawnHeight(target.Height, target.Style);

        if (!Near(target.InsertPoint, text.Position) || !SameAngle(target.Rotation, text.Rotation) ||
            !Near(height, text.Height))
        {
            double factor = height > 0 ? text.Height / height : 1;

            target.InsertPoint = ToXYZ(text.Position);
            target.AlignmentPoint = new XYZ(Math.Cos(text.Rotation), Math.Sin(text.Rotation), 0);
            target.RectangleWidth *= factor;
            target.Normal = XYZ.AxisZ;
            SetHeight(target.Style, height, text.Height, h => target.Height = h);
            changed = true;
        }

        // Rewritten only when the words changed: the reader strips MTEXT's
        // formatting, so writing the plain lines back would lose it.
        if (!text.Lines.SequenceEqual(target.GetPlainTextLines()))
        {
            target.Value = TextFactory.MTextValue(text.Lines);
            changed = true;
        }

        return new(true, changed);
    }

    private void SetHeight(TextStyle? style, double was, double now, Action<double> set)
    {
        if (Near(was, now)) return;

        if (style is { Height: > 0 })
        {
            _report.Note($"Text on style {style.Name} keeps the style's fixed height of {style.Height:0.###}.");
            return;
        }

        set(now);
    }

    private Outcome WriteInsert(SInsert insert, Insert target)
    {
        if (target.Block is null || target.Block.Handle != insert.Block.SourceHandle) return Outcome.Incompatible;

        var current = PlacementOf(target);
        if (Near(current, insert.Placement)) return new(true, false);

        if (target.ColumnCount > 1 || target.RowCount > 1)
        {
            _report.Note($"An array insert ({target.Handle:X}) was moved; MINSERT edits are not written.");
            return new(true, false);
        }

        // The attributes are separate objects in world space; they follow
        // the insert only if they are taken along.
        var move = Between(current, insert.Placement);
        Place(target, insert.Placement);
        foreach (var attribute in target.Attributes) Apply(attribute, move);

        target.Normal = XYZ.AxisZ;
        return new(true, true);
    }

    /// <summary>Where the reader put an imported dimension's picture.</summary>
    private static Mat3 PoseOf(Dimension dimension) =>
        SInsert.BuildTransform(ToVec2(dimension.Block?.BlockEntity?.BasePoint ?? XYZ.Zero),
            ToVec2(dimension.InsertionPoint), 1, 1, 0);

    private Outcome WriteImportedDimension(SInsert insert, Dimension target, ulong baseline)
    {
        var was = _state.Poses.TryGetValue(baseline, out var pose) ? pose : PoseOf(target);
        if (Near(was, insert.Placement)) return new(true, false, insert.Placement);

        Apply(target, Between(was, insert.Placement));
        return new(true, true, insert.Placement);
    }

    /// <summary>
    /// A hatch, or a SOLID: moved by however far the scene's copy has moved
    /// since it was read or last written.
    /// </summary>
    private Outcome WriteMoved(SHatch hatch, Entity target, ulong baseline)
    {
        var was = _state.Poses.TryGetValue(baseline, out var pose) ? pose : Mat3.Identity;
        if (Near(was, hatch.Moved)) return new(true, false, hatch.Moved);

        if (!Apply(target, Between(was, hatch.Moved)))
        {
            _report.Note($"A {target.ObjectName} could not be moved; it was left where it was.");
            return new(true, false, was);
        }

        return new(true, true, hatch.Moved);
    }

    private static Outcome WriteViewport(SViewport viewport, Viewport target)
    {
        var rect = viewport.PaperRect;
        var center = new Vec2((rect.Min.X + rect.Max.X) / 2, (rect.Min.Y + rect.Max.Y) / 2);

        if (Near(target.Center, center) && Near(target.Width, rect.Width) && Near(target.Height, rect.Height) &&
            Near(ToVec2(target.ViewCenter), viewport.ViewCenter) && Near(target.ViewHeight, viewport.ViewHeight) &&
            Near(target.TwistAngle, viewport.TwistAngle))
            return new(true, false);

        target.Center = ToXYZ(center, target.Center.Z);
        target.Width = rect.Width;
        target.Height = rect.Height;
        target.ViewCenter = ToXY(viewport.ViewCenter);
        target.ViewHeight = viewport.ViewHeight;
        target.TwistAngle = viewport.TwistAngle;
        return new(true, true);
    }

    // ---- dimensions --------------------------------------------------------

    private static bool Matches(SDimension dimension, Dimension target) => dimension.Kind switch
    {
        DimensionKind.Linear => target is DimensionLinear,
        DimensionKind.Aligned => target is DimensionAligned and not DimensionLinear,
        DimensionKind.Radius => target is DimensionRadius,
        DimensionKind.Diameter => target is DimensionDiameter,
        _ => false,
    };

    private static Dimension NewDimension(SDimension dimension) => dimension.Kind switch
    {
        DimensionKind.Linear => new DimensionLinear(),
        DimensionKind.Aligned => new DimensionAligned(),
        DimensionKind.Radius => new DimensionRadius(),
        _ => new DimensionDiameter(),
    };

    /// <summary>
    /// A drawn dimension, as a real DIMENSION: the definition points that let
    /// AutoCAD edit it, and a picture block drawn by the dimension itself so
    /// that what the file shows is what the editor showed.
    /// </summary>
    private Outcome WriteDimension(SDimension dimension, Dimension target)
    {
        var (geometry, fit) = dimension.Layout();
        bool changed = false;

        void Set(Func<XYZ> get, Action<XYZ> set, Vec2 value)
        {
            if (Near(get(), value)) return;
            set(ToXYZ(value));
            changed = true;
        }

        switch (target)
        {
            case DimensionAligned aligned:
                Set(() => aligned.FirstPoint, p => aligned.FirstPoint = p, dimension.First);
                Set(() => aligned.SecondPoint, p => aligned.SecondPoint = p, dimension.Second);
                Set(() => aligned.DefinitionPoint, p => aligned.DefinitionPoint = p, geometry.LineEnd);
                if (aligned is DimensionLinear linear && !SameAngle(linear.Rotation, dimension.Rotation))
                {
                    linear.Rotation = ArcMath.Normalize(dimension.Rotation);
                    changed = true;
                }
                break;

            case DimensionRadius radius:
                Set(() => radius.DefinitionPoint, p => radius.DefinitionPoint = p, dimension.First);
                Set(() => radius.AngleVertex, p => radius.AngleVertex = p, dimension.Second);
                break;

            case DimensionDiameter diameter:
                Set(() => diameter.DefinitionPoint, p => diameter.DefinitionPoint = p, dimension.First * 2 - dimension.Second);
                Set(() => diameter.AngleVertex, p => diameter.AngleVertex = p, dimension.Second);
                break;
        }

        Set(() => target.TextMiddlePoint, p => target.TextMiddlePoint = p, fit.TextAnchor);

        string text = dimension.TextOverride ?? "";
        if (!string.Equals(target.Text ?? "", text, StringComparison.Ordinal))
        {
            target.Text = text;
            changed = true;
        }

        if (changed || target.Block is null || target.Block.Entities.Count == 0) DrawPicture(dimension, target);
        return new(true, changed);
    }

    private void DrawPicture(SDimension dimension, Dimension target)
    {
        var sink = new BlockPictureSink();
        dimension.Emit(new EmitContext(sink, _drawing.Layers, 1.0), dimension.Style);

        if (target.Block is null)
            target.Block = new BlockRecord(AnonymousName("*D")) { IsAnonymous = true };

        target.Block.Entities.Clear();
        foreach (var entity in sink.Entities)
        {
            // Dimension pictures live on layer 0 and take their colour from
            // the dimension, as AutoCAD's own do.
            entity.Layer = _doc.Layers[AcadLayer.DefaultName];
            target.Block.Entities.Add(entity);
        }
    }

    // ---- new objects -------------------------------------------------------

    private Entity? Build(SceneEntity entity)
    {
        switch (entity)
        {
            case SLine line:
                return new AcadLine(ToXYZ(line.Start), ToXYZ(line.End));

            case SArc arc:
            {
                var (start, end) = CounterClockwise(arc.StartAngle, arc.Sweep);
                return new AcadArc
                {
                    Center = ToXYZ(arc.Center),
                    Radius = arc.Radius,
                    StartAngle = ArcMath.Normalize(start),
                    EndAngle = ArcMath.Normalize(end),
                };
            }

            case SCircle circle:
                return new AcadCircle { Center = ToXYZ(circle.Center), Radius = circle.Radius };

            case SEllipse ellipse:
            {
                var (major, ratio, start, end) = EllipseForFile(ellipse);
                return new AcadEllipse
                {
                    Center = ToXYZ(ellipse.Center),
                    MajorAxisEndPoint = ToXYZ(major),
                    RadiusRatio = ratio,
                    StartParameter = ellipse.IsClosed ? 0 : ArcMath.Normalize(start),
                    EndParameter = ellipse.IsClosed ? ArcMath.TwoPi : ArcMath.Normalize(end),
                };
            }

            case SPolyline polyline:
            {
                var file = new LwPolyline { IsClosed = polyline.Closed };
                foreach (var v in polyline.Vertices)
                    file.Vertices.Add(new LwPolyline.Vertex(ToXY(v.Point)) { Bulge = v.Bulge });
                return file;
            }

            case SSpline spline:
            {
                var file = new Spline { Degree = spline.Degree };
                WriteSpline(spline, file);
                return file;
            }

            case SText text:
            {
                var file = TextFactory.Build(text.Lines, text.Position, text.Height, ArcMath.Normalize(text.Rotation),
                    text.AnchorX, text.AnchorY, text.WidthFactor, text.LineStep, text.WrapWidth);

                // No typeface of its own: the drawing's current text style,
                // which is what AutoCAD would have placed it in.
                if (text.FontFamily is null && CurrentTextStyle() is { } current) SetStyle(file, current);
                else WriteFont(file, text);
                return file;
            }

            case SInsert insert:
            {
                if (!_doc.TryGetCadObject(insert.Block.SourceHandle, out CadObject found) || found is not BlockRecord block)
                    return null;

                var file = new Insert(block);
                Place(file, insert.Placement);
                return file;
            }

            case SDimension dimension:
            {
                var file = NewDimension(dimension);
                WriteDimension(dimension, file);
                return file;
            }

            case SHatch { IsSolid: true } hatch when hatch.Loops.Count > 0:
            {
                var file = new Hatch { IsSolid = true, Pattern = HatchPattern.Solid };
                foreach (var loop in hatch.Loops)
                {
                    var edge = new Hatch.BoundaryPath.Polyline { IsClosed = true };
                    foreach (var point in loop) edge.Vertices.Add(ToXYZ(point));
                    var path = new Hatch.BoundaryPath();
                    path.Edges.Add(edge);
                    file.Paths.Add(path);
                }
                return file;
            }

            default:
                return null;
        }
    }

    // ---- style -------------------------------------------------------------

    /// <summary>
    /// Puts the entity on its layer and gives it its colour, width and dash,
    /// saying ByLayer wherever the scene's value is simply the layer's.
    /// </summary>
    /// <remarks>
    /// The scene holds resolved styles, so ByLayer is not something it can
    /// say. Writing every colour out explicitly would turn every ByLayer
    /// entity touched by a save into one that no longer follows its layer;
    /// so nothing is written where the file already resolves to the scene's
    /// value, and a value equal to the layer's is written as ByLayer.
    /// </remarks>
    private bool WriteStyle(Entity file, SceneEntity entity)
    {
        bool changed = false;
        var sceneLayer = SceneLayerAt(entity.LayerIndex);
        var layer = FileLayer(entity.LayerIndex);

        if (!string.Equals(file.Layer?.Name, layer.Name, StringComparison.OrdinalIgnoreCase))
        {
            file.Layer = layer;
            changed = true;
        }

        if (sceneLayer is null) return changed;
        var style = entity.Style;

        if (!entity.Inherits.HasFlag(StyleInheritance.Color) &&
            StyleResolver.ToRgb(file.GetActiveColor(), sceneLayer.Color) != style.Color)
        {
            file.Color = style.Color == sceneLayer.Color ? Color.ByLayer : ToColor(style.Color);
            changed = true;
        }

        if (!entity.Inherits.HasFlag(StyleInheritance.Lineweight) &&
            !StyleResolver.ToLineweight(file.GetActiveLineWeightType(), sceneLayer.Lineweight).Equals(style.Lineweight))
        {
            file.LineWeight = style.Lineweight.Equals(sceneLayer.Lineweight)
                ? LineWeightType.ByLayer
                : (LineWeightType)style.Lineweight.Hundredths;
            changed = true;
        }

        if (!entity.Inherits.HasFlag(StyleInheritance.Linetype)) changed |= WriteLinetype(file, style, sceneLayer, layer);
        return changed;
    }

    private bool WriteLinetype(Entity file, DisplayStyle style, SceneLayer sceneLayer, AcadLayer layer)
    {
        string wanted = style.Linetype?.Name ?? LineType.ContinuousName;
        string current = file.LineType is null || IsNamed(file.LineType, LineType.ByLayerName)
            ? layer.LineType?.Name ?? LineType.ContinuousName
            : file.LineType.Name;

        if (IsNamed(file.LineType, LineType.ByBlockName) || string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            return false;

        string layerType = sceneLayer.Linetype?.Name ?? LineType.ContinuousName;
        string name = string.Equals(wanted, layerType, StringComparison.OrdinalIgnoreCase) ? LineType.ByLayerName : wanted;

        if (!_doc.LineTypes.TryGetValue(name, out var linetype))
        {
            _report.Note($"Linetype {wanted} is not in the file; the entity kept its own.");
            return false;
        }

        file.LineType = linetype;
        return true;
    }

    private static bool IsNamed(LineType? linetype, string name) =>
        linetype is not null && string.Equals(linetype.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An index colour where one is exactly this colour, a true colour
    /// otherwise. Index colours are what every DWG reader understands, and
    /// the reader turned them into exactly these RGB values on the way in.
    /// </summary>
    internal static Color ToColor(Rgb rgb) =>
        IndexByRgb.Value.TryGetValue(rgb, out short index) ? new Color(index) : new Color(rgb.R, rgb.G, rgb.B);

    /// <summary>
    /// The index colours by their RGB, first index winning. Built here rather
    /// than asked of <c>Color.ApproxIndex</c>, which adds up signed channel
    /// differences and so can answer with the wrong colour or none.
    /// </summary>
    private static readonly Lazy<Dictionary<Rgb, short>> IndexByRgb = new(() =>
    {
        var map = new Dictionary<Rgb, short>();
        for (short i = 1; i <= 255; i++)
        {
            var rgb = Color.GetIndexRGB((byte)i);
            map.TryAdd(new Rgb(rgb[0], rgb[1], rgb[2]), i);
        }
        return map;
    });

    // ---- text styles -------------------------------------------------------

    private readonly FontResolver _fonts = new();

    private TextStyle? CurrentTextStyle() =>
        _doc.TextStyles.TryGetValue(_doc.Header.CurrentTextStyleName, out var style) ? style : null;

    private static TextStyle? StyleOf(Entity text) => text switch
    {
        TextEntity t => t.Style,
        MText m => m.Style,
        _ => null,
    };

    private static void SetStyle(Entity text, TextStyle style)
    {
        if (text is TextEntity t) t.Style = style;
        else if (text is MText m) m.Style = style;
    }

    /// <summary>
    /// Puts text on a style in its typeface, if it has one of its own and is
    /// not already in it. Text with no typeface keeps whatever style it has.
    /// </summary>
    private bool WriteFont(Entity file, SText text)
    {
        if (text.FontFamily is not { } family) return false;

        var (current, bold, italic, _, _) = _fonts.Resolve(StyleOf(file));
        if (string.Equals(current, family, StringComparison.OrdinalIgnoreCase) &&
            bold == text.Bold && italic == text.Italic)
            return false;

        SetStyle(file, TextStyleFor(family, text.Bold, text.Italic));
        return true;
    }

    /// <summary>
    /// A text style in a TrueType typeface: found if the file has one that
    /// draws exactly that, made if not.
    /// </summary>
    /// <remarks>
    /// Made the way AutoCAD makes one, so AutoCAD draws it the same: the font
    /// file, and the face name with its bold and italic flags in the style's
    /// ACAD xdata -- which is where AutoCAD keeps them, rather than in the
    /// style's own fields. A style that draws the face at a fixed height is
    /// not reused, or the text would come out its height rather than its own.
    /// </remarks>
    private TextStyle TextStyleFor(string family, bool bold, bool italic)
    {
        string wanted = family + (bold ? " Bold" : "") + (italic ? " Italic" : "");

        for (int n = 1; ; n++)
        {
            string name = n == 1 ? wanted : $"{wanted} {n}";
            if (!_doc.TextStyles.TryGetValue(name, out var existing)) return MakeTextStyle(name, family, bold, italic);

            var (face, b, i, _, _) = _fonts.Resolve(existing);
            if (existing.Height == 0 && b == bold && i == italic &&
                string.Equals(face, family, StringComparison.OrdinalIgnoreCase))
                return existing;
        }
    }

    private TextStyle MakeTextStyle(string name, string family, bool bold, bool italic)
    {
        var style = new TextStyle(name)
        {
            Filename = FontResolver.FileOf(family) ?? family,
            TrueType = (bold ? FontFlags.Bold : 0) | (italic ? FontFlags.Italic : 0),
        };

        _doc.TextStyles.Add(style);

        if (!_doc.AppIds.TryGetValue(FontResolver.AcadAppId, out var app))
        {
            app = new AppId(FontResolver.AcadAppId);
            _doc.AppIds.Add(app);
        }

        // 0x22 is variable pitch, Swiss family -- what AutoCAD writes for an
        // ordinary sans serif -- with the bold and italic bits above it.
        int flags = 0x22 | (bold ? FontResolver.BoldFlag : 0) | (italic ? FontResolver.ItalicFlag : 0);
        style.ExtendedData.Add(app, new ExtendedDataRecord[]
        {
            new ExtendedDataString(family),
            new ExtendedDataInteger32(flags),
        });

        return style;
    }

    // ---- header ------------------------------------------------------------

    private void SyncHeader()
    {
        var header = _doc.Header;

        // Only when it has changed: a file in a unit this editor does not
        // model reads as Unitless, and writing that back would erase it.
        if (DwgLoader.UnitsOf(header.InsUnits) != _drawing.Units)
            header.InsUnits = DwgLoader.InsUnitsOf(_drawing.Units);

        if (_drawing.CurrentLayer is { } current && _doc.Layers.Contains(current.Name))
            header.CurrentLayerName = current.Name;

        SyncDimensionSettings(header);
        SyncTextSettings(header);
    }

    /// <summary>
    /// The DIM variables, so the file's next dimension -- drawn here or in
    /// AutoCAD -- comes out the size this drawing's did.
    /// </summary>
    /// <remarks>
    /// The header rather than the dimension style table: these are the
    /// current settings, which AutoCAD applies to new dimensions only. Editing
    /// the style itself would quietly resize every dimension already using it
    /// the next time AutoCAD regenerates them, which is what the dialog's own
    /// "apply to existing" box is for, and only when it is ticked.
    /// </remarks>
    /// <summary>
    /// TEXTSIZE and the current text style, so the file's next text comes
    /// out the way the drawing's did. A height still following the dimension
    /// text is written as the number it follows, which is what it is now.
    /// </summary>
    private void SyncTextSettings(ACadSharp.Header.CadHeader header)
    {
        var text = _drawing.Text;
        double height = text.Height ?? FreeDwg.Core.Scene.DimensionStyle.For(_drawing).TextHeight;

        if (height > 0 && !Near(header.TextHeightDefault, height)) header.TextHeightDefault = height;

        // Only a typeface the drawing has been given that the file does not
        // already say: re-finding the current style's own font on every save
        // would add a style and switch to it in a file nobody touched.
        var fromFile = DwgLoader.TextSettingsOf(header, _doc);
        bool sameFace = string.Equals(fromFile.FontFamily, text.FontFamily, StringComparison.OrdinalIgnoreCase) &&
                        fromFile.Bold == text.Bold && fromFile.Italic == text.Italic;

        if (!sameFace && text.FontFamily is { } family)
        {
            var style = TextStyleFor(family, text.Bold, text.Italic);
            if (!string.Equals(header.CurrentTextStyleName, style.Name, StringComparison.OrdinalIgnoreCase))
                header.CurrentTextStyleName = style.Name;
        }
    }

    private void SyncDimensionSettings(ACadSharp.Header.CadHeader header)
    {
        if (_drawing.Dimensions is not { IsValid: true } settings) return;
        if (DwgLoader.DimensionSettingsOf(header, _drawing.Units) == settings) return;

        var sizes = settings.Sizes;
        header.DimensionTextHeight = sizes.TextHeight;
        header.DimensionArrowSize = sizes.ArrowSize;
        header.DimensionExtensionLineOffset = sizes.ExtensionOffset;
        header.DimensionExtensionLineExtension = sizes.ExtensionBeyond;
        header.DimensionLineGap = sizes.TextGap;
        header.DimensionDecimalPlaces = (short)sizes.Decimals;
        header.DimensionScaleFactor = settings.Scale;
    }
}
