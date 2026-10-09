using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Editing;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Snapping;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Tools;
using FreeDWGEditorThatWorks.Rendering;
using SceneDrawing = FreeDwg.Core.Scene.Drawing;

namespace FreeDWGEditorThatWorks.Controls;

/// <summary>
/// The model-space viewport. A single element that paints the whole scene in
/// <see cref="OnRender"/> -- one Shape per entity would put tens of thousands
/// of objects in the visual tree and fall over.
/// </summary>
public sealed class CadCanvas : FrameworkElement
{
    private SceneDrawing? _drawing;
    private SolidColorBrush _backgroundBrush = Brushes.Black;
    private uint _backgroundBrushKey = uint.MaxValue;

    private bool _isPanning;
    private bool _panDragged;
    private Point _panAnchor;
    private bool _zoomExtentsPending;

    /// <summary>
    /// What the left button is in the middle of, if anything.
    /// </summary>
    /// <remarks>
    /// One field for the same reason <see cref="Mode"/> is one enum: a bool
    /// per kind of drag lets two of them be true, and then a mouse-up has
    /// two answers for what it just finished. That shape has already cost
    /// this codebase a toolbar that lit two buttons at once.
    /// </remarks>
    private enum LeftGesture
    {
        None,

        /// <summary>Pressed, but not yet dragged far enough to be a band.</summary>
        Pick,

        /// <summary>Dragging a selection window, or a zoom window.</summary>
        Band,

        /// <summary>Dragging a grip on something already selected.</summary>
        Grip,
    }

    private LeftGesture _gesture;
    private Point _pickAnchor;
    private Point _bandCorner;

    private readonly GripSet _grips = new();
    private EntityGrip? _heldGrip;
    private EntityGrip? _hoverGrip;

    private CanvasTool? _tool;
    private Vec2 _cursor;
    private Vec2 _snapped;
    private SnapResult _snap;

    /// <summary>Where the next point would land before any typed size holds it.</summary>
    private Vec2 _aimed;

    /// <summary>
    /// The sides of the rectangle being drawn that have been typed. Null
    /// follows the cursor. They belong to the one corner being placed and go
    /// with it: placed, cancelled, or a new tool.
    /// </summary>
    private double? _lockedWidth, _lockedHeight;

    /// <summary>How far a click may miss by, in device pixels.</summary>
    private const double PickRadiusPixels = 6.0;

    /// <summary>Drag further than this and a click becomes a selection window.</summary>
    private const double DragThresholdPixels = 4.0;

    /// <summary>Colour the tool draws in while the entity is still being picked.</summary>
    private static readonly Rgb PreviewColor = new(255, 190, 90);

    /// <summary>How near the cursor a snap point has to be, in device pixels.</summary>
    private const double SnapRadiusPixels = 12.0;

    /// <summary>How near a grip the cursor has to be to take hold of it, in device pixels.</summary>
    private const double GripRadiusPixels = 7.0;

    /// <summary>The side of a grip square, in device pixels.</summary>
    private const double GripSizePixels = 7.0;

    private static readonly Pen SnapPen = MakeSnapPen();
    private static readonly Pen GuidePen = MakeGuidePen();
    private static readonly Pen GridPen = MakeGridPen(60);
    private static readonly Pen GridAxisPen = MakeGridPen(105);

