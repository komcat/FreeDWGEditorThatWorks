namespace FreeDwg.Core.Geometry;

/// <summary>A straight run between two points, used where a path would be overkill.</summary>
public readonly record struct Segment2(Vec2 A, Vec2 B);
