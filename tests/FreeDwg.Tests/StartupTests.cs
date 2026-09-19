using System.Diagnostics;
using System.IO;

namespace FreeDwg.Tests;

/// <summary>
/// That the app actually starts.
/// </summary>
/// <remarks>
/// Nothing else here builds a window, and a surprising amount can only go
/// wrong while one is being built: a StaticResource that resolves to nothing,
/// an event handler that fires during XAML parsing and touches a field the
/// parser has not reached yet. Both produce a clean compile, a green test
/// run, and an app that dies before it draws a pixel. This has caught that
/// twice.
/// <para>
/// Runs the real executable rather than constructing the window in process,
/// because a WPF Application is a per-AppDomain singleton with thread
/// affinity: creating one here would make every other test that touches a
/// WPF object depend on which thread got there first.
/// </para>
/// </remarks>
public sealed class StartupTests
{
    [Fact]
    public void TheAppStartsAndStaysUp()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "FreeDWGEditorThatWorks.exe");
        Assert.True(File.Exists(exe), $"the shell was not built next to the tests: {exe}");

        using var app = Process.Start(new ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });

        Assert.NotNull(app);

        var errors = new System.Text.StringBuilder();
        app!.ErrorDataReceived += (_, e) => errors.AppendLine(e.Data);
        app.BeginErrorReadLine();
        app.BeginOutputReadLine();

        try
        {
            // Waiting for the process to *exit* is not the test: an unhandled
            // exception on the UI thread can leave the process alive under
            // Windows error reporting, so a crash looks like a pass. The
            // question is whether a window ever appeared.
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (DateTime.UtcNow < deadline)
            {
                if (app.WaitForExit(250))
                {
                    Assert.Fail(
                        $"the app exited with code {app.ExitCode} instead of opening a window.{Environment.NewLine}{errors}");
                }

                app.Refresh();
                if (app.MainWindowHandle != IntPtr.Zero) return;
            }

            Assert.Fail($"the app never opened a window.{Environment.NewLine}{errors}");
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
                app.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
    }
}
