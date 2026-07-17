using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace AccessibleLiveCaptions.Audio;

/// <summary>
/// Detects a dead process-loopback tap. On some audio devices (observed with a
/// "Hi-Res Audio" headphone output, likely hardware-offloaded rendering) the Windows
/// process-loopback API delivers only silence — in BOTH include and exclude modes —
/// while ordinary device loopback still hears everything. The capture keeps "working"
/// (silent packets flow), so the only symptom is a transcript of hallucinated noise.
///
/// The watchdog compares the energy we actually captured against the peak meters of
/// the audio sessions that *should* be reaching us (per-mode filter, so a screen
/// reader speaking while correctly excluded can't false-trigger). If reference
/// sessions are clearly audible while our tap stays silent for several consecutive
/// seconds, <see cref="TapSilent"/> fires once so the owner can fall back or warn.
/// </summary>
public sealed class ProcessLoopbackWatchdog : IDisposable
{
    private const double ReferencePeakThreshold = 0.04; // session meter: clearly audible
    private const double CapturedMaxThreshold = 0.002;  // our samples: essentially silence
    private const int ConsecutiveChecksToFire = 3;      // seconds of disagreement

    private readonly Func<int, string, bool> _isReferenceSession;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private float _capturedMax;
    private int _strikes;
    private bool _fired;

    /// <param name="isReferenceSession">
    /// Given a session's (pid, process name), return true if that session's audio is
    /// expected to reach this capture — i.e. it counts as evidence the tap is dead.
    /// </param>
    public ProcessLoopbackWatchdog(Func<int, string, bool> isReferenceSession)
    {
        _isReferenceSession = isReferenceSession;
        _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    }

    /// <summary>Fired (once, on a threadpool thread) when the tap looks dead.</summary>
    public event EventHandler? TapSilent;

    /// <summary>Feed every captured frame so the watchdog knows what we received.</summary>
    public void NoteSamples(float[] samples)
    {
        float max = 0;
        foreach (var s in samples)
        {
            var a = Math.Abs(s);
            if (a > max)
                max = a;
        }
        lock (_gate)
            _capturedMax = Math.Max(_capturedMax, max);
    }

    private void Check()
    {
        if (_fired)
            return;

        float captured;
        lock (_gate)
        {
            captured = _capturedMax;
            _capturedMax = 0;
        }

        double reference = 0;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid == 0 || pid == Environment.ProcessId)
                        continue;
                    using var proc = Process.GetProcessById(pid);
                    if (_isReferenceSession(pid, proc.ProcessName))
                        reference = Math.Max(reference, session.AudioMeterInformation.MasterPeakValue);
                }
                catch
                {
                    // Session or process gone mid-enumeration; skip it.
                }
            }
        }
        catch
        {
            return; // No default device or enumeration failed — nothing to judge.
        }

        if (reference > ReferencePeakThreshold && captured < CapturedMaxThreshold)
        {
            if (++_strikes >= ConsecutiveChecksToFire)
            {
                _fired = true;
                _timer.Dispose();
                TapSilent?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            _strikes = 0;
        }
    }

    public void Dispose() => _timer.Dispose();
}
