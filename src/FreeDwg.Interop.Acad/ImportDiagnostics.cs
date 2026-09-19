using System.Text;
using ACadSharp.Entities;
using ACadSharp.IO;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// Collects everything the importer could not fully honour: ACadSharp's own
/// reader notifications plus our own unsupported-entity tally.
/// </summary>
/// <remarks>
/// During the reader phase this is the primary development tool -- the
/// frequency table tells you which converter to write next, ranked by what
/// real drawings actually contain rather than by guesswork.
/// </remarks>
public sealed class ImportDiagnostics
{
    private readonly List<string> _messages = new();
    private readonly Dictionary<string, int> _unsupported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fontSubstitutions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Messages => _messages;

    /// <summary>Entity DXF names we skipped, and how many of each.</summary>
    public IReadOnlyDictionary<string, int> UnsupportedEntities => _unsupported;

    /// <summary>SHX fonts drawn with an outline substitute, keyed by the requested font.</summary>
    public IReadOnlyDictionary<string, string> FontSubstitutions => _fontSubstitutions;

    public int SkippedCount { get; private set; }
    public int ImportedCount { get; internal set; }
    public bool HasErrors { get; private set; }

    internal void OnNotification(NotificationEventArgs e)
    {
        if (e.NotificationType == NotificationType.Error) HasErrors = true;

        // Readers are chatty on damaged files; keep the tail bounded.
        if (_messages.Count < 500)
            _messages.Add($"[{e.NotificationType}] {e.Message}");
    }

    /// <summary>Records an SHX font that had to be drawn with a substitute.</summary>
    internal void FontSubstituted(string requested, string substitute)
    {
        _fontSubstitutions[requested] = substitute;
    }

    /// <summary>Records something the importer had to work around.</summary>
    internal void Note(string message)
    {
        if (_messages.Count < 500) _messages.Add($"[Import] {message}");
    }

    internal void Unsupported(Entity entity)
    {
        string name = entity.ObjectName ?? entity.GetType().Name;
        _unsupported[name] = _unsupported.TryGetValue(name, out int n) ? n + 1 : 1;
        SkippedCount++;
    }

    /// <summary>One-line summary suitable for a status bar.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append(ImportedCount).Append(" entities");

        if (SkippedCount > 0)
        {
            sb.Append(", ").Append(SkippedCount).Append(" skipped (");
            sb.AppendJoin(", ", _unsupported
                .OrderByDescending(kv => kv.Value)
                .Take(4)
                .Select(kv => $"{kv.Key} x{kv.Value}"));
            if (_unsupported.Count > 4) sb.Append(", ...");
            sb.Append(')');
        }

        if (_fontSubstitutions.Count > 0)
            sb.Append(", ").Append(_fontSubstitutions.Count).Append(" SHX font(s) substituted");

        if (HasErrors) sb.Append(" -- reader reported errors");
        return sb.ToString();
    }
}
