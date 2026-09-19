namespace FreeDwg.Core.Styling;

/// <summary>
/// Which parts of an entity's style come from the block reference that draws
/// it rather than from the entity itself -- DWG's ByBlock. An entity inside a
/// block definition can inherit colour and width independently, so this is a
/// flag set rather than a single switch.
/// </summary>
[Flags]
public enum StyleInheritance
{
    None = 0,
    Color = 1,
    Lineweight = 2,
}
