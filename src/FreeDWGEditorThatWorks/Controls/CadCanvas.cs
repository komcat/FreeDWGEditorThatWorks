using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
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

    private DrawTool? _tool;
    private Vec2 _cursor;

    /// <summary>How far a click may miss by, in device pixels.</summary>
    private const double PickRadiusPixels = 6.0;

    /// <summary>Drag further than this and a click becomes a selection window.</summary>
    private const double DragThresholdPixels = 4.0;

    /// <summary>Colour the tool draws in while the entity is still being picked.</summary>
    private static readonly Rgb PreviewColor = new(255, 190, 90);

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
    /// The tool in force, or null for the pointer. Setting it abandons
    /// whatever the previous tool had half-picked.
    /// </summary>
    public DrawTool? Tool
    {
        get => _tool;
        set
        {
            _tool?.Cancel();
            _tool = value;

            if (_tool is not null)
            {
                Selection.Clear();
                ZoomWindowArmed = false;
            }

            Cursor = _tool is null ? Cursors.Arrow : Cursors.Cross;
            InvalidateVisual();
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>What the tool wants next, for the status bar.</summary>
    public string? ToolPrompt => _tool?.Prompt;

    /// <summary>Pick tolerance in world units at the current zoom.</summary>
    public double PickTolerance => Camera.Scale > 0 ? PickRadiusPixels / Camera.Scale : 0;

    /// <summary>
    /// When set, the next dragged rectangle frames the view instead of
    /// selecting. One shot: it disarms itself, as AutoCAD's zoom window does.
    /// </summary>
    public bool ZoomWindowArmed { get; set; }

    /// <summary>Fires when an armed zoom window is used up or abandoned.</summary>
    public event EventHandler? ZoomWindowDisarmed;

    /// <summary>Fires with the cursor position in world units.</summary>
    public event EventHandler<Vec2>? CursorMoved;

    /// <summary>Fires after any pan or zoom, so the shell can refresh its readout.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Fires after the selection changes, for the same reason.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Fires when the active tool changes, or wants a different point.</summary>
    public event EventHandler? ToolChanged;

    /// <summary>Fires after the drawing is edited, so the shell can refresh counts.</summary>
    public event EventHandler? DrawingEdited;

    public SceneDrawing? Drawing
    {
        get => _drawing;
        set
        {
            _drawing = value;
            Tool = null;
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
        var sink = new WpfDrawingSink(dc, Camera, Settings, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        LastStats = SceneRenderer.Render(_drawing, Camera, sink,
            Selection.IsEmpty ? null : Selection.Entities, Settings);

        if (_tool is not null) DrawToolPreview(sink);
        if (_isBanding) DrawSelectionBand(dc);
    }

    /// <summary>
    /// The entity as it would be if the cursor were the next point, drawn
    /// through the same sink as the scene. A preview that went through its
    /// own drawing path would be free to disagree with the result, which is
    /// exactly what a preview must not do.
    /// </summary>
    private void DrawToolPreview(IDrawingSink sink)
    {
        if (_tool is null || !_tool.InProgress || _drawing is null) return;

        var context = new EmitContext(sink, _drawing.Layers, Camera.Scale);
        _tool.Preview(_cursor, context, new DisplayStyle(PreviewColor, Lineweight.Default));
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
        if (_tool is not null && e.ChangedButton == MouseButton.Left)
        {
            Point picked = e.GetPosition(this);
            PlaceToolPoint(Camera.ScreenToWorld(new Vec2(picked.X, picked.Y)));
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

        if (_isPanning)
        {
            // A right button that only ever went down and up is a click, not
            // a pan, and a click is how an open-ended tool is finished.
            if (Math.Abs(p.X - _panAnchor.X) > 1 || Math.Abs(p.Y - _panAnchor.Y) > 1) _panDragged = true;

            Camera.PanByScreenDelta(p.X - _panAnchor.X, p.Y - _panAnchor.Y);
            _panAnchor = p;
            Redraw();
        }
        else if (_tool is { InProgress: true })
        {
            // The preview follows the cursor, so every move is a repaint.
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
            InvalidateVisual();
            ToolChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_tool is not null)
        {
            Tool = null;
            return;
        }

        if (ZoomWindowArmed) Disarm();
        else Selection.Clear();
    }

    // ---- editing --------------------------------------------------------

    /// <summary>
    /// Hands the active tool its next point. Public for the same reason
    /// <see cref="PickAt"/> is.
    /// </summary>
    public void PlaceToolPoint(Vec2 world)
    {
        if (_tool is null) return;

        Commit(_tool.Click(world));

        InvalidateVisual();
        ToolChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends an open-ended tool, as Enter or a right click does.</summary>
    public void FinishTool()
    {
        if (_tool is null) return;

        Commit(_tool.Finish());

        InvalidateVisual();
        ToolChanged?.Invoke(this, EventArgs.Empty);
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

    private void Disarm()
    {
        ZoomWindowArmed = false;
        ZoomWindowDisarmed?.Invoke(this, EventArgs.Empty);
    }

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
