using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Scene.Entities;

public sealed class SLine : SceneEntity
{
    public SLine(Vec2 start, Vec2 end) { Start = start; End = end; }

    public Vec2 Start { get; set; }
    public Vec2 End { get; set; }

    protected override Bounds2 ComputeBounds() => Bounds2.FromCorners(Start, End);

    public override void Emit(IDrawingSink sink, in DisplayStyle style)
    {
        sink.BeginFigure(Start, closed: false, style);
        sink.LineTo(End);
        sink.EndFigure();
    }
}
