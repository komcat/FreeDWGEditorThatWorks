using System.Collections.Concurrent;
using System.IO;

namespace FreeDwg.Tests.Fixtures;

/// <summary>
/// Writes each fixture drawing once per test run and hands back its path.
/// </summary>
/// <remarks>
/// The fixtures are generated rather than committed, which keeps binary DWGs
/// out of the repository and means every render test is also a write-then-read
/// round trip through the library.
/// </remarks>
public static class FixtureFiles
{
    private static readonly ConcurrentDictionary<string, string> Written = new();

    private static readonly Lazy<string> Root = new(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "FreeDwg.Tests", Environment.ProcessId.ToString());
        Directory.CreateDirectory(path);
        return path;
    });

    public static string Ensure(string name, Action<string> write) =>
        Written.GetOrAdd(name, key =>
        {
            string path = Path.Combine(Root.Value, key + ".dwg");
            write(path);
            return path;
        });
}
