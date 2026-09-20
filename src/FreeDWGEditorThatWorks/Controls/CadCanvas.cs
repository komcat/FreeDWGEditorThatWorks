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

    private bool _isBanding;
    private Point _pickAnchor;
    private Point _bandCorner;

    private CanvasTool? _tool;
    private Vec2 _cursor;
    private Vec2 _snapped;
    private SnapResult _snap;

    /// <summary>How far a click may miss by, in device pixels.</summary>
    private const double PickRadiusPixels = 6.0;

    /// <summary>Drag further than this and a click becomes a selection window.</summary>
    private const double DragThresholdPixels = 4.0;

    /// <summary>Colour the tool draws in while the entity is still being picked.</summary>
    private static readonly Rgb PreviewColor = new(255, 190, 90);

    /// <summary>How near the cursor a snap point has to be, in device pixels.</summary>
    private const double SnapRadiusPixels = 12.0;

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
    /// Fillet radius and chamfer distance. Zero means a sharp corner.
    /// </summary>
    /// <remarks>
    /// Held here rather than copied into each tool as it is built, because
    /// two copies of a setting drift: one path sets it at construction and
    /// the other on change, and whichever is forgotten is a size that
    /// silently does nothing. The canvas owns it and applies it, so a tool
    /// started before or after the number is typed behaves the same.
    /// </remarks>
    public double CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = value;
            ApplyCornerRadius();
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private double _cornerRadius;

    private void ApplyCornerRadius()
    {
        if (_tool is FilletTool fillet) fillet.Radius = _cornerRadius;
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
        _tool = tool;
        Mode = mode;

        // Drawing and selecting are different modes, and carrying a selection
        // into a draw tool only makes the next Delete a surprise. A modify
        // tool is the exception: the selection is what it acts on.
        if (mode != CanvasMode.Select && tool?.NeedsSelection != true) Selection.Clear();

        _snap = default;

        // A new tool starts with a clean slate: points acquired while drawing
        // the last thing are rarely what the next one wants to line up with.
        Snapping.ClearTracking();

        // Whatever the tool was built with, the canvas's figure wins: there
        // is one setting and it is here.
        ApplyCornerRadius();

        Cursor = mode == CanvasMode.Select ? Cursors.Arrow : Cursors.Cross;

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the tool wants next, for the status bar.</summary>
    public string? ToolPrompt => _tool?.Prompt;

    /// <summary>Pick tolerance in world units at the current zoom.</summary>
    public double PickTolerance => Camera.Scale > 0 ? PickRadiusPixels / Camera.Scale : 0;

    /// <summary>Snap radius in world units at the current zoom.</summary>
    public double SnapTolerance => Camera.Scale > 0 ? SnapRadiusPixels / Camera.Scale : 0;

    /// <summary>Where the snap marker is showing, if one is.</summary>
    public SnapResult ActiveSnap => _snap;

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

        Vec2? from = _tool is { InProgress: true } tool ? tool.Points[^1] : null;

        // The point before that, so polar angles can be measured from the
        // segment just drawn rather than from the horizon.
        Vec2? before = _tool is { InProgress: true, Points.Count: >= 2 } run
            ? run.Points[^2]
            : null;

        _snap = Snapping.Resolve(_drawing.ActiveLayout, _drawing.Layers,
            world, SnapTolerance, from, Camera.Scale, before);

        AcquireForTracking();
        return _snap.Point;
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
        if (_isBanding) DrawSelectionBand(dc);
        if (_snap.Found && Mode == CanvasMode.Draw)
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
        if (!_tool.InProgress && _tool is not ModifyTool) return;

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

            // An entity tool wants what is under the cursor, not a snapped
            // coordinate: it is pointing at objects, not placing points.
            if (_tool is EntityTool) PickEntityForTool(world);
            else PlaceToolPoint(ResolvePoint(world));

            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _pickAnchor = e.GetPosition(this);
            _bandCorner = _pickAnchor;
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
        _snapped = Mode == CanvasMode.Draw ? ResolvePoint(_cursor) : _cursor;

        if (_isPanning)
        {
            // A right button that only ever went down and up is a click, not
            // a pan, and a click is how an open-ended tool is finished.
            if (Math.Abs(p.X - _panAnchor.X) > 1 || Math.Abs(p.Y - _panAnchor.Y) > 1) _panDragged = true;

            Camera.PanByScreenDelta(p.X - _panAnchor.X, p.Y - _panAnchor.Y);
            _panAnchor = p;
            Redraw();
        }
        else if (Mode == CanvasMode.Draw)
        {
            // The preview and the snap marker both follow the cursor, so
            // every move is a repaint.
            InvalidateVisual();
        }
        else if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
        {
            // A band only starts once the drag is unmistakably a drag; below
            // that a shaky hand would turn every click into an empty window.
            if (!_isBanding &&
                (Math.Abs(p.X - _pickAnchor.X) > DragThresholdPixels ||
                 Math.Abs(p.Y - _pickAnchor.Y) > DragThresholdPixels))
            {
                _isBanding = true;
            }

            if (_isBanding)
            {
                _bandCorner = p;
                InvalidateVisual();
            }
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

        bool extend = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

        if (_isBanding)
        {
            _bandCorner = p;
            SelectInBand(extend);
            _isBanding = false;
            InvalidateVisual();
        }
        else
        {
            PickAt(Camera.ScreenToWorld(new Vec2(p.X, p.Y)), extend);
        }
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
        }
    }

    /// <summary>
    /// Escape, in the order a user means it: the thing most recently started
    /// is the thing it takes back.
    /// </summary>
    private void CancelWhateverIsHappening()
    {
        if (_isBanding)
        {
            _isBanding = false;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        if (_tool is { InProgress: true })
        {
            _tool.Cancel();
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

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands an entity tool whatever is under the cursor, along with the
    /// curves of everything around it to work against.
    /// </summary>
    public void PickEntityForTool(Vec2 world)
    {
        if (_tool is not EntityTool tool || _drawing is null || Commands is null) return;

        var hit = Picker.At(_drawing, world, PickTolerance);
        var plan = tool.Click(new EntityPick(hit, world, BoundariesAround(world, hit)));

        Apply(tool, plan);

        // Whatever the tool is still holding shows as selected, so a fillet
        // waiting for its second line says which first line it has.
        Selection.Set(tool.PickedEntities);

        InvalidateVisual();
        ModeChanged?.Invoke(this, EventArgs.Empty);
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

            DrawingEdited?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (plan.IsEmpty) return;

        string name = char.ToUpperInvariant(tool.Name[0]) + tool.Name[1..];
        Commands.Do(new ReplaceEntities(_drawing.ActiveLayout, plan, name));

        DrawingEdited?.Invoke(this, EventArgs.Empty);
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
        Commands.Do(new AddEntities(_drawing.ActiveLayout, entity));

        DrawingEdited?.Invoke(this, EventArgs.Empty);
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

        DrawingEdited?.Invoke(this, EventArgs.Empty);
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

        DrawingEdited?.Invoke(this, EventArgs.Empty);
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

        DrawingEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        return true;
    }

    /// <summary>Deletes what is selected. Returns whether there was anything to delete.</summary>
    public bool EraseSelection()
    {
        if (_drawing is null || Commands is null || Selection.IsEmpty) return false;

        Commands.Do(new DeleteEntities(_drawing.ActiveLayout, Selection.Ordered));
        Selection.Clear();

        DrawingEdited?.Invoke(this, EventArgs.Empty);
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

        DrawingEdited?.Invoke(this, EventArgs.Empty);
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
