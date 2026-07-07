using System.Globalization;
using System.Speech.Recognition;

namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// Caption source backed by <c>System.Speech</c> (the classic Windows SAPI
/// desktop recognizer). Chosen for the demo because it:
///   * runs fully offline with no API keys or accounts,
///   * ships with Windows, so it transcribes the microphone immediately, and
///   * cleanly exposes both interim ("hypothesized") and final results, which map
///     exactly onto the live-caption vs. transcript-history distinction.
///
/// Accuracy is modest compared with modern cloud/on-device models — the point of
/// this app is the *accessible presentation* of captions, not the engine. Because
/// everything is behind <see cref="ICaptionSource"/>, a higher-accuracy backend can
/// be dropped in without changing the UI. See README for upgrade paths.
///
/// Threading note: System.Speech raises its events on a worker thread. This class
/// forwards them verbatim; the UI layer is responsible for marshalling back onto the
/// dispatcher.
/// </summary>
public sealed class SystemSpeechCaptionSource : ICaptionSource
{
    private SpeechRecognitionEngine? _engine;
    private bool _disposed;

    public event EventHandler<CaptionTextEventArgs>? PartialRecognized;
    public event EventHandler<CaptionTextEventArgs>? FinalRecognized;
    public event EventHandler<CaptionStateChangedEventArgs>? StateChanged;

    public CaptionState State { get; private set; } = CaptionState.Stopped;

    public string EngineDescription { get; private set; } = "Windows offline speech (System.Speech / SAPI)";

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State is CaptionState.Listening or CaptionState.Starting)
            return;

        SetState(CaptionState.Starting, "Starting the speech recognizer…");

        try
        {
            _engine = CreateEngine();

            // Dictation grammar = free-form speech-to-text (as opposed to a fixed
            // command grammar). This is what makes it behave like live captioning.
            _engine.LoadGrammar(new DictationGrammar { Name = "Dictation" });

            _engine.SpeechHypothesized += OnSpeechHypothesized;
            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.RecognizeCompleted += OnRecognizeCompleted;
            _engine.AudioStateChanged += OnAudioStateChanged;

            _engine.SetInputToDefaultAudioDevice();

            // Multiple = keep recognizing continuously until we stop it.
            _engine.RecognizeAsync(RecognizeMode.Multiple);

            SetState(CaptionState.Listening, "Listening. Speak into your microphone.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("audio", StringComparison.OrdinalIgnoreCase))
        {
            Fail("No microphone was found, or it is in use by another app. " +
                 "Plug in / enable a microphone and try again.");
        }
        catch (Exception ex)
        {
            Fail(DescribeStartFailure(ex));
        }
    }

    public void Stop()
    {
        if (_engine is null)
        {
            SetState(CaptionState.Stopped, "Stopped.");
            return;
        }

        try
        {
            _engine.RecognizeAsyncCancel();
        }
        catch
        {
            // Best effort — we are tearing down anyway.
        }

        SetState(CaptionState.Stopped, "Stopped.");
    }

    private SpeechRecognitionEngine CreateEngine()
    {
        // Prefer an en-US recognizer, but fall back to whatever recognizer the
        // machine actually has installed rather than throwing.
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        if (installed.Count == 0)
        {
            throw new PlatformNotSupportedException(
                "No Windows speech recognizer is installed. In Windows Settings, add a " +
                "speech language pack (Time & language → Language & region → your " +
                "language → Language options → Speech).");
        }

        var preferred =
            installed.FirstOrDefault(r => r.Culture.Equals(CultureInfo.CurrentUICulture)) ??
            installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en") ??
            installed[0];

        EngineDescription = $"Windows offline speech (System.Speech / SAPI) – {preferred.Culture.DisplayName}";
        return new SpeechRecognitionEngine(preferred);
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        var text = e.Result?.Text;
        if (!string.IsNullOrWhiteSpace(text))
            PartialRecognized?.Invoke(this, new CaptionTextEventArgs(text));
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var text = e.Result?.Text;
        if (!string.IsNullOrWhiteSpace(text))
            FinalRecognized?.Invoke(this, new CaptionTextEventArgs(text));
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Error is not null && State == CaptionState.Listening)
            Fail("The speech recognizer stopped unexpectedly: " + e.Error.Message);
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs e)
    {
        // Surface a gentle status hint when the mic goes silent vs. picks up speech.
        if (State != CaptionState.Listening)
            return;

        var hint = e.AudioState switch
        {
            AudioState.Speech => "Hearing speech…",
            AudioState.Silence => "Listening (silence).",
            AudioState.Stopped => "Microphone stopped.",
            _ => null
        };

        if (hint is not null)
            StateChanged?.Invoke(this, new CaptionStateChangedEventArgs(CaptionState.Listening, hint));
    }

    private static string DescribeStartFailure(Exception ex) => ex switch
    {
        PlatformNotSupportedException => ex.Message,
        _ => "Could not start speech recognition: " + ex.Message
    };

    private void Fail(string message) => SetState(CaptionState.Error, message);

    private void SetState(CaptionState state, string? message)
    {
        State = state;
        StateChanged?.Invoke(this, new CaptionStateChangedEventArgs(state, message));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_engine is not null)
        {
            try { _engine.RecognizeAsyncCancel(); } catch { /* ignore */ }
            _engine.SpeechHypothesized -= OnSpeechHypothesized;
            _engine.SpeechRecognized -= OnSpeechRecognized;
            _engine.RecognizeCompleted -= OnRecognizeCompleted;
            _engine.AudioStateChanged -= OnAudioStateChanged;
            _engine.Dispose();
            _engine = null;
        }
    }
}
