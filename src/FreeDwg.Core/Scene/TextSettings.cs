namespace FreeDwg.Core.Scene;

/// <summary>
/// What a drawing says new text looks like: its height and its typeface.
/// </summary>
/// <remarks>
/// The half of the text tool's settings a drawing keeps, which is also the
/// half a DWG keeps: the height is the file's TEXTSIZE, and the typeface is
/// the current text style. Rotation and justification are chosen afresh for
/// each piece of text, in AutoCAD as here, and live on the canvas.
/// </remarks>
/// <param name="Height">Null follows the dimension text, so labels and dimensions agree.</param>
/// <param name="FontFamily">Null is the drawing's standard style, whatever that resolves to.</param>
public readonly record struct TextSettings(double? Height, string? FontFamily, bool Bold, bool Italic);
