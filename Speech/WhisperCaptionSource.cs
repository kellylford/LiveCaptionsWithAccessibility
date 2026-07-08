using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using AccessibleLiveCaptions.Audio;
using Whisper.net;
using Whisper.net.Ggml;

namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// High-accuracy, fully on-device caption engine backed by Whisper (via Whisper.net's
/// CPU runtime, which runs natively on Windows ARM64 / Snapdragon). Consumes normalized
/// 16 kHz mono frames from any <see cref="IAudioCaptureSource"/> — microphone or system
/// loopback — so the same engine captions your voice or a meeting.
///
/// Whisper is not a streaming recognizer, so we do lightweight real-time segmentation:
///   * accumulate audio into the current "utterance",
///   * a simple energy VAD tracks trailing silence,
///   * every ~0.7 s we re-transcribe the utterance-so-far and emit it as an interim
///     ("Now hearing") caption,
///   * when ~0.6 s of silence follows speech (or the utterance grows past ~12 s) we
///     transcribe once more and emit a final line, then reset.
///
/// A single worker thread owns the Whisper processor, so inference never overlaps.
/// </summary>
public sealed class WhisperCaptionSource : ICaptionSource
{
    private const int Rate = 16000;
    private const int SilenceToFinalizeSamples = (int)(0.6 * Rate);
    private const int MinVoicedSamples = (int)(0.3 * Rate);
    private const int MaxUtteranceSamples = 12 * Rate;
    private const int PartialIntervalMs = 700;
    private const float VoiceRmsThreshold = 0.012f;

    private readonly IAudioCaptureSource _capture;
    private readonly GgmlType _model;

    private readonly object _gate = new();
    private readonly List<float> _incoming = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private bool _disposed;

    public WhisperCaptionSource(IAudioCaptureSource capture, GgmlType model = GgmlType.BaseEn)
    {
        _capture = capture;
        _model = model;
        EngineDescription = $"Whisper on-device ({model}) — {capture.Description}";
    }

    public event EventHandler<CaptionTextEventArgs>? PartialRecognized;
    public event EventHandler<CaptionTextEventArgs>? FinalRecognized;
    public event EventHandler<CaptionStateChangedEventArgs>? StateChanged;

    public CaptionState State { get; private set; } = CaptionState.Stopped;
    public string EngineDescription { get; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is CaptionState.Listening or CaptionState.Starting)
            return;

        _cts = new CancellationTokenSource();
        _ = StartAsync(_cts.Token);
    }

    private async Task StartAsync(CancellationToken ct)
    {
        try
        {
            SetState(CaptionState.Starting, "Preparing the on-device Whisper model…");
            var modelPath = await EnsureModelAsync(_model, ct);

            SetState(CaptionState.Starting, "Loading the speech model into memory…");
            _factory = WhisperFactory.FromPath(modelPath);
            _processor = _factory.CreateBuilder().WithLanguage("en").Build();

            _capture.FrameAvailable += OnFrame;
            _capture.Failed += OnCaptureFailed;
            _capture.Start();

            _worker = Task.Run(() => WorkerLoop(ct), ct);
            SetState(CaptionState.Listening, "Listening. Captions appear as speech is detected.");
        }
        catch (OperationCanceledException)
        {
            // Stopped during startup — nothing to report.
        }
        catch (Exception ex)
        {
            SetState(CaptionState.Error, "Could not start Whisper: " + ex.Message);
        }
    }

    public void Stop()
    {
        if (State == CaptionState.Stopped)
            return;

        _cts?.Cancel();
        _capture.FrameAvailable -= OnFrame;
        _capture.Failed -= OnCaptureFailed;
        _capture.Stop();

        lock (_gate)
            _incoming.Clear();

        SetState(CaptionState.Stopped, "Stopped.");
    }

    private void OnFrame(object? sender, AudioFrameEventArgs e)
    {
        lock (_gate)
            _incoming.AddRange(e.Samples);
    }

    private void OnCaptureFailed(object? sender, string message) =>
        SetState(CaptionState.Error, message);

    private async Task WorkerLoop(CancellationToken ct)
    {
        var utterance = new List<float>(MaxUtteranceSamples);
        int silenceRun = 0;
        bool hasVoice = false;
        var sinceLastPartial = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            float[]? chunk = null;
            lock (_gate)
            {
                if (_incoming.Count > 0)
                {
                    chunk = _incoming.ToArray();
                    _incoming.Clear();
                }
            }

            if (chunk is { Length: > 0 })
            {
                bool voiced = Rms(chunk) > VoiceRmsThreshold;
                if (voiced)
                {
                    hasVoice = true;
                    silenceRun = 0;
                }
                else
                {
                    silenceRun += chunk.Length;
                }
                utterance.AddRange(chunk);
            }

            bool endOfSpeech = hasVoice
                && CountVoiced(utterance) >= MinVoicedSamples
                && silenceRun >= SilenceToFinalizeSamples;
            bool tooLong = utterance.Count >= MaxUtteranceSamples;

            if ((endOfSpeech || tooLong) && utterance.Count > 0)
            {
                var text = await Transcribe(utterance.ToArray());
                if (!string.IsNullOrWhiteSpace(text))
                    FinalRecognized?.Invoke(this, new CaptionTextEventArgs(text));

                utterance.Clear();
                silenceRun = 0;
                hasVoice = false;
                sinceLastPartial.Restart();
            }
            else if (hasVoice && utterance.Count >= MinVoicedSamples
                     && sinceLastPartial.ElapsedMilliseconds >= PartialIntervalMs)
            {
                var text = await Transcribe(utterance.ToArray());
                if (!string.IsNullOrWhiteSpace(text))
                    PartialRecognized?.Invoke(this, new CaptionTextEventArgs(text));
                sinceLastPartial.Restart();
            }

            try { await Task.Delay(120, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<string> Transcribe(float[] samples)
    {
        if (_processor is null)
            return string.Empty;

        var sb = new StringBuilder();
        try
        {
            await foreach (var seg in _processor.ProcessAsync(samples))
                sb.Append(seg.Text);
        }
        catch (Exception ex)
        {
            SetState(CaptionState.Error, "Transcription error: " + ex.Message);
            return string.Empty;
        }
        return CleanUp(sb.ToString());
    }

    /// <summary>Whisper emits bracketed non-speech tokens like [BLANK_AUDIO]; drop them.</summary>
    private static string CleanUp(string text)
    {
        text = text.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
            return string.Empty;
        return text;
    }

    private static float Rms(float[] samples)
    {
        double sum = 0;
        foreach (var s in samples)
            sum += s * (double)s;
        return (float)Math.Sqrt(sum / Math.Max(1, samples.Length));
    }

    // Rough voiced-sample count so a long silence doesn't count as a real utterance.
    private static int CountVoiced(List<float> utterance)
    {
        const int win = 1600; // 100 ms windows
        int voiced = 0;
        for (int i = 0; i + win <= utterance.Count; i += win)
        {
            double sum = 0;
            for (int j = 0; j < win; j++)
                sum += utterance[i + j] * (double)utterance[i + j];
            if (Math.Sqrt(sum / win) > VoiceRmsThreshold)
                voiced += win;
        }
        return voiced;
    }

    private Task<string> EnsureModelAsync(GgmlType type, CancellationToken ct) =>
        WhisperModelStore.EnsureAsync(type, msg => SetState(CaptionState.Starting, msg), ct);

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

        Stop();
        try { _worker?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _processor?.Dispose();
        _factory?.Dispose();
        _capture.Dispose();
        _cts?.Dispose();
    }
}
