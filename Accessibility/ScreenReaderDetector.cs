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

    // Processes whose audio IS screen-reader speech even though they aren't the
    // reader's main process — e.g. JAWS renders through its fsSynth synthesizer.
    private static readonly string[] SpeechProcesses =
        ["nvda", "jfw", "Narrator", "fsSynth32", "fsSynth64"];

    /// <summary>
    /// True when a process's audio output is screen-reader speech (used by the
    /// capture watchdog so the reader speaking alone never counts as missed audio).
    /// </summary>
    public static bool IsScreenReaderSpeechProcess(string processName) =>
        SpeechProcesses.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));

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
