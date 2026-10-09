using ACadSharp;
using ACadSharp.IO;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Scene;

namespace FreeDwg.Interop.Acad;

/// <summary>
/// An open drawing: the scene the editor works on, and the document it was
/// read from, kept together so that a save can write onto the original.
/// </summary>
/// <remarks>
/// The document is the reason this exists. A DWG holds far more than the
/// scene models -- xdata, extension dictionaries, proxy objects, annotation
/// scales, every entity kind the reader skips -- and a save that regenerated
/// a file from the scene would quietly destroy all of it, a little more each
/// time the file was opened here. Writing the scene's changes onto the
/// document that was read leaves everything else exactly as it was.
/// <para>
/// Core never sees this: it is the only object that holds both halves, and
/// it lives on the side that knows what a handle is.
/// </para>
/// </remarks>
public sealed class DwgSession
{
    private readonly CadDocument _document;
    private readonly SaveState _state = new();

    private DwgSession(CadDocument document, Drawing drawing, string? path)
    {
        _document = document;
        Drawing = drawing;
        Path = path;
    }

    public Drawing Drawing { get; }

    /// <summary>Where this was read from or last saved to; null until a new drawing is first saved.</summary>
    public string? Path { get; private set; }

    /// <summary>The release whose format a save writes, e.g. "AutoCAD 2018".</summary>
    public string FormatName => NameOf(WritableVersion(_document.Header.Version));

    public static DwgSession Open(string path, ImportDiagnostics? diagnostics = null)
    {
        var diag = diagnostics ?? new ImportDiagnostics();
        var document = DwgLoader.Read(path, diag);

        var drawing = DwgLoader.Convert(document, diag);
        drawing.SourcePath = path;
        return new DwgSession(document, drawing, path);
    }

    /// <summary>
    /// An empty drawing in millimetres, backed by a fresh document so that the
    /// first save has somewhere to write.
    /// </summary>
    public static DwgSession CreateNew()
    {
        var document = new CadDocument();
        document.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Millimeters;

        return new DwgSession(document, DwgLoader.Convert(document, new ImportDiagnostics()), null);
    }

    /// <summary>
    /// Writes the drawing to <paramref name="path"/>, or back to where it came
    /// from, as DWG or DXF by the extension.
    /// </summary>
    /// <remarks>
    /// The file is written beside the target and only then swapped in, so a
    /// save that fails half way leaves the old file whole. The old file is
    /// kept as <c>.bak</c>, as AutoCAD keeps it: the writer is new, and a
    /// drawing someone has spent a week on deserves a way back.
    /// </remarks>
    public SaveReport Save(CommandStack commands, string? path = null)
    {
        path ??= Path ?? throw new InvalidOperationException("A new drawing needs a path for its first save.");
        path = System.IO.Path.GetFullPath(path);

        var report = new SaveReport { Path = path };
        new SceneWriter(_document, Drawing, _state, report).Run(commands.Summarize());

        var version = WritableVersion(_document.Header.Version);
        if (version != _document.Header.Version)
            report.Note($"{NameOf(_document.Header.Version)} format cannot be written; saved as {NameOf(version)}.");

        _document.Header.Version = version;
        report.Version = NameOf(version);

        string temp = path + ".saving";
        try
        {
            using (var stream = File.Create(temp))
            {
                NotificationEventHandler notify = (_, e) =>
                {
                    // The writer says so when it meets something it cannot
                    // write, and that is data the file is about to lose.
                    if (e.NotificationType is NotificationType.Error or NotificationType.NotImplemented)
                        report.Note($"Writer: {e.Message}");
                };

                if (DwgLoader.IsDxf(path)) DxfWriter.Write(stream, _document, binary: false, notification: notify);
                else DwgWriter.Write(stream, _document, notification: notify);
            }

            if (File.Exists(path))
            {
                string backup = System.IO.Path.ChangeExtension(path, ".bak");
                File.Replace(temp, path, backup, ignoreMetadataErrors: true);
                report.BackupPath = backup;
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        Path = path;
        Drawing.SourcePath = path;
        commands.MarkSaved();
        return report;
    }

    /// <summary>
    /// The version a save writes. ACadSharp cannot write R2007 at all, so a
    /// 2007 file goes up to 2010 rather than down to 2004, which would lose
    /// whatever 2007 added; anything older than R14 goes up to 2000.
    /// </summary>
    private static ACadVersion WritableVersion(ACadVersion version) => version switch
    {
        ACadVersion.AC1021 => ACadVersion.AC1024,
        ACadVersion.AC1014 or ACadVersion.AC1015 or ACadVersion.AC1018 or
        ACadVersion.AC1024 or ACadVersion.AC1027 or ACadVersion.AC1032 => version,
        _ => ACadVersion.AC1015,
    };

    private static string NameOf(ACadVersion version) => version switch
    {
        ACadVersion.AC1014 => "AutoCAD R14",
        ACadVersion.AC1015 => "AutoCAD 2000",
        ACadVersion.AC1018 => "AutoCAD 2004",
        ACadVersion.AC1021 => "AutoCAD 2007",
        ACadVersion.AC1024 => "AutoCAD 2010",
        ACadVersion.AC1027 => "AutoCAD 2013",
        ACadVersion.AC1032 => "AutoCAD 2018",
        _ => version.ToString(),
    };
}
