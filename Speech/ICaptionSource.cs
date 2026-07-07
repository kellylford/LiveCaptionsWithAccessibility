namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// Current lifecycle state of a caption source.
/// </summary>
public enum CaptionState
{
    Stopped,
    Starting,
    Listening,
    Error
}

public sealed class CaptionStateChangedEventArgs(CaptionState state, string? message = null) : EventArgs
{
    public CaptionState State { get; } = state;
    public string? Message { get; } = message;
}

public sealed class CaptionTextEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

/// <summary>
/// Abstraction over "something that turns audio into caption text".
///
/// The rest of the app only depends on this interface, so the underlying engine
/// (offline SAPI dictation today; Azure Speech, Whisper, or the Windows on-device
/// recognizer tomorrow) can be swapped without touching the accessible UI.
///
/// Two kinds of result are surfaced, mirroring how real speech engines work:
///   * <see cref="PartialRecognized"/> — a live, still-changing guess ("hypothesis").
///     Shown on screen but deliberately NOT announced to a screen reader on every
///     keystroke-like change, because that would be overwhelming.
///   * <see cref="FinalRecognized"/> — a settled utterance. This is what gets added
///     to the scrollable transcript and (optionally) spoken by the screen reader.
/// </summary>
public interface ICaptionSource : IDisposable
{
    event EventHandler<CaptionTextEventArgs>? PartialRecognized;
    event EventHandler<CaptionTextEventArgs>? FinalRecognized;
    event EventHandler<CaptionStateChangedEventArgs>? StateChanged;

    CaptionState State { get; }

    /// <summary>Human-readable name of the active engine, for display.</summary>
    string EngineDescription { get; }

    void Start();
    void Stop();
}
