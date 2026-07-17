using AccessibleLiveCaptions.Audio;
using SherpaOnnx;

namespace AccessibleLiveCaptions.Speech;

/// <summary>
/// True-streaming on-device caption engine backed by a NeMo cache-aware streaming
/// FastConformer transducer running under sherpa-onnx (native win-arm64, CPU).
///
/// Unlike Whisper, this is a real streaming recognizer: audio is fed continuously and
/// tokens come out as they are spoken (~80 ms algorithmic latency), so interim captions
/// update word by word without re-transcribing the whole utterance. Endpointing
/// (trailing-silence rules inside sherpa-onnx) decides when a line is final; the model
/// emits lowercase text without punctuation, so finals get a leading capital only.
///
/// Consumes normalized 16 kHz mono frames from any <see cref="IAudioCaptureSource"/> —
/// microphone or system loopback — exactly like <see cref="WhisperCaptionSource"/>.
/// A single worker thread owns the recognizer, so decoding never overlaps.
/// </summary>
public sealed class SherpaCaptionSource : ICaptionSource
{
    private const int Rate = 16000;

    private readonly IAudioCaptureSource _capture;

    private readonly object _gate = new();
    private readonly List<float> _incoming = new();

    private OnlineRecognizer? _recognizer;
    private OnlineStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private bool _disposed;

    public SherpaCaptionSource(IAudioCaptureSource capture)
    {
        _capture = capture;
        EngineDescription = $"Streaming on-device ({SherpaModelStore.DisplayName}) — {capture.Description}";
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
            SetState(CaptionState.Starting, "Preparing the on-device streaming model…");
            await SherpaModelStore.EnsureAsync(msg => SetState(CaptionState.Starting, msg), ct);

            SetState(CaptionState.Starting, "Loading the streaming model into memory…");
            await Task.Run(CreateRecognizer, ct);

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
            SetState(CaptionState.Error, "Could not start the streaming engine: " + ex.Message);
        }
    }

    private void CreateRecognizer()
    {
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = Rate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = SherpaModelStore.EncoderPath;
        config.ModelConfig.Transducer.Decoder = SherpaModelStore.DecoderPath;
        config.ModelConfig.Transducer.Joiner = SherpaModelStore.JoinerPath;
        config.ModelConfig.Tokens = SherpaModelStore.TokensPath;
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.NumThreads = 4;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        // Endpointing: a line is finalized after ~0.9 s of silence following speech,
        // ~2.4 s of leading silence with nothing decoded resets the stream, and any
        // utterance longer than 15 s is force-finalized so lines stay reviewable.
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 2.4f;
        config.Rule2MinTrailingSilence = 0.9f;
        config.Rule3MinUtteranceLength = 15f;

        _recognizer = new OnlineRecognizer(config);
        _stream = _recognizer.CreateStream();
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
        var lastPartial = "";

        // System loopback delivers no frames at all while nothing is rendering, so the
        // recognizer would never see the trailing silence its endpoint rules count.
        // Track wall-clock vs. samples fed, and top up with real silence whenever the
        // capture goes quiet, so lines still finalize when the audio stops.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long fedSamples = 0;

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

            try
            {
                if (_recognizer is null || _stream is null)
                    break;

                if (chunk is { Length: > 0 })
                {
                    _stream.AcceptWaveform(Rate, chunk);
                    fedSamples += chunk.Length;
                    // Real audio re-anchors the clock so silence-fill only ever covers
                    // gaps where the capture was genuinely idle.
                    if (fedSamples > (long)(clock.Elapsed.TotalSeconds * Rate))
                    {
                        clock.Restart();
                        fedSamples = 0;
                    }
                }
                else
                {
                    long deficit = (long)(clock.Elapsed.TotalSeconds * Rate) - fedSamples;
                    if (deficit >= Rate / 10)
                    {
                        _stream.AcceptWaveform(Rate, new float[deficit]);
                        fedSamples += deficit;
                    }
                }

                while (_recognizer.IsReady(_stream))
                    _recognizer.Decode(_stream);

                var text = _recognizer.GetResult(_stream).Text.Trim();

                if (_recognizer.IsEndpoint(_stream))
                {
                    if (text.Length > 0)
                    {
                        FinalRecognized?.Invoke(this, new CaptionTextEventArgs(Finalize(text)));
                        lastPartial = "";
                    }
                    _recognizer.Reset(_stream);
                }
                else if (text.Length > 0 && text != lastPartial)
                {
                    lastPartial = text;
                    PartialRecognized?.Invoke(this, new CaptionTextEventArgs(text));
                }
            }
            catch (Exception ex)
            {
                SetState(CaptionState.Error, "Streaming recognition error: " + ex.Message);
                break;
            }

            try { await Task.Delay(80, ct); }
            catch (OperationCanceledException) { break; }
        }

        // Don't lose the words spoken just before Stop: pad with silence so the
        // encoder has right-context for the last word, flush, and emit a final line.
        try
        {
            if (_recognizer is not null && _stream is not null)
            {
                _stream.AcceptWaveform(Rate, new float[(int)(0.8 * Rate)]);
                while (_recognizer.IsReady(_stream))
                    _recognizer.Decode(_stream);
                var tail = _recognizer.GetResult(_stream).Text.Trim();
                if (tail.Length > 0)
                    FinalRecognized?.Invoke(this, new CaptionTextEventArgs(Finalize(tail)));
            }
        }
        catch
        {
            // Disposal race on shutdown — the transcript already has everything final.
        }
    }

    /// <summary>The transducer emits lowercase text; give finalized lines a capital.</summary>
    private static string Finalize(string text) =>
        text.Length > 0 && char.IsLower(text[0])
            ? char.ToUpper(text[0]) + text[1..]
            : text;

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
        _stream?.Dispose();
        _recognizer?.Dispose();
        _capture.Dispose();
        _cts?.Dispose();
    }
}
