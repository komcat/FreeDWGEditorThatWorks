using System.Text;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// What a save did, and anything in the drawing it could not write.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="ImportDiagnostics"/>. An edit the writer
/// could not express is reported rather than dropped: a file that silently
/// comes back without the change someone made is worse than one that says so.
/// </remarks>
public sealed class SaveReport
{
    private readonly List<string> _notes = new();

    public int Created { get; internal set; }
    public int Modified { get; internal set; }
    public int Deleted { get; internal set; }
    public int LayersCreated { get; internal set; }
    public int LayersModified { get; internal set; }
    public int LayersDeleted { get; internal set; }

    /// <summary>The path written, and the format version it was written as.</summary>
    public string Path { get; internal set; } = "";
    public string Version { get; internal set; } = "";

    /// <summary>Where the previous file went, if one was replaced.</summary>
    public string? BackupPath { get; internal set; }

    /// <summary>Edits that were not written, or were written differently from how they were made.</summary>
    public IReadOnlyList<string> Notes => _notes;

    internal void Note(string message)
    {
        // A save of a drawing with a thousand unsupported edits should still
        // fit on a screen; the first few say what the problem is.
        if (_notes.Count < 200) _notes.Add(message);
    }

    /// <summary>One line for a status bar.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append("Saved ").Append(System.IO.Path.GetFileName(Path)).Append(" (").Append(Version).Append(')');

        var parts = new List<string>();
        if (Created > 0) parts.Add($"{Created} new");
        if (Modified > 0) parts.Add($"{Modified} changed");
        if (Deleted > 0) parts.Add($"{Deleted} deleted");
        int layers = LayersCreated + LayersModified + LayersDeleted;
        if (layers > 0) parts.Add($"{layers} layer change{(layers == 1 ? "" : "s")}");

        if (parts.Count > 0) sb.Append(": ").AppendJoin(", ", parts);
        if (_notes.Count > 0) sb.Append(" -- ").Append(_notes.Count).Append(" not written as drawn (").Append(_notes[0]).Append(')');
        return sb.ToString();
    }

    public override string ToString() => Summary();
}