    private static Pen MakeSnapPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 230, 140)), 1.6);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeGuidePen()
    {
        // Dashed and faint: a guide explains the answer without competing
        // with the drawing it is laid over.
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 120, 230, 140)), 1.0)
        {
            DashStyle = new DashStyle([5, 4], 0),
        };

        pen.Freeze();
        return pen;
    }

    private static Pen MakeGridPen(byte alpha)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 128, 140, 155)), 1.0);
        pen.Freeze();
        return pen;
    }

    // The selection colour, so a handle plainly belongs to the thing that is
    // lit up; the held one goes warm, which is the CAD convention for a grip
    // that is about to move.
    private static readonly Brush GripFill = MakeGripFill(Color.FromRgb(90, 175, 255));
    private static readonly Brush HotGripFill = MakeGripFill(Color.FromRgb(255, 120, 80));
    private static readonly Pen GripEdge = MakeGripEdge();

    private static Brush MakeGripFill(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen MakeGripEdge()
    {
        // A dark outline so a square still reads against geometry of its own
        // colour, which a selected blue line is by definition.
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 12, 16, 24)), 1.0);
        pen.Freeze();
        return pen;
    }

    private static readonly Pen WindowPen = MakeBandPen(Color.FromRgb(90, 150, 255), dashed: false);
    private static readonly Pen CrossingPen = MakeBandPen(Color.FromRgb(110, 220, 120), dashed: true);
    private static readonly Brush WindowFill = MakeBandFill(Color.FromRgb(90, 150, 255));
    private static readonly Brush CrossingFill = MakeBandFill(Color.FromRgb(110, 220, 120));

    private static Pen MakeBandPen(Color color, bool dashed)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.0);
        if (dashed) pen.DashStyle = new DashStyle([4, 3], 0);
        pen.Freeze();
        return pen;
    }

    private static Brush MakeBandFill(Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public CadCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        Selection.Changed += (_, _) =>
        {
            RefreshGrips();
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public Camera Camera { get; } = new();
    public RenderSettings Settings { get; } = new();
    public RenderStats LastStats { get; private set; }

    /// <summary>What is selected. Survives a redraw; cleared when the drawing changes.</summary>
    public Selection Selection { get; } = new();

    /// <summary>
    /// Undo stack for the drawing on show. Every change goes through it, so
    /// it doubles as the record of what a save would have to write.
    /// </summary>
    public CommandStack? Commands { get; private set; }

    /// <summary>
    /// The tool in force, or null unless <see cref="Mode"/> is
    /// <see cref="CanvasMode.Draw"/>.
    /// </summary>
    public CanvasTool? Tool => _tool;

    /// <summary>
    /// What a left click does. Exactly one thing, always.
    /// </summary>
    /// <remarks>
    /// This exists because the alternative did not work: a tool flag and a
    /// separate zoom-window flag could both be set, leaving two buttons lit
    /// and one of them lying about what the next click would do. One field,
    /// one setter, one event -- so the toolbar cannot disagree with the
    /// canvas even if a caller forgets to reset something.
    /// </remarks>
    public CanvasMode Mode { get; private set; } = CanvasMode.Select;

    /// <summary>Object snap, ortho and the grid.</summary>
    public SnapEngine Snapping { get; } = new();

    /// <summary>
    /// Radius of a filleted corner. Zero means a sharp one.
    /// </summary>
    /// <remarks>
    /// Held here rather than copied into each tool as it is built, because
    /// two copies of a setting drift: one path sets it at construction and
    /// the other on change, and whichever is forgotten is a size that
    /// silently does nothing. The canvas owns it and applies it, so a tool
    /// started before or after the number is typed behaves the same.
    /// <para>
    /// Separate from <see cref="ChamferDistance"/>. They were one number
    /// once, which meant setting a 2 mm chamfer quietly changed every fillet
    /// after it to 2 mm as well -- they are different measurements of
    /// different things and only look alike.
    /// </para>
    /// </remarks>
    public double FilletRadius
    {
        get => _filletRadius;
        set => SetCornerSize(ref _filletRadius, value);
    }

    /// <summary>How far back along each edge a chamfer cuts.</summary>
    public double ChamferDistance
    {
        get => _chamferDistance;
        set => SetCornerSize(ref _chamferDistance, value);
    }

    private double _filletRadius;
    private double _chamferDistance;

    private void SetCornerSize(ref double field, double value)
    {
        if (field == value) return;

        field = value;
        ApplyToolSettings();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// How big the marks on a new dimension are, and how its number reads.
    /// Taken from the drawing, since both answers are the drawing's: ISO
    /// sizes are millimetres of paper, and a drawing in metres wants the
    /// same marks at a thousandth of the number.
    /// </summary>
    public DimensionStyle DimensionStyle =>
        _drawing is null ? DimensionStyle.Iso : DimensionStyle.For(_drawing);

    /// <summary>
    /// Pushes every setting the canvas owns into the tool in force.
    /// </summary>
    /// <remarks>
    /// One method rather than one per setting, and called from one place
    /// besides the setters, because the failure this guards against is a
    /// setting that was applied down one path and forgotten down the other
    /// -- a size that silently does nothing depending on whether the tool
    /// was picked before or after the number was typed.
    /// </remarks>
    private void ApplyToolSettings()
    {
        // Chamfer derives from fillet, so it has to be asked about first.
        if (_tool is ChamferTool chamfer) chamfer.Radius = _chamferDistance;
        else if (_tool is FilletTool fillet) fillet.Radius = _filletRadius;

        if (_tool is DimensionTool dimension) dimension.Sizes = DimensionStyle;
    }

    /// <summary>
    /// Re-reads the settings that come from the document, for the shell to
    /// call when the units or the precision change under an active tool.
    /// </summary>
    public void RefreshToolSettings()
    {
        ApplyToolSettings();
        InvalidateVisual();
    }

    /// <summary>Back to the pointer.</summary>
    public void UseSelect() => SetMode(CanvasMode.Select, null);

    /// <summary>
    /// Starts <paramref name="tool"/>. Draw and modify tools share a mode:
    /// both are "a click gives the tool its next point", and the only
    /// difference is what comes back at the end.
    /// </summary>
    public void UseTool(CanvasTool tool) => SetMode(CanvasMode.Draw, tool);

    /// <summary>Arms a one-shot zoom window; the next drag frames the view.</summary>
    public void UseZoomWindow() => SetMode(CanvasMode.ZoomWindow, null);

    private void SetMode(CanvasMode mode, CanvasTool? tool)
    {
        _tool?.Cancel();
        AbandonGesture();

        _tool = tool;
        Mode = mode;
        ClearSizeLocks();

        // Drawing and selecting are different modes, and carrying a selection
        // into a draw tool only makes the next Delete a surprise. A modify
        // tool is the exception: the selection is what it acts on.
        if (mode != CanvasMode.Select && tool?.NeedsSelection != true) Selection.Clear();

        _snap = default;

        // A new tool starts with a clean slate: points acquired while drawing
        // the last thing are rarely what the next one wants to line up with.
        Snapping.ClearTracking();

        // Whatever the tool was built with, the canvas's figures win: there
        // is one copy of each of them and it is here.
        ApplyToolSettings();

        // Grips belong to the pointer. A modify tool acts on the selection as
        // a whole, so handles on its parts would offer an edit that the next
        // click is not going to make.
        RefreshGrips();

        Cursor = mode == CanvasMode.Select ? Cursors.Arrow : Cursors.Cross;

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What is being asked for next, for the status bar. A held grip has
    /// something to say as much as a tool mid-pick does, and the status bar
    /// should not have to know which of the two is talking.
    /// </summary>
    public string? ToolPrompt => _heldGrip is not null
        ? "Grip: pick where it goes, or type a distance"
        : _tool?.Prompt;

    /// <summary>Pick tolerance in world units at the current zoom.</summary>
    public double PickTolerance => Camera.Scale > 0 ? PickRadiusPixels / Camera.Scale : 0;

    /// <summary>Snap radius in world units at the current zoom.</summary>
    public double SnapTolerance => Camera.Scale > 0 ? SnapRadiusPixels / Camera.Scale : 0;

    /// <summary>Grip radius in world units at the current zoom.</summary>
    public double GripTolerance => Camera.Scale > 0 ? GripRadiusPixels / Camera.Scale : 0;

    /// <summary>Where the snap marker is showing, if one is.</summary>
    public SnapResult ActiveSnap => _snap;

    /// <summary>
    /// What the box beside the cursor is editing, if anything.
    /// </summary>
    /// <remarks>
    /// One question with one answer, for the reason <see cref="Mode"/> is
    /// one enum. A length while a point is being placed, and otherwise the
    /// size the corner tools work to -- which used to be reachable only from
    /// a row in the properties panel, on the far side of the window from
    /// where the user is looking.
    /// </remarks>
    public CursorEntry Entry
    {
        get
        {
            if (PendingFrom is not null)
                return _heldGrip is null && _tool is RectangleTool ? CursorEntry.Size : CursorEntry.Length;

            return _tool switch
            {
                ChamferTool => CursorEntry.Distance,
                FilletTool => CursorEntry.Radius,
                _ => CursorEntry.None,
            };
        }
    }

    /// <summary>The number in that box.</summary>
    public double? EntryValue => Entry switch
    {
        CursorEntry.Length => PendingLength,
        CursorEntry.Size => PendingSize?.X,
        CursorEntry.Radius => _filletRadius,
        CursorEntry.Distance => _chamferDistance,
        _ => null,
    };

    /// <summary>
    /// Takes a number typed into that box, wherever it belongs. False when
    /// there was nowhere for it to go.
    /// </summary>
    public bool ApplyEntry(double value)
    {
        switch (Entry)
        {
            case CursorEntry.Length:
                return PlaceTypedLength(value);

            case CursorEntry.Size:
                LockSize(value, _lockedHeight);
                return PlaceSizedCorner();

            case CursorEntry.Radius when value >= 0:
                FilletRadius = value;
                return true;

            case CursorEntry.Distance when value >= 0:
                ChamferDistance = value;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// The point being measured from: where the tool last picked, or where
    /// the grip being dragged started. Null when nothing is being placed.
    /// </summary>
    public Vec2? PendingFrom => _heldGrip is { } held
        ? held.Point
        : _tool is { InProgress: true } tool ? tool.Points[^1] : null;

    /// <summary>
    /// Where the next point would land as things stand, in world units.
    /// </summary>
    public Vec2 PendingPoint => _snapped;

    /// <summary>
    /// Where the cursor is, unsnapped. What the overlay follows when the
    /// number it is showing belongs to the tool rather than to a point.
    /// </summary>
    public Vec2 CursorWorld => _cursor;

    /// <summary>How long the run being drawn currently is.</summary>
    public double? PendingLength =>
        PendingFrom is { } from ? Vec2.Distance(from, _snapped) : null;

    /// <summary>The width and height the rectangle being drawn would have.</summary>
    public Vec2? PendingSize =>
        Entry == CursorEntry.Size && PendingFrom is { } from
            ? new Vec2(Math.Abs(_snapped.X - from.X), Math.Abs(_snapped.Y - from.Y))
            : null;

    /// <summary>The rectangle's typed width, if one has been typed.</summary>
    public double? LockedWidth => _lockedWidth;

    /// <summary>The rectangle's typed height, if one has been typed.</summary>
    public double? LockedHeight => _lockedHeight;

    /// <summary>
    /// Holds the rectangle being drawn to a typed width, height or both;
    /// null lets that side follow the cursor again.
    /// </summary>
    /// <remarks>
    /// Applied at once rather than on Enter, so the preview shows the size
    /// being typed while the other side is still being aimed with the mouse.
    /// </remarks>
    public void LockSize(double? width, double? height)
    {
        if (Entry != CursorEntry.Size) return;

        _lockedWidth = width is > 0 ? width : null;
        _lockedHeight = height is > 0 ? height : null;
        ApplySizeLocks();

        InvalidateVisual();
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Places the rectangle's opposite corner where the typed sizes and the
    /// cursor together put it. False when no rectangle is being drawn.
    /// </summary>
    public bool PlaceSizedCorner()
    {
        if (Entry != CursorEntry.Size) return false;

        ApplySizeLocks();
        PlaceToolPoint(_snapped);
        return true;
    }

    private void ClearSizeLocks() => _lockedWidth = _lockedHeight = null;

    /// <summary>
    /// Holds the aimed point to the typed sides. A point moved off what it
    /// snapped to is no longer on it, so the snap marker goes.
    /// </summary>
    private void ApplySizeLocks()
    {
        _snapped = _aimed;
        if (_lockedWidth is null && _lockedHeight is null) return;
        if (Entry != CursorEntry.Size || PendingFrom is not { } corner) return;

        var held = RectangleTool.Constrain(corner, _aimed, _lockedWidth, _lockedHeight);
        if (held == _snapped) return;

        _snapped = held;
        _snap = default;
    }

    /// <summary>
    /// Places the next point at a typed distance, keeping the direction the
    /// cursor is pointing in.
    /// </summary>
    /// <remarks>
    /// Direction from the cursor and length from the keyboard is how CAD has
    /// always taken a measured line: aim it, with whatever snapping is on to
    /// hold the angle, and say how long. It needs no separate angle field to
    /// be useful, because polar tracking already sets the angle exactly.
    /// </remarks>
    public bool PlaceTypedLength(double length)
    {
        if (length <= 0) return false;

        // A grip drag has exactly the same shape as a picked point -- the
        // cursor aims it and the keyboard says how far -- so it takes the
        // typed distance too, and a vertex can be pulled out by 50.
        if (_heldGrip is { } held)
        {
            var aim = (_snapped - held.Point).Normalized();
            if (aim.LengthSquared <= 0) return false;

            return EndGripDrag(held.Point + aim * length);
        }

        if (_tool is not { InProgress: true } tool) return false;

        var from = tool.Points[^1];
        var direction = (_snapped - from).Normalized();

        // With the cursor still on the last point there is no direction to
        // run in, and no sensible answer to give.
        if (direction.LengthSquared <= 0) return false;

        PlaceToolPoint(from + direction * length);
        return true;
    }

    /// <summary>
    /// Where a point picked at <paramref name="world"/> actually lands, once
    /// object snap, ortho and the grid have had their say.
    /// </summary>
    /// <remarks>
    /// Everything that turns a cursor position into a point goes through
    /// here -- the preview and the committed point both -- so what is drawn
    /// under the cursor is exactly what gets built.
    /// </remarks>
    public Vec2 ResolvePoint(Vec2 world)
    {
        if (_drawing is null) return world;

        // A held grip is the point being moved, so ortho and polar are
        // measured from where it started -- pulling a vertex straight up is
        // the same gesture as drawing straight up.
        Vec2? from = PendingFrom;

        // The point before that, so polar angles can be measured from the
        // segment just drawn rather than from the horizon.
        Vec2? before = _heldGrip is null && _tool is { InProgress: true, Points.Count: >= 2 } run
            ? run.Points[^2]
            : null;

        _snap = Snapping.Resolve(_drawing.ActiveLayout, _drawing.Layers,
            world, SnapTolerance, from, Camera.Scale, before);

        AcquireForTracking();

        // The current aim is set here and nowhere else. It used to be the
        // mouse handler's business, which left this method half doing its
        // job: anything calling it directly got the answer back but left the
        // preview, the length readout and a typed length all looking at the
        // previous position.
        _aimed = _snap.Point;
        ApplySizeLocks();
        return _snapped;
    }

    /// <summary>
    /// Remembers a point the cursor is sitting on, so that a later point can
    /// be lined up with it.
    /// </summary>
    /// <remarks>
    /// Only the points that belong to an object: a grid crossing is already
    /// on a grid of alignments, and tracking from one would offer a line the
    /// grid itself draws. Tracking from a tracked point would compound too.
    /// </remarks>
    private void AcquireForTracking()
    {
        if (!Snapping.Modes.HasFlag(SnapModes.Tracking)) return;

        if (_snap.Kind is SnapKind.Endpoint or SnapKind.Midpoint
                       or SnapKind.Center or SnapKind.Quadrant)
        {
            Snapping.Acquire(_snap.Point);
        }
    }

    private bool ZoomWindowArmed => Mode == CanvasMode.ZoomWindow;

    /// <summary>
    /// Whether a point is being placed: a tool is picking, or a grip is
    /// being dragged. Both want snapping resolved on every move and the snap
    /// marker drawn, and one answer keeps them from drifting apart.
    /// </summary>
    private bool IsPlacingPoint => Mode == CanvasMode.Draw || _heldGrip is not null;

    /// <summary>Fires with the cursor position in world units.</summary>
    public event EventHandler<Vec2>? CursorMoved;

    /// <summary>Fires after any pan or zoom, so the shell can refresh its readout.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Fires after the selection changes, for the same reason.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Fires when the mode or the active tool changes, or when the tool wants
    /// a different point. The shell syncs every toolbar button from this, so
    /// there is one source of truth for what is lit.
    /// </summary>
    public event EventHandler? ModeChanged;

    /// <summary>
    /// Fires when a tool is mid-pick and the cursor has moved, so an overlay
    /// showing the length can keep up.
    /// </summary>
    public event EventHandler? PendingPointChanged;

    /// <summary>
    /// Fires when a digit is typed while a tool is picking, carrying what was
    /// typed. The shell opens its length box on it.
    /// </summary>
    public event EventHandler<string>? LengthTypingStarted;

    /// <summary>
    /// Fires when Tab is pressed while there is a size to type, so the shell
    /// can open its box on the first field without anything typed yet.
    /// </summary>
    public event EventHandler? EntryFieldRequested;

    /// <summary>Fires after the drawing is edited, so the shell can refresh counts.</summary>
    public event EventHandler? DrawingEdited;

    public SceneDrawing? Drawing
    {
        get => _drawing;
        set
        {
            _drawing = value;
            SetMode(CanvasMode.Select, null);
            Selection.Clear();

            // The stack describes one document; a new document starts clean.
            Commands = value is null ? null : new CommandStack(value);
            ZoomExtents();
        }
    }

    public void ZoomExtents()
    {
        SyncViewport();

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            // Called before the first layout pass; retry once we have a size.
            _zoomExtentsPending = true;
            return;
        }

        _zoomExtentsPending = false;
        Camera.ZoomToFit(_drawing?.Bounds ?? Bounds2.Empty);
        Redraw();
    }

    public void Redraw()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_zoomExtentsPending) ZoomExtents();
        else InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Also makes the element hit-testable, which a FrameworkElement with no
        // painted background is not.
        dc.DrawRectangle(GetBackgroundBrush(), null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_drawing is null) return;

        SyncViewport();

        // Under the scene: a grid drawn over the drawing would compete with it.
        if (Snapping.Grid.IsVisible) DrawGrid(dc);

        var sink = new WpfDrawingSink(dc, Camera, Settings, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        LastStats = SceneRenderer.Render(_drawing, Camera, sink,
            Selection.IsEmpty ? null : Selection.Entities, Settings);

        if (_tool is not null) DrawToolPreview(sink);
        DrawGripPreview(sink);

        // Over the preview: a grip is a target for the mouse, and one hidden
        // under the line work it belongs to is not one you can aim at.
        if (!_grips.IsEmpty) DrawGrips(dc);

        if (_gesture == LeftGesture.Band) DrawSelectionBand(dc);
        if (_snap.Found && IsPlacingPoint)
        {
            DrawTrackingGuides(dc);
            DrawSnapMarker(dc);
        }
    }

    /// <summary>
    /// The dashed lines back to the points an answer was lined up with.
    /// </summary>
    /// <remarks>
    /// Without them a point placed level with a corner on the far side of
    /// the sheet is indistinguishable from one placed by hand nearby, and
    /// the user has no way to tell which they got.
    /// </remarks>
    private void DrawTrackingGuides(DrawingContext dc)
    {
        if (_snap.Guides.Count == 0) return;

        Vec2 to = Camera.WorldToScreen(_snap.Point);

        foreach (var guide in _snap.Guides)
        {
            Vec2 from = Camera.WorldToScreen(guide);
            dc.DrawLine(GuidePen, new Point(from.X, from.Y), new Point(to.X, to.Y));

            // A small tick on the acquired point itself, so it is clear what
            // the line is coming from rather than just where it ends.
            dc.DrawLine(GuidePen, new Point(from.X - 4, from.Y), new Point(from.X + 4, from.Y));
            dc.DrawLine(GuidePen, new Point(from.X, from.Y - 4), new Point(from.X, from.Y + 4));
        }
    }

    /// <summary>
    /// The grid, in device space and thinned to suit the zoom. Drawing it
    /// through the sink would put it in the scene's coordinate space and its
    /// line weights, when what it wants is one hairline whatever the zoom.
    /// </summary>
    private void DrawGrid(DrawingContext dc)
    {
        double spacing = Snapping.Grid.EffectiveSpacing(Camera.Scale);
        if (spacing <= 0) return;

        var visible = Camera.VisibleWorldBounds;

        foreach (double x in Grid.LinesAcross(visible.MinX, visible.MaxX, spacing))
        {
            double device = Camera.WorldToScreen(new Vec2(x, 0)).X;
            var pen = Math.Abs(x) < spacing / 2 ? GridAxisPen : GridPen;
            dc.DrawLine(pen, new Point(device, 0), new Point(device, ActualHeight));
        }

        foreach (double y in Grid.LinesAcross(visible.MinY, visible.MaxY, spacing))
        {
            double device = Camera.WorldToScreen(new Vec2(0, y)).Y;
            var pen = Math.Abs(y) < spacing / 2 ? GridAxisPen : GridPen;
            dc.DrawLine(pen, new Point(0, device), new Point(ActualWidth, device));
        }
    }

    /// <summary>
    /// The marker for whatever the cursor has caught: a square for an
    /// endpoint, a triangle for a midpoint, a circle for a centre, a diamond
    /// for a quadrant. Not decoration -- it is the only way to tell that a
    /// point landed on the thing you aimed at rather than a pixel away.
    /// </summary>
    private void DrawSnapMarker(DrawingContext dc)
    {
        Vec2 at = Camera.WorldToScreen(_snap.Point);
        const double r = 5.0;

        switch (_snap.Kind)
        {
            case SnapKind.Endpoint:
                dc.DrawRectangle(null, SnapPen, new Rect(at.X - r, at.Y - r, r * 2, r * 2));
                return;

            case SnapKind.Midpoint:
                DrawPolygon(dc, [(at.X - r, at.Y + r), (at.X, at.Y - r), (at.X + r, at.Y + r)]);
                return;

            case SnapKind.Center:
                dc.DrawEllipse(null, SnapPen, new Point(at.X, at.Y), r, r);
                return;

            case SnapKind.Quadrant:
                DrawPolygon(dc, [(at.X, at.Y - r * 1.3), (at.X + r * 1.3, at.Y),
                                 (at.X, at.Y + r * 1.3), (at.X - r * 1.3, at.Y)]);
                return;

            case SnapKind.Grid:
            case SnapKind.Tracking:
                dc.DrawLine(SnapPen, new Point(at.X - r, at.Y), new Point(at.X + r, at.Y));
                dc.DrawLine(SnapPen, new Point(at.X, at.Y - r), new Point(at.X, at.Y + r));
                return;

            // A right angle, the way it is marked on a drawing.
            case SnapKind.Perpendicular:
                dc.DrawLine(SnapPen, new Point(at.X - r, at.Y - r), new Point(at.X - r, at.Y + r));
                dc.DrawLine(SnapPen, new Point(at.X - r, at.Y + r), new Point(at.X + r, at.Y + r));
                dc.DrawLine(SnapPen, new Point(at.X - r, at.Y), new Point(at.X, at.Y));
                dc.DrawLine(SnapPen, new Point(at.X, at.Y), new Point(at.X, at.Y + r));
                return;

            // An X on the crossing, as a drawing marks one.
            case SnapKind.Intersection:
                dc.DrawLine(SnapPen, new Point(at.X - r, at.Y - r), new Point(at.X + r, at.Y + r));
                dc.DrawLine(SnapPen, new Point(at.X + r, at.Y - r), new Point(at.X - r, at.Y + r));
                return;

            // A circle with the line it touches lying across the top.
            case SnapKind.Tangent:
                dc.DrawEllipse(null, SnapPen, new Point(at.X, at.Y + 1), r, r - 1);
                dc.DrawLine(SnapPen, new Point(at.X - r, at.Y - r), new Point(at.X + r, at.Y - r));
                return;
        }
    }

    private static void DrawPolygon(DrawingContext dc, (double X, double Y)[] points)
    {
        var geometry = new StreamGeometry();

        using (var figure = geometry.Open())
        {
            figure.BeginFigure(new Point(points[0].X, points[0].Y), isFilled: false, isClosed: true);
            for (int i = 1; i < points.Length; i++)
                figure.LineTo(new Point(points[i].X, points[i].Y), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, SnapPen, geometry);
    }

    /// <summary>
    /// The entity as it would be if the cursor were the next point, drawn
    /// through the same sink as the scene. A preview that went through its
    /// own drawing path would be free to disagree with the result, which is
    /// exactly what a preview must not do.
    /// </summary>
    private void DrawToolPreview(IDrawingSink sink)
    {
        if (_tool is null || _drawing is null) return;

        // A circle dimension has nothing picked and plenty to show: it is
        // holding a circle rather than a list of points.
        bool started = _tool.InProgress || _tool is CircleDimensionTool { HasSubject: true };
        if (!started && _tool is not ModifyTool) return;

        var context = new EmitContext(sink, _drawing.Layers, Camera.Scale);
        var style = new DisplayStyle(PreviewColor, Lineweight.Default);

        if (_tool is ModifyTool modify)
        {
            DrawModifyPreview(modify, context, style);
            return;
        }

        // An entity tool has no rubber geometry: what it has picked so far is
        // shown by the selection highlight instead.
        if (_tool is EntityTool) return;

        _tool.Preview(_snapped, context, style);
    }

    /// <summary>
    /// The selection drawn where the pending transform would put it.
    /// </summary>
    /// <remarks>
    /// Done here rather than in the tool because it needs the selection, and
    /// a tool that could see the selection could also edit it. Pushing the
    /// transform onto the sink rather than moving anything means the preview
    /// costs nothing and cannot leave the drawing changed if it is abandoned.
    /// </remarks>
    private void DrawModifyPreview(ModifyTool tool, in EmitContext context, in DisplayStyle style)
    {
        if (tool.Pending(_snapped) is not { } transform) return;

        context.Sink.PushTransform(transform);

        foreach (var entity in Selection.Ordered) entity.Emit(context, style);

        context.Sink.PopTransform();
    }

    /// <summary>
    /// The handles on the selection, as squares of a fixed pixel size.
    /// </summary>
    /// <remarks>
    /// Device space, like the snap marker and for the same reason: a grip is
    /// a target for the mouse, so it has to stay the same size to aim at
    /// however far the view is zoomed out. Where the handles *are* is world
    /// geometry, which is <see cref="GripSet"/>'s job and is the half worth
    /// testing.
    /// </remarks>
    private void DrawGrips(DrawingContext dc)
    {
        const double half = GripSizePixels / 2;

        foreach (var grip in _grips.Grips)
        {
            bool held = _heldGrip == grip;

            // The held one rides with the cursor rather than staying behind
            // at the position the geometry has already left.
            Vec2 at = Camera.WorldToScreen(held ? _snapped : grip.Point);
            bool hot = held || (_heldGrip is null && _hoverGrip == grip);

            dc.DrawRectangle(hot ? HotGripFill : GripFill, GripEdge,
                new Rect(at.X - half, at.Y - half, GripSizePixels, GripSizePixels));
        }
    }

    /// <summary>
    /// The entity as the grip drag would leave it, through the same sink as
    /// the scene -- the rule every preview here follows.
    /// </summary>
    private void DrawGripPreview(IDrawingSink sink)
    {
        if (_heldGrip is not { } held || _drawing is null) return;

        var context = new EmitContext(sink, _drawing.Layers, Camera.Scale);
        var style = new DisplayStyle(PreviewColor, Lineweight.Default);

        if (held.Grip.Role == GripRole.Move)
        {
            // Pushed onto the sink rather than applied, so an abandoned drag
            // cannot leave the entity somewhere else.
            sink.PushTransform(Mat3.Translation(_snapped - held.Point));
            held.Entity.Emit(context, style);
            sink.PopTransform();
            return;
        }

        // A shape change has no transform to push, so it is previewed from a
        // copy -- the same copy the commit makes, which is what stops the
        // preview from being free to disagree with the result.
        var moved = held.Entity.Clone();
        if (moved.MoveGrip(held.Grip, _snapped)) moved.Emit(context, style);
    }

    /// <summary>
    /// The rubber band, in device space and over the scene. Blue filled for a
    /// window and green dashed for a crossing, which is the convention every
    /// CAD user already reads without being told.
    /// </summary>
    private void DrawSelectionBand(DrawingContext dc)
    {
        var rect = new Rect(_pickAnchor, _bandCorner);
        if (rect.Width <= 0 && rect.Height <= 0) return;

        // A zoom window is not a selection, so it does not borrow the
        // crossing colour just because it was dragged right to left.
        bool crossing = !ZoomWindowArmed && BandMode == SelectionMode.Crossing;
        var pen = crossing ? CrossingPen : WindowPen;
        var fill = crossing ? CrossingFill : WindowFill;

        dc.DrawRectangle(fill, pen, rect);
    }

    private SelectionMode BandMode =>
        _bandCorner.X < _pickAnchor.X ? SelectionMode.Crossing : SelectionMode.Window;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        Point p = e.GetPosition(this);
        Camera.ZoomAtScreenPoint(new Vec2(p.X, p.Y), factor);
        Redraw();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        // Middle-drag pans, as everywhere in CAD, and right-drag does too so
        // that a trackpad can still pan. Left is the editor's: a click picks,
        // a drag bands.
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = true;
            _panDragged = false;
            _panAnchor = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (_drawing is null) return;

        // A tool takes its points on the way down, and never bands.
        if (Mode == CanvasMode.Draw && e.ChangedButton == MouseButton.Left)
        {
            Point picked = e.GetPosition(this);
            var world = Camera.ScreenToWorld(new Vec2(picked.X, picked.Y));

            // Some clicks want what is under the cursor rather than a
            // snapped coordinate: they are pointing at an object, not
            // placing a point. The tool says which, per click.
            if (_tool is { WantsEntity: true }) PickEntityForTool(world);
            else PlaceToolPoint(ResolvePoint(world));

            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            Point pressed = e.GetPosition(this);

            // A grip wins over a band. The handle is on something already
            // selected, and a band started on top of it would clear the very
            // selection the handle belongs to.
            if (Mode == CanvasMode.Select &&
                BeginGripDrag(Camera.ScreenToWorld(new Vec2(pressed.X, pressed.Y))))
            {
                _gesture = LeftGesture.Grip;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            _pickAnchor = pressed;
            _bandCorner = _pickAnchor;
            _gesture = LeftGesture.Pick;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        // Right-click finishes an open-ended tool, as it does in AutoCAD.
        // A right-drag was a pan, and must not also end the polyline.
        if (_tool is null || _panDragged || !_tool.InProgress) return;

        FinishTool();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);

        _cursor = Camera.ScreenToWorld(new Vec2(p.X, p.Y));

        // Resolving on every move is what puts the marker under the cursor
        // before the click rather than after it.
        if (IsPlacingPoint) ResolvePoint(_cursor);
        else _snapped = _cursor;

        if (_isPanning)
        {
            // A right button that only ever went down and up is a click, not
            // a pan, and a click is how an open-ended tool is finished.
            if (Math.Abs(p.X - _panAnchor.X) > 1 || Math.Abs(p.Y - _panAnchor.Y) > 1) _panDragged = true;

            Camera.PanByScreenDelta(p.X - _panAnchor.X, p.Y - _panAnchor.Y);
            _panAnchor = p;
            Redraw();
        }
        else if (_heldGrip is not null)
        {
            // The preview, the grip square and the length readout all follow
            // the cursor, so every move is a repaint here too.
            InvalidateVisual();
            PendingPointChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (Mode == CanvasMode.Draw)
        {
            // The preview and the snap marker both follow the cursor, so
            // every move is a repaint.
            InvalidateVisual();
            PendingPointChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (_gesture is LeftGesture.Pick or LeftGesture.Band)
        {
            // A band only starts once the drag is unmistakably a drag; below
            // that a shaky hand would turn every click into an empty window.
            if (_gesture == LeftGesture.Pick &&
                (Math.Abs(p.X - _pickAnchor.X) > DragThresholdPixels ||
                 Math.Abs(p.Y - _pickAnchor.Y) > DragThresholdPixels))
            {
                _gesture = LeftGesture.Band;
            }

            if (_gesture == LeftGesture.Band)
            {
                _bandCorner = p;
                InvalidateVisual();
            }
        }
        else if (Mode == CanvasMode.Select)
        {
            HoverGrip(_cursor);
        }

        CursorMoved?.Invoke(this, Camera.ScreenToWorld(new Vec2(p.X, p.Y)));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning && e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || !IsMouseCaptured) return;

        ReleaseMouseCapture();
        Point p = e.GetPosition(this);
        var world = Camera.ScreenToWorld(new Vec2(p.X, p.Y));

        bool extend = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

        var gesture = _gesture;
        _gesture = LeftGesture.None;

        switch (gesture)
        {
            case LeftGesture.Grip:
                // Resolved here rather than inside, so that a grip dropped on
                // an endpoint lands on it exactly -- and so that a typed
                // length, which has already worked out its own point, is not
                // snapped away from the distance that was asked for.
                EndGripDrag(ResolvePoint(world));
                break;

            case LeftGesture.Band:
                _bandCorner = p;
                SelectInBand(extend);
                InvalidateVisual();
                break;

            case LeftGesture.Pick:
                PickAt(world, extend);
                break;
        }
    }

    /// <summary>
    /// Starting to type a number while picking opens the length box.
    /// </summary>
    /// <remarks>
    /// The canvas keeps the keyboard so that Escape, Enter and Delete keep
    /// working, and hands typing over only when it is plainly a measurement.
    /// Focusing the box up front instead would take those keys away for the
    /// whole of every draw.
    /// </remarks>
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (e.Text.Length == 0 || Entry == CursorEntry.None) return;
        if (!char.IsAsciiDigit(e.Text[0]) && e.Text[0] != '.') return;

        LengthTypingStarted?.Invoke(this, e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                CancelWhateverIsHappening();
                e.Handled = true;
                return;

            case Key.Enter:
                if (_tool is { InProgress: true })
                {
                    FinishTool();
                    e.Handled = true;
                }
                return;

            case Key.Delete:
                if (EraseSelection()) e.Handled = true;
                return;

            case Key.Tab:
                // Tab is how the width and height of a rectangle are reached
                // without typing a digit first, so it is not focus travel
                // while there is one being drawn.
                if (Entry == CursorEntry.Size)
                {
                    EntryFieldRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                return;
        }
    }

    /// <summary>
    /// Escape, in the order a user means it: the thing most recently started
    /// is the thing it takes back.
    /// </summary>
    private void CancelWhateverIsHappening()
    {
        if (_heldGrip is not null)
        {
            CancelGripDrag();
            return;
        }

        if (_gesture == LeftGesture.Band)
        {
            _gesture = LeftGesture.None;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        if (_tool is { InProgress: true })
        {
            _tool.Cancel();
            ClearSizeLocks();
            Snapping.ClearTracking();

            InvalidateVisual();
            ModeChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Mode != CanvasMode.Select)
        {
            UseSelect();
            return;
        }

        Selection.Clear();
    }

    // ---- grips ----------------------------------------------------------

    /// <summary>The handles on show, in world coordinates.</summary>
    public IReadOnlyList<EntityGrip> Grips => _grips.Grips;

    /// <summary>Whether a grip is being dragged.</summary>
    public bool IsDraggingGrip => _heldGrip is not null;

    /// <summary>
    /// Recomputes the handles on show.
    /// </summary>
    /// <remarks>
    /// Grips are world geometry read off the selected entities, so anything
    /// that changes either of those -- the selection, an edit, an undo -- has
    /// to come back through here, or the squares are left sitting where the
    /// geometry used to be. Three callers: the selection, an edit, and a
    /// change of mode.
    /// </remarks>
    private void RefreshGrips() =>
        _grips.Rebuild(Mode == CanvasMode.Select ? Selection.Ordered : []);

    /// <summary>Lights the handle under the cursor, if there is one.</summary>
    private void HoverGrip(Vec2 world)
    {
        var hover = _grips.Nearest(world, GripTolerance);
        if (hover == _hoverGrip) return;

        _hoverGrip = hover;
        InvalidateVisual();
    }

    /// <summary>
    /// Takes hold of the grip nearest a world point. False if none is in
    /// reach, which is what leaves a click free to select instead.
    /// </summary>
    /// <remarks>
    /// World coordinates and public, for the reason <see cref="PickAt"/> is:
    /// the mouse is not the only thing that will ever drive this, and a test
    /// has no mouse.
    /// </remarks>
    public bool BeginGripDrag(Vec2 world)
    {
        if (_drawing is null || Mode != CanvasMode.Select) return false;
        if (_grips.Nearest(world, GripTolerance) is not { } grip) return false;

        _heldGrip = grip;
        _hoverGrip = grip;

        // The handle's own position, not where it was clicked: a grab that
        // lands a pixel off the grip must not shift the geometry by a pixel.
        _snapped = grip.Point;
        _snap = default;

        // Points acquired while drawing something else are not what this
        // drag wants to line up with, exactly as for a new tool.
        Snapping.ClearTracking();

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Moves the held grip, snapping as a picked point would.</summary>
    public void DragGripTo(Vec2 world)
    {
        if (_heldGrip is null) return;

        ResolvePoint(world);
        InvalidateVisual();
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops the held grip at <paramref name="to"/> and commits the edit.
    /// Returns whether the drawing changed.
    /// </summary>
    /// <remarks>
    /// Takes the point already resolved, the way <see cref="PlaceToolPoint"/>
    /// does, so that a caller with a point of its own -- a typed length --
    /// is not snapped off the answer it worked out.
    /// </remarks>
    public bool EndGripDrag(Vec2 to)
    {
        if (_heldGrip is not { } held) return false;

        _heldGrip = null;
        _snap = default;

        bool changed = CommitGrip(held, to);

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    /// <summary>Lets go of the held grip, changing nothing.</summary>
    public void CancelGripDrag()
    {
        if (_heldGrip is null) return;

        AbandonGesture();
        _snap = default;

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AbandonGesture()
    {
        _heldGrip = null;
        _hoverGrip = null;
        _gesture = LeftGesture.None;
    }

    /// <summary>
    /// Turns a finished grip drag into a command.
    /// </summary>
    /// <remarks>
    /// Two routes, because a grip means one of two things. A move is a
    /// translation of the whole entity, which the transform command already
    /// applies and undoes by its exact inverse. A shape change edits a
    /// *copy* and swaps it in, the way the properties panel does: that is
    /// what makes it undoable without every entity needing a way to save and
    /// restore its own geometry, and the replacement keeps the handle the
    /// file knows the object by.
    /// </remarks>
    private bool CommitGrip(EntityGrip held, Vec2 to)
    {
        if (_drawing is null || Commands is null) return false;

        var (entity, grip) = held;

        // A grab and release with no drag is not an edit, and must not put a
        // do-nothing step on the undo stack.
        if (Vec2.Distance(grip.Point, to) < 1e-12) return false;

        if (grip.Role == GripRole.Move)
        {
            Commands.Do(new TransformEntities(_drawing.ActiveLayout, [entity],
                Mat3.Translation(to - grip.Point), "Move"));

            RaiseDrawingEdited();
            return true;
        }

        var moved = entity.Clone();
        if (!moved.MoveGrip(grip, to)) return false;

        Commands.Do(new ReplaceEntities(_drawing.ActiveLayout,
            EditPlan.Replace(entity, moved), "Stretch"));

        // Whatever else was selected stays selected, with the replacement in
        // place of the original: a grip drag edits one object out of however
        // many are picked, and clearing the rest would be a surprise.
        Selection.Set(Selection.Ordered
            .Select(picked => ReferenceEquals(picked, entity) ? moved : picked)
            .ToList());

        RaiseDrawingEdited();
        return true;
    }

    // ---- editing --------------------------------------------------------

    /// <summary>
    /// Hands the active tool its next point. Public for the same reason
    /// <see cref="PickAt"/> is.
    /// </summary>
    public void PlaceToolPoint(Vec2 world)
    {
        switch (_tool)
        {
            case DrawTool draw:
                Commit(draw.Click(world));
                break;

            case ModifyTool modify:
                Commit(modify, modify.Click(world));
                break;

            default:
                return;
        }

        // A typed size was for the corner just placed, not the next one.
        ClearSizeLocks();
        _aimed = _snapped;

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands an entity tool whatever is under the cursor, along with the
    /// curves of everything around it to work against.
    /// </summary>
    public void PickEntityForTool(Vec2 world)
    {
        if (_drawing is null || Commands is null) return;

        var hit = Picker.At(_drawing, world, PickTolerance);

        switch (_tool)
        {
            case EntityTool tool:
                Apply(tool, tool.Click(new EntityPick(hit, world, BoundariesAround(world, hit))));

                // Whatever the tool is still holding shows as selected, so a
                // fillet waiting for its second edge says which it has.
                Selection.Set(tool.PickedEntities);
                break;

            case CircleDimensionTool dimension:
                // The circle it took, highlighted while the number is placed,
                // so it is clear which one is being measured.
                Selection.Set(dimension.Take(hit) && hit is not null ? [hit] : []);
                break;

            default:
                return;
        }

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        PendingPointChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The curves of everything near the click except the thing clicked.
    /// </summary>
    /// <remarks>
    /// AutoCAD makes you select cutting edges first; taking everything nearby
    /// instead is what modern CAD does and saves a step that is nearly always
    /// answered with "all of it". Nearby rather than everything so that a
    /// drawing of a hundred thousand objects does not flatten all of them for
    /// one click -- the window is the view, since a boundary you cannot see
    /// is not one you meant.
    /// </remarks>
    private IReadOnlyList<CurvePiece> BoundariesAround(Vec2 world, SceneEntity? exclude)
    {
        if (_drawing is null) return [];

        var index = _drawing.ActiveLayout.Index;
        var nearby = new List<int>();
        index.Query(Camera.VisibleWorldBounds, nearby);

        var curves = new List<CurvePiece>();
        double tolerance = Math.Max(PickTolerance / 4, 1e-9);

        foreach (int position in nearby)
        {
            var entity = index[position];
            if (ReferenceEquals(entity, exclude)) continue;

            if ((uint)entity.LayerIndex < (uint)_drawing.Layers.Count &&
                !_drawing.Layers[entity.LayerIndex].IsVisible)
            {
                continue;
            }

            entity.CollectCurves(curves, tolerance);
        }

        return curves;
    }

    private void Apply(EntityTool tool, in EditPlan plan)
    {
        if (_drawing is null || Commands is null) return;

        // Tangent mate is a move rather than a swap: the circle keeps its
        // identity, so it goes through the transform command instead.
        if (tool is TangentMateTool { Offset: { } offset, Target: { } target })
        {
            Commands.Do(new TransformEntities(_drawing.ActiveLayout, [target],
                Mat3.Translation(offset), "Mate"));

            RaiseDrawingEdited();
            return;
        }

        if (plan.IsEmpty) return;

        string name = char.ToUpperInvariant(tool.Name[0]) + tool.Name[1..];
        Commands.Do(new ReplaceEntities(_drawing.ActiveLayout, plan, name));

        RaiseDrawingEdited();
    }

    /// <summary>Ends an open-ended tool, as Enter or a right click does.</summary>
    public void FinishTool()
    {
        if (_tool is not DrawTool draw) return;

        Commit(draw.Finish());

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts a finished entity into the drawing, on the current layer and
    /// through the command stack -- the only route in.
    /// </summary>
    private void Commit(SceneEntity? entity)
    {
        if (entity is null || _drawing is null || Commands is null) return;

        _drawing.Place(entity);

        // A dimension is annotation rather than geometry, and belongs on its
        // own layer whatever layer happens to be current. That is what every
        // drawing office standard says, and it is what makes it possible to
        // turn every dimension in a drawing off at once.
        Commands.Do(entity is SDimension
            ? OntoDimensionLayer(entity)
            : new AddEntities(_drawing.ActiveLayout, entity));

        RaiseDrawingEdited();
    }

    /// <summary>The layer dimensions are drawn on, made when the first one is.</summary>
    public const string DimensionLayerName = "Dimensions";

    /// <summary>
    /// Green, which is what a drawing full of white geometry leaves free and
    /// what AutoCAD's own templates use for dimensions.
    /// </summary>
    private static readonly Rgb DimensionLayerColor = new(0, 200, 0);

    /// <summary>
    /// Adds a dimension on the dimension layer, creating that layer if the
    /// drawing has not got one, as a single step on the undo stack.
    /// </summary>
    private IEditCommand OntoDimensionLayer(SceneEntity entity)
    {
        var drawing = _drawing!;
        int existing = LayerTable.IndexOf(drawing, DimensionLayerName);

        if (existing >= 0)
        {
            entity.LayerIndex = existing;
            entity.Style = drawing.Layers[existing].Style;

            return new AddEntities(drawing.ActiveLayout, entity);
        }

        var layer = new Layer(DimensionLayerName) { Color = DimensionLayerColor };

        // A new layer goes on the end of the table, so that is the index the
        // dimension needs -- and it needs it before the add runs, since a
        // command applies rather than works things out.
        entity.LayerIndex = drawing.Layers.Count;
        entity.Style = layer.Style;

        return new Composite("Draw dimension",
            new AddLayer(layer),
            new AddEntities(drawing.ActiveLayout, entity));
    }

    // ---- layers ---------------------------------------------------------

    /// <summary>
    /// Adds a layer and returns it, or null if there is no drawing.
    /// </summary>
    /// <remarks>
    /// The name is made unique whatever was asked for. Two layers of one
    /// name is a drawing nobody -- and no file format -- can reason about,
    /// and refusing the whole thing over it would throw away the rest of
    /// what the user filled in.
    /// </remarks>
    public Layer? AddLayer(LayerProperties properties)
    {
        if (_drawing is null || Commands is null) return null;

        var layer = new Layer(UniqueLayerName(properties.Name, except: -1));
        (properties with { Name = layer.Name }).ApplyTo(layer);

        Commands.Do(new AddLayer(layer));

        RaiseDrawingEdited();
        return layer;
    }

    /// <summary>
    /// A layer name not already taken by a layer other than
    /// <paramref name="except"/>, falling back to a default for a blank one.
    /// </summary>
    private string UniqueLayerName(string wanted, int except)
    {
        var drawing = _drawing!;

        string name = wanted.Trim();
        if (name.Length == 0) name = "Layer";

        int clash = LayerTable.IndexOf(drawing, name);
        return clash < 0 || clash == except ? name : LayerTable.UniqueName(drawing, name);
    }

    /// <summary>
    /// Why the layer at <paramref name="index"/> cannot be deleted, in words
    /// that can be shown to the user, or null if it can be.
    /// </summary>
    public string? WhyLayerCannotBeDeleted(int index)
    {
        if (_drawing is null || (uint)index >= (uint)_drawing.Layers.Count) return "No such layer.";
        if (index == 0) return "Layer 0 is the one every DWG has, and cannot be deleted.";

        return LayerTable.IsInUse(_drawing, index)
            ? $"Layer {_drawing.Layers[index].Name} still has geometry on it. Move or erase it first."
            : null;
    }

    /// <summary>Deletes a layer. False when it is one of the ones that cannot go.</summary>
    public bool DeleteLayer(int index)
    {
        if (_drawing is null || Commands is null) return false;
        if (WhyLayerCannotBeDeleted(index) is not null) return false;

        Commands.Do(new DeleteLayer(_drawing, index));

        RaiseDrawingEdited();
        return true;
    }

    /// <summary>
    /// Changes the sizes new dimensions are drawn with, and optionally every
    /// dimension already drawn, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The dimension tool in hand is told at once: a setting the tools use
    /// is pushed into them, never copied, so a dimension started before the
    /// change comes out at the new size like one started after it.
    /// </remarks>
    public bool SetDimensionSettings(DimensionSettings settings, bool restyleExisting)
    {
        if (_drawing is null || Commands is null || !settings.IsValid) return false;

        Commands.Do(ChangeDimensionSettings.Including(_drawing, settings, restyleExisting));
        ApplyToolSettings();

        // A restyled dimension is a replacement; the one that was selected
        // has gone, and its successor takes its place in the selection.
        Selection.Prune(_drawing.ActiveLayout.Entities.Contains);

        RaiseDrawingEdited();
        return true;
    }

    /// <summary>
    /// Renames or restyles a layer, taking the entities that were following
    /// it along with it.
    /// </summary>
    public bool EditLayer(int index, LayerProperties properties)
    {
        if (_drawing is null || Commands is null) return false;
        if ((uint)index >= (uint)_drawing.Layers.Count) return false;

        Commands.Do(new ChangeLayer(_drawing, index,
            properties with { Name = UniqueLayerName(properties.Name, except: index) }));

        RaiseDrawingEdited();
        return true;
    }

    /// <summary>
    /// Applies a finished modify transform to the selection, either moving it
    /// or leaving a transformed copy behind.
    /// </summary>
    private void Commit(ModifyTool tool, Mat3? transform)
    {
        if (transform is not { } matrix || _drawing is null || Commands is null) return;
        if (Selection.IsEmpty) return;

        string name = char.ToUpperInvariant(tool.Name[0]) + tool.Name[1..];

        if (tool.Duplicates)
        {
            var copy = new CopyEntities(_drawing.ActiveLayout, Selection.Ordered, matrix, name);
            Commands.Do(copy);

            // The copies are what you almost always want to act on next, and
            // leaving the originals selected makes a second copy silently
            // duplicate the wrong thing.
            Selection.Set(copy.Copies);
        }
        else
        {
            Commands.Do(new TransformEntities(_drawing.ActiveLayout, Selection.Ordered, matrix, name));
        }

        RaiseDrawingEdited();
    }

    /// <summary>
    /// Swaps one entity for an edited copy of itself, through the command
    /// stack, and leaves the copy selected.
    /// </summary>
    /// <remarks>
    /// How the properties panel writes back. Editing a copy rather than the
    /// original is what makes the change undoable without every entity
    /// needing a way to save and restore its own geometry, and
    /// <c>EditPlan.Replace</c> carries the handle across so the file still
    /// recognises the object afterwards.
    /// </remarks>
    public bool ApplyEdit(SceneEntity original, SceneEntity updated, string name)
    {
        if (_drawing is null || Commands is null) return false;

        Commands.Do(new ReplaceEntities(_drawing.ActiveLayout,
            EditPlan.Replace(original, updated), name));

        Selection.Set([updated]);

        RaiseDrawingEdited();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Swaps a whole set of entities for edited copies, as one undo step.
    /// </summary>
    /// <remarks>
    /// One command rather than one per entity: setting the layer of forty
    /// objects should take one press of Ctrl+Z to put back, not forty.
    /// </remarks>
    public bool ApplyEdits(IReadOnlyList<(SceneEntity Original, SceneEntity Updated)> pairs, string name)
    {
        if (_drawing is null || Commands is null || pairs.Count == 0) return false;

        Commands.Do(new ReplaceEntities(_drawing.ActiveLayout, EditPlan.Swap(pairs), name));

        Selection.Set(pairs.Select(pair => pair.Updated));

        RaiseDrawingEdited();
        InvalidateVisual();
        return true;
    }

    /// <summary>Deletes what is selected. Returns whether there was anything to delete.</summary>
    public bool EraseSelection()
    {
        if (_drawing is null || Commands is null || Selection.IsEmpty) return false;

        Commands.Do(new DeleteEntities(_drawing.ActiveLayout, Selection.Ordered));
        Selection.Clear();

        RaiseDrawingEdited();
        InvalidateVisual();
        return true;
    }

    public void Undo()
    {
        if (Commands?.Undo() != true) return;
        AfterHistoryMove();
    }

    public void Redo()
    {
        if (Commands?.Redo() != true) return;
        AfterHistoryMove();
    }

    private void AfterHistoryMove()
    {
        // Undoing a draw takes the entity out of the layout, and a selection
        // holding an entity that is no longer in the drawing would keep it
        // alive on screen as a highlight over nothing.
        if (_drawing is not null)
            Selection.Prune(_drawing.ActiveLayout.Entities.Contains);

        // Undoing a dimension style puts the old sizes back, and a dimension
        // tool in hand has to draw at those, not at the ones taken back.
        ApplyToolSettings();

        RaiseDrawingEdited();
        InvalidateVisual();
    }

    private void Disarm() => UseSelect();

    /// <summary>
    /// Picks whatever is at a world point, as a click does.
    /// </summary>
    /// <remarks>
    /// World coordinates rather than device ones, and public, because the
    /// mouse handler is not the only thing that will ever want this: a
    /// command line takes typed coordinates, and a test has no mouse. The
    /// handler converts and calls in here.
    /// </remarks>
    public void PickAt(Vec2 world, bool extend = false)
    {
        if (_drawing is null) return;

        // A click is not a window, so an armed zoom has nothing to frame.
        if (ZoomWindowArmed)
        {
            Disarm();
            return;
        }

        var hit = Picker.At(_drawing, world, PickTolerance);

        if (hit is null)
        {
            // Clicking nothing means "never mind", unless the user is still
            // building a set.
            if (!extend) Selection.Clear();
            return;
        }

        if (extend) Selection.Toggle(hit);
        else Selection.Set([hit]);
    }

    private void SelectInBand(bool extend)
    {
        if (_drawing is null) return;

        var a = Camera.ScreenToWorld(new Vec2(_pickAnchor.X, _pickAnchor.Y));
        var b = Camera.ScreenToWorld(new Vec2(_bandCorner.X, _bandCorner.Y));
        var rect = Bounds2.FromCorners(a, b);

        if (ZoomWindowArmed)
        {
            // No padding: the user drew the frame they want, exactly.
            Camera.ZoomToFit(rect, paddingFraction: 0);
            Disarm();
            Redraw();
            return;
        }

        var hits = Picker.InRect(_drawing, rect, BandMode, PickTolerance);

        if (extend) Selection.AddRange(hits);
        else Selection.Set(hits);
    }

    /// <summary>
    /// Says the drawing changed, having first put the handles back where the
    /// geometry now is. One method rather than a raise at each call site,
    /// because the site that forgets is a set of squares left floating
    /// somewhere the object no longer is.
    /// </summary>
    private void RaiseDrawingEdited()
    {
        RefreshGrips();
        DrawingEdited?.Invoke(this, EventArgs.Empty);
    }

    private void SyncViewport()
    {
        Camera.ViewportWidth = ActualWidth;
        Camera.ViewportHeight = ActualHeight;
    }

    private SolidColorBrush GetBackgroundBrush()
    {
        var bg = Settings.Background;
        if (_backgroundBrushKey != bg.Packed)
        {
            _backgroundBrush = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));
            _backgroundBrush.Freeze();
            _backgroundBrushKey = bg.Packed;
        }
        return _backgroundBrush;
    }
}
