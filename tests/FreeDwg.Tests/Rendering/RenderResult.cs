using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Styling;

namespace FreeDwg.Tests.Rendering;

/// <summary>
/// The outcome of a render, as plain data.
/// </summary>
/// <remarks>
/// Deliberately free of WPF types. Rendering has to happen on an STA thread
/// and its objects cannot be touched from another, so what crosses back is a
/// pixel buffer plus enough of the camera to convert world coordinates into
/// indexes into it. That also keeps every assertion in the suite expressible
/// in drawing coordinates rather than pixels.
/// </remarks>
public sealed class RenderResult
{
    public required byte[] Pixels { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Bytes per row; the buffer is Pbgra32 over an opaque canvas.</summary>
    public int Stride => Width * 4;

    public required Vec2 CameraCenter { get; init; }
    public required double CameraScale { get; init; }
    public required Rgb Background { get; init; }
    public required RenderStats Stats { get; init; }

    public Vec2 WorldToScreen(Vec2 world) => new(
        (world.X - CameraCenter.X) * CameraScale + Width * 0.5,
        Height * 0.5 - (world.Y - CameraCenter.Y) * CameraScale);
}
