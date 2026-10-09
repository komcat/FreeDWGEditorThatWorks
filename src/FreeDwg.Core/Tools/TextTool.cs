using FreeDwg.Core.Geometry;
using FreeDwg.Core.Rendering;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Scene.Entities;
using FreeDwg.Core.Styling;

namespace FreeDwg.Core.Tools;

/// <summary>
/// One click for where the text goes, then the words, then Enter.
/// </summary>
/// <remarks>
/// A draw tool like the others: it takes a point and gives back an entity,
/// and holds no drawing. The difference is that the point is not enough --
/// the words arrive afterwards, typed into the shell's box, and are handed
/// over through <see cref="Lines"/> as they change so the preview shows them
/// at their real size. Enter is the early finish every open-ended tool has.
/// <para>
/// The click is the top-left of the text, as MTEXT's is. A baseline anchor
/// would be TEXT's habit, but it would make the first line jump the moment a
/// second one was started; anchored at the top, the block grows downwards
/// from where it was put, which is how text is written.
/// </para>
/// </remarks>
public sealed class TextTool : DrawTool
{
    /// <summary>MTEXT's line spacing at a factor of one: five thirds of the height.</summary>
    public const double LineSpacing = 5.0 / 3.0;

    public override string Name => "text";

    public override string Prompt => Points.Count == 0
        ? "Text: pick where the text goes"
        : "Text: type -- Enter to place it, Shift+Enter for a new line, Escape to drop it";

    /// <summary>Height, turn, justification and typeface. Pushed in by the canvas.</summary>
    public TextFormat Format { get; set; } = TextFormat.Default;

    /// <summary>Capital height of the text, in drawing units.</summary>
    public double Height
    {
        get => Format.Height;
        set => Format = Format with { Height = value };
    }

    /// <summary>What has been typed so far, one entry per line.</summary>
    public IReadOnlyList<string> Lines { get; set; } = [];

    /// <summary>Whether there is anything worth placing: text that is all blank is not text.</summary>
    public bool HasWords => Lines.Any(line => !string.IsNullOrWhiteSpace(line));

    /// <summary>
    /// The first point alone is never enough; the words come before Enter.
    /// A second click is the user moving on: with words typed it places them,
    /// as clicking away does in any text box, and with none it moves where
    /// the text will start.
    /// </summary>
    protected override SceneEntity? Build(Vec2 last)
    {
        if (Points.Count < 2) return null;

        if (!HasWords)
        {
            ClearPoints();
            AddPoint(last);
            return null;
        }

        var text = Make(Points[0], Trimmed(Lines), Format);
        Lines = [];
        return text;
    }

    protected override SceneEntity? BuildPartial() =>
        Points.Count > 0 && HasWords ? Make(Points[0], Trimmed(Lines), Format) : null;

    public override void Cancel()
    {
        base.Cancel();
        Lines = [];
    }

    public override SceneEntity? Finish()
    {
        var entity = base.Finish();
        Lines = [];
        return entity;
    }

    /// <summary>
    /// Text as the tool makes it, for the tool and for an edit made to
    /// existing text through the same box.
    /// </summary>
    public static SText Make(Vec2 at, IReadOnlyList<string> lines, double height) =>
        Make(at, lines, TextFormat.Default with { Height = height });

    /// <remarks>
    /// Several lines on a baseline anchor are re-anchored at the top, with
    /// the first line kept exactly where it was. MTEXT, which several lines
    /// are saved as, has no baseline; anchored at its bottom instead, it
    /// would come back from a file standing on its last line.
    /// </remarks>
    public static SText Make(Vec2 at, IReadOnlyList<string> lines, in TextFormat format)
    {
        var text = new SText(lines.ToArray(), at, format.Height)
        {
            Rotation = format.Rotation,
            AnchorX = format.AnchorX,
            AnchorY = format.AnchorY,
            LineStep = format.Height * LineSpacing,
            FontFamily = format.FontFamily,
            Bold = format.Bold,
            Italic = format.Italic,
        };

        if (lines.Count > 1 && text.AnchorY == TextAnchorY.Baseline)
        {
            text.Position += new Vec2(-Math.Sin(text.Rotation), Math.Cos(text.Rotation)) * text.Height;
            text.AnchorY = TextAnchorY.Top;
        }

        return text;
    }

    /// <summary>Blank lines at the end are a stray Shift+Enter, not part of the text.</summary>
    public static IReadOnlyList<string> Trimmed(IReadOnlyList<string> lines)
    {
        int count = lines.Count;
        while (count > 1 && string.IsNullOrWhiteSpace(lines[count - 1])) count--;
        return lines.Take(count).ToArray();
    }

    public override void Preview(Vec2 cursor, in EmitContext context, in DisplayStyle style)
    {
        if (Points.Count == 0) return;

        var at = Points[0];

        if (HasWords)
        {
            Make(at, Lines, Format).Emit(context, style);
            return;
        }

        // Nothing typed yet: a bar as tall as the text will be, standing on
        // the click the way the anchor will hang it, so it is plain where
        // the words will start, how big, and which way they lean.
        var up = new Vec2(-Math.Sin(Format.Rotation), Math.Cos(Format.Rotation)) * Height;
        var (low, high) = Format.AnchorY switch
        {
            TextAnchorY.Top => (at - up, at),
            TextAnchorY.Middle => (at - up * 0.5, at + up * 0.5),
            _ => (at, at + up),
        };
        Stroke(context, style, [low, high], closed: false);
    }
}

/// <summary>
/// How text is placed: its height, its turn, which point of it sits on the
/// click, and its typeface. One value, so the tool, the canvas and an edit
/// made through the text box all hand the same thing about.
/// </summary>
public readonly record struct TextFormat(
    double Height,
    double Rotation,
    TextAnchorX AnchorX,
    TextAnchorY AnchorY,
    string? FontFamily,
    bool Bold,
    bool Italic)
{
    /// <summary>Hung from its top-left, as MTEXT is, in the standard typeface.</summary>
    public static TextFormat Default { get; } =
        new(2.5, 0, TextAnchorX.Left, TextAnchorY.Top, null, false, false);

    /// <summary>How <paramref name="text"/> is placed now.</summary>
    public static TextFormat Of(SText text) =>
        new(text.Height, text.Rotation, text.AnchorX, text.AnchorY, text.FontFamily, text.Bold, text.Italic);
}
