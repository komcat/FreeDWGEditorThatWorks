using System.Collections.Concurrent;
using System.IO;
using System.Threading;

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
    // Lazy values, not bare ones: ConcurrentDictionary may run a GetOrAdd
    // factory on more than one thread at once, and two threads writing the
    // same fixture file collide on File.Create. Two test classes sharing a
    // fixture is enough to hit it.
    private static readonly ConcurrentDictionary<string, Lazy<string>> Written = new();

    private static readonly Lazy<string> Root = new(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "FreeDwg.Tests", Environment.ProcessId.ToString());
        Directory.CreateDirectory(path);
        return path;
    });

    public static string Ensure(string name, Action<string> write) =>
        Written.GetOrAdd(name, key => new Lazy<string>(() =>
        {
            string path = Path.Combine(Root.Value, key + ".dwg");
            write(path);
            return path;
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
