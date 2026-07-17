using System.Diagnostics;

namespace AccessibleLiveCaptions.Accessibility;

/// <summary>
/// Finds the running screen reader so system-audio capture can exclude its speech
/// (see <see cref="Audio.SystemAudioExceptProcessCaptureSource"/>). Process-loopback
/// exclude mode takes a single process tree, so the first match wins; third-party
/// screen readers are checked before Narrator since they're the primary reader when
/// both are running.
/// </summary>
public static class ScreenReaderDetector
{
    // Process name (without .exe) → friendly name. JAWS covers Fusion, whose speech
    // runs under jfw.exe.
    private static readonly (string Process, string Name)[] Known =
    [
        ("nvda", "NVDA"),
        ("jfw", "JAWS"),
        ("Narrator", "Narrator"),
    ];

    /// <summary>The first running screen reader, or null if none is detected.</summary>
    public static (int Pid, string Name)? FindRunning()
    {
        foreach (var (process, name) in Known)
        {
            foreach (var p in Process.GetProcessesByName(process))
            {
                using (p)
                {
                    if (!p.HasExited)
                        return (p.Id, name);
                }
            }
        }
        return null;
    }
}
