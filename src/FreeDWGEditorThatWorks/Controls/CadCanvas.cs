using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeDwg.Core.Geometry;
using FreeDwg.Core.Picking;
using FreeDwg.Core.Rendering;
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
    private Point _panAnchor;
    private bool _zoomExtentsPending;

    private bool _isBanding;
    private Point _pickAnchor;
    private Point _bandCorner;

    /// <summary>How far a click may miss by, in device pixels.</summary>
    private const double PickRadiusPixels = 6.0;

    /// <summary>Drag further than this and a click becomes a selection window.</summary>
    private const double DragThresholdPixels = 4.0;

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

    public SceneDrawing? Drawing
    {
        get => _drawing;
        set
        {
            _drawing = value;
            Selection.Clear();
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

        if (_isBanding) DrawSelectionBand(dc);
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
            _panAnchor = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _drawing is not null)
        {
            _pickAnchor = e.GetPosition(this);
            _bandCorner = _pickAnchor;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);

        if (_isPanning)
        {
            Camera.PanByScreenDelta(p.X - _panAnchor.X, p.Y - _panAnchor.Y);
            _panAnchor = p;
            Redraw();
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
            SelectAtPoint(p, extend);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key != Key.Escape) return;

        if (_isBanding)
        {
            _isBanding = false;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        if (ZoomWindowArmed) Disarm();
        else Selection.Clear();

        e.Handled = true;
    }

    private void Disarm()
    {
        ZoomWindowArmed = false;
        ZoomWindowDisarmed?.Invoke(this, EventArgs.Empty);
    }

    private void SelectAtPoint(Point device, bool extend)
    {
        if (_drawing is null) return;

        // A click is not a window, so an armed zoom has nothing to frame.
        if (ZoomWindowArmed)
        {
            Disarm();
            return;
        }

        var world = Camera.ScreenToWorld(new Vec2(device.X, device.Y));
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
