namespace AccessibleLiveCaptions.Audio;

public sealed class AudioFrameEventArgs(float[] samples) : EventArgs
{
    /// <summary>Mono PCM samples in [-1, 1] at <see cref="IAudioCaptureSource.SampleRate"/>.</summary>
    public float[] Samples { get; } = samples;
}

/// <summary>
/// A source of microphone-or-system audio, normalized to the format speech engines
/// want: 16 kHz, mono, floating-point. Whether the bytes come from the microphone or
/// from the system's render loopback (everything you hear) is an implementation detail
/// the recognizer never has to care about.
/// </summary>
public interface IAudioCaptureSource : IDisposable
{
    /// <summary>Raised as normalized 16 kHz mono frames become available.</summary>
    event EventHandler<AudioFrameEventArgs>? FrameAvailable;

    /// <summary>Raised if capture cannot start or stops unexpectedly.</summary>
    event EventHandler<string>? Failed;

    /// <summary>Target sample rate of frames handed to consumers.</summary>
    int SampleRate { get; }

    string Description { get; }

    void Start();
    void Stop();
}
