using System.Windows;
using FreeDwg.Core.Rendering;
using FreeDWGEditorThatWorks.Rendering;

namespace FreeDWGEditorThatWorks;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Core has no font stack, so its text bounds are a crude per-character
        // estimate until a real measurer is installed. Without this, a
        // text-heavy drawing zooms to visibly wrong extents.
        TextMetrics.Measure = WpfText.MeasureWidth;

        base.OnStartup(e);
    }
}
